import argparse
import ctypes
import hashlib
import json
import os
import re
import shutil
import socket
import struct
import threading
import time
import uuid
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath


PROTOCOL_VERSION = 2
MAX_HEADER = 256 * 1024
MAX_BODY = 256 * 1024 * 1024
MAX_TOTAL = 4 * 1024 * 1024 * 1024
SAFE_ID = re.compile(r"^[A-Za-z0-9_-]{1,128}$")
ALLOWED_MAID_FILES = {
    "maid_data.maid",
    "maid.json",
    "model.json",
    "texture.png",
    "animation.json",
    "ai_chat.json.gz",
    "attachments.json",
    "attachments.json.bak",
    "mask.png",
}
SOUND_FILE = re.compile(r"^maid/(ai|mode|environment|other)/[a-z_]+[0-9]*\.ogg$")
LIST_KINDS = {"maid", "house", "sound"}


@dataclass
class Upload:
    upload_id: str
    kind: str
    maid_id: str
    name: str
    folder: Path
    target_root: Path
    expected_files: int
    expected_bytes: int
    received_files: int = 0
    received_bytes: int = 0
    paths: set = field(default_factory=set)
    broken: bool = False
    error: str = ""


def default_save_root():
    if os.name == "nt":
        buffer = ctypes.create_unicode_buffer(260)
        result = ctypes.windll.shell32.SHGetFolderPathW(None, 5, None, 0, buffer)
        if result == 0 and buffer.value:
            return Path(buffer.value) / "MaidHome" / "saves" / "maid"
    return Path.home() / "Documents" / "MaidHome" / "saves" / "maid"


def recv_exact(sock, count):
    data = bytearray()
    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk:
            raise EOFError("连接已关闭")
        data.extend(chunk)
    return bytes(data)


def read_frame(sock):
    length = struct.unpack(">I", recv_exact(sock, 4))[0]
    if length == 0 or length > MAX_HEADER:
        raise ValueError(f"header 长度非法: {length}")
    raw = recv_exact(sock, length)
    try:
        header = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("header 不是有效 UTF-8 JSON") from exc
    if not isinstance(header, dict) or not isinstance(header.get("op"), str) or not header["op"]:
        raise ValueError("header 缺少 op")
    body_length = header.get("body", 0)
    if isinstance(body_length, bool) or not isinstance(body_length, int):
        raise ValueError("body 长度必须是整数")
    if body_length < 0 or body_length > MAX_BODY:
        raise ValueError(f"body 长度非法: {body_length}")
    body = recv_exact(sock, body_length) if body_length else b""
    return header, body


def write_frame(sock, header, body=b""):
    value = dict(header)
    if body:
        value["body"] = len(body)
    raw = json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    if not raw or len(raw) > MAX_HEADER:
        raise ValueError("响应 header 超限")
    sock.sendall(struct.pack(">I", len(raw)) + raw + body)


def send_error(sock, request, message):
    write_frame(sock, {"op": "error", "request": request, "message": message})


def safe_id(value):
    if not isinstance(value, str) or not SAFE_ID.fullmatch(value):
        raise ValueError(f"非法 ID: {value!r}")
    return value


def safe_relative(kind, value):
    if not isinstance(value, str) or not value or len(value) > 512:
        raise ValueError("非法文件路径")
    if "\\" in value or ":" in value or value.startswith("/") or "\0" in value:
        raise ValueError(f"非法文件路径: {value}")
    path = PurePosixPath(value)
    if any(part in ("", ".", "..") for part in path.parts):
        raise ValueError(f"非法文件路径: {value}")
    normalized = str(path)
    allowed = normalized in ALLOWED_MAID_FILES if kind == "maid" else bool(SOUND_FILE.fullmatch(normalized))
    if not allowed:
        raise ValueError(f"{kind} 包不允许文件: {normalized}")
    return normalized


def folder_bytes(folder):
    total = 0
    if folder.is_dir():
        for path in folder.rglob("*"):
            if path.is_file():
                total += path.stat().st_size
    return total


def discover_name(folder, fallback):
    path = folder / "maid.json"
    if path.is_file():
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
            name = data.get("name")
            if isinstance(name, str) and name.strip():
                return name.strip()
        except (OSError, ValueError, json.JSONDecodeError):
            pass
    return fallback


def promote(upload):
    target = upload.target_root / upload.maid_id
    upload.target_root.mkdir(parents=True, exist_ok=True)
    backup = target.with_name(target.name + ".old-" + time.strftime("%Y%m%d-%H%M%S"))
    moved_old = False
    try:
        if target.exists():
            os.replace(target, backup)
            moved_old = True
        os.replace(upload.folder, target)
        if moved_old:
            shutil.rmtree(backup, ignore_errors=True)
        return target
    except Exception:
        if moved_old and not target.exists() and backup.exists():
            os.replace(backup, target)
        raise


class MaidUploadServer:
    def __init__(self, host, port, save_root, once=False):
        self.host = host
        self.port = port
        self.save_root = Path(save_root).resolve()
        self.once = once
        self.staging_root = self.save_root.parent / "tmp" / "virtual_portal"
        self.sounds_root = self.save_root.parent / "sounds"
        self.ready = threading.Event()
        self.bound_port = None
        self.sock = None

    def serve(self):
        self.staging_root.mkdir(parents=True, exist_ok=True)
        self.save_root.mkdir(parents=True, exist_ok=True)
        listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind((self.host, self.port))
        listener.listen(4)
        self.sock = listener
        self.bound_port = listener.getsockname()[1]
        self.ready.set()
        print(f"[virtual-portal] listening on {self.host}:{self.bound_port}", flush=True)
        print(f"[virtual-portal] maid save root: {self.save_root}", flush=True)
        try:
            while True:
                connection, address = listener.accept()
                print(f"[virtual-portal] client {address[0]}:{address[1]}", flush=True)
                completed = self.handle(connection)
                if self.once and completed:
                    break
        finally:
            listener.close()
            self.sock = None
            print("[virtual-portal] stopped", flush=True)

    def close(self):
        if self.sock is not None:
            try:
                self.sock.close()
            except OSError:
                pass

    def handle(self, connection):
        connection.settimeout(60)
        upload = None
        hello = False
        try:
            while True:
                try:
                    header, body = read_frame(connection)
                except EOFError:
                    break
                op = header["op"]
                request = op
                if op == "hello":
                    version = header.get("version")
                    if version != PROTOCOL_VERSION:
                        send_error(connection, op, f"协议版本不一致: {version}")
                        break
                    hello = True
                    write_frame(connection, {
                        "op": "hello.ok",
                        "version": PROTOCOL_VERSION,
                        "server": "VirtualMaidServer",
                        "kinds": ["maid"],
                        "capabilities": ["maid_ai_chat_v1", "virtual_python"],
                    })
                    continue
                if not hello:
                    send_error(connection, op, "请先握手")
                    break
                if op == "ping":
                    write_frame(connection, {"op": "pong"})
                    continue
                if op == "list.request":
                    try:
                        kind = header.get("kind")
                        if kind not in LIST_KINDS:
                            raise ValueError(f"未知列表类型: {kind}")
                        items = self.list_items(kind)
                        write_frame(connection, {
                            "op": "list.ok",
                            "kind": kind,
                            "items": items,
                        })
                    except Exception as exc:
                        send_error(connection, op, str(exc))
                    continue
                if op == "upload.begin":
                    if upload is not None:
                        send_error(connection, op, "已有进行中的上传")
                        continue
                    try:
                        kind = header.get("kind")
                        if kind not in ("maid", "sound"):
                            raise ValueError("虚拟服务端当前只接受 maid / sound")
                        item_id = safe_id(header.get("id") or str(uuid.uuid4()))
                        expected_files = int(header.get("file_count", 0))
                        expected_bytes = int(header.get("total_bytes", 0))
                        max_files = 20_000 if kind == "sound" else len(ALLOWED_MAID_FILES)
                        if expected_files <= 0 or expected_files > max_files:
                            raise ValueError("file_count 非法")
                        if expected_bytes <= 0 or expected_bytes > MAX_TOTAL:
                            raise ValueError("total_bytes 非法")
                        upload_id = uuid.uuid4().hex
                        folder = self.staging_root / upload_id
                        if folder.exists():
                            shutil.rmtree(folder)
                        folder.mkdir(parents=True)
                        upload = Upload(
                            upload_id=upload_id,
                            kind=kind,
                            maid_id=item_id,
                            name=str(header.get("name") or item_id),
                            folder=folder,
                            target_root=self.sounds_root if kind == "sound" else self.save_root,
                            expected_files=expected_files,
                            expected_bytes=expected_bytes,
                        )
                        write_frame(connection, {
                            "op": "upload.begin.ok",
                            "upload_id": upload_id,
                            "kind": kind,
                            "id": item_id,
                        })
                        print(f"[virtual-portal] begin {kind} {item_id} ({expected_files} files)", flush=True)
                    except Exception as exc:
                        send_error(connection, op, str(exc))
                    continue
                if op == "upload.file":
                    if upload is None:
                        send_error(connection, op, "没有进行中的上传")
                        continue
                    try:
                        relative = safe_relative(upload.kind, header.get("path"))
                        if relative in upload.paths:
                            raise ValueError(f"重复文件: {relative}")
                        expected_hash = str(header.get("sha256") or "")
                        digest = hashlib.sha256(body).hexdigest()
                        if not expected_hash or digest != expected_hash.lower():
                            raise ValueError(f"sha256 对不上: {relative}")
                        path = upload.folder / relative
                        path.parent.mkdir(parents=True, exist_ok=True)
                        path.write_bytes(body)
                        upload.paths.add(relative)
                        upload.received_files += 1
                        upload.received_bytes += len(body)
                        if upload.received_files > upload.expected_files:
                            raise ValueError("文件数超出 send.begin")
                        write_frame(connection, {
                            "op": "upload.file.ok",
                            "path": relative,
                            "bytes": len(body),
                        })
                    except Exception as exc:
                        upload.broken = True
                        upload.error = str(exc)
                        send_error(connection, op, str(exc))
                    continue
                if op == "upload.abort":
                    if upload is not None:
                        shutil.rmtree(upload.folder, ignore_errors=True)
                        upload = None
                    write_frame(connection, {"op": "upload.abort.ok"})
                    continue
                if op == "upload.commit":
                    if upload is None:
                        send_error(connection, op, "没有进行中的上传")
                        continue
                    current = upload
                    upload = None
                    if current.broken:
                        shutil.rmtree(current.folder, ignore_errors=True)
                        send_error(connection, op, current.error or "上传内容损坏")
                        continue
                    if current.received_files != current.expected_files:
                        shutil.rmtree(current.folder, ignore_errors=True)
                        send_error(connection, op,
                                   f"文件数不符: {current.received_files}/{current.expected_files}")
                        continue
                    if current.received_bytes != current.expected_bytes:
                        shutil.rmtree(current.folder, ignore_errors=True)
                        send_error(connection, op,
                                   f"总字节不符: {current.received_bytes}/{current.expected_bytes}")
                        continue
                    if current.kind == "maid" and "maid_data.maid" not in current.paths:
                        shutil.rmtree(current.folder, ignore_errors=True)
                        send_error(connection, op, "缺少 maid_data.maid")
                        continue
                    if current.kind == "sound" and not any(
                            SOUND_FILE.fullmatch(path) for path in current.paths):
                        shutil.rmtree(current.folder, ignore_errors=True)
                        send_error(connection, op, "音效包没有 OGG 文件")
                        continue
                    try:
                        target = promote(current)
                    except Exception as exc:
                        send_error(connection, op, f"写入存档失败: {exc}")
                        continue
                    name = discover_name(target, current.name)
                    write_frame(connection, {
                        "op": "upload.commit.ok",
                        "kind": current.kind,
                        "id": current.maid_id,
                        "name": name if current.kind == "maid" else current.maid_id,
                    })
                    print(f"[virtual-portal] committed {current.kind} {current.maid_id}: {target}", flush=True)
                    if current.kind == "maid":
                        return True
                    continue
                send_error(connection, op, f"未知操作: {op}")
        except Exception as exc:
            print(f"[virtual-portal] connection error: {exc}", flush=True)
            try:
                send_error(connection, "connection", str(exc))
            except OSError:
                pass
        finally:
            if upload is not None:
                shutil.rmtree(upload.folder, ignore_errors=True)
            connection.close()
        return False

    def list_items(self, kind):
        if kind == "house":
            return []
        root = self.sounds_root if kind == "sound" else self.save_root
        if not root.is_dir():
            return []
        items = []
        for folder in sorted(root.iterdir(), key=lambda path: path.name):
            if not folder.is_dir() or folder.name.startswith("."):
                continue
            if kind == "maid" and not (folder / "maid.json").is_file():
                continue
            name = folder.name
            if kind == "maid":
                name = discover_name(folder, name)
            items.append({
                "id": folder.name,
                "name": name,
                "bytes": folder_bytes(folder),
                "sendable": True,
            })
        return items


def request(sock, op, header=None, body=b""):
    payload = {"op": op}
    if header:
        payload.update(header)
    write_frame(sock, payload, body)
    response, response_body = read_frame(sock)
    if response.get("op") == "error":
        raise RuntimeError(response.get("message", "server error"))
    return response, response_body


def self_test():
    tools = Path(__file__).resolve().parent
    out = tools / "out"
    out.mkdir(parents=True, exist_ok=True)
    temp = out / "virtual_maid_selftest"
    if temp.exists():
        shutil.rmtree(temp)
    temp.mkdir(parents=True)
    try:
        root = temp / "saves" / "maid"
        server = MaidUploadServer("127.0.0.1", 0, root, once=True)
        thread = threading.Thread(target=server.serve, daemon=True)
        thread.start()
        server.ready.wait(5)
        if not server.ready.is_set():
            raise RuntimeError("server did not start")
        maid_id = str(uuid.uuid4())
        files = {
            "maid_data.maid": b"virtual maid archive",
            "maid.json": json.dumps({"name": "虚拟测试女仆"}, ensure_ascii=False).encode("utf-8"),
        }
        total = sum(len(v) for v in files.values())
        with socket.create_connection(("127.0.0.1", server.bound_port), timeout=5) as sock:
            request(sock, "hello", {"version": PROTOCOL_VERSION, "client": "self-test"})
            listed, _ = request(sock, "list.request", {"kind": "sound"})
            if listed.get("kind") != "sound" or not isinstance(listed.get("items"), list):
                raise RuntimeError("list.request response invalid")

            sound_id = "virtual_sound"
            sound_files = {
                "maid/ai/test0.ogg": b"OggS-virtual-test",
            }
            sound_total = sum(len(v) for v in sound_files.values())
            request(sock, "upload.begin", {
                "kind": "sound",
                "id": sound_id,
                "name": sound_id,
                "file_count": len(sound_files),
                "total_bytes": sound_total,
            })
            for relative, data in sound_files.items():
                request(sock, "upload.file", {
                    "path": relative,
                    "sha256": hashlib.sha256(data).hexdigest(),
                }, data)
            request(sock, "upload.commit")

            reply, _ = request(sock, "upload.begin", {
                "kind": "maid",
                "id": maid_id,
                "name": "虚拟测试女仆",
                "file_count": len(files),
                "total_bytes": total,
            })
            if reply.get("id") != maid_id:
                raise RuntimeError("upload id mismatch")
            for relative, data in files.items():
                request(sock, "upload.file", {
                    "path": relative,
                    "sha256": hashlib.sha256(data).hexdigest(),
                }, data)
            request(sock, "upload.commit")
        thread.join(5)
        target = root / maid_id
        if not (target / "maid_data.maid").is_file():
            raise RuntimeError("maid_data.maid missing after commit")
        if not (root.parent / "sounds" / sound_id / "maid" / "ai" / "test0.ogg").is_file():
            raise RuntimeError("sound package missing after commit")
        print(f"OK: self-test wrote {target}")
    finally:
        shutil.rmtree(temp, ignore_errors=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=7411)
    parser.add_argument("--save-root", type=Path, default=default_save_root())
    parser.add_argument("--once", action="store_true", help="接收一个女仆后退出")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    server = MaidUploadServer(args.host, args.port, args.save_root, args.once)
    try:
        server.serve()
    except KeyboardInterrupt:
        print("\n[virtual-portal] interrupted", flush=True)
    finally:
        server.close()


if __name__ == "__main__":
    main()
