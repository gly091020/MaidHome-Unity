#!/usr/bin/env python3
"""Portal 协议的测试客户端，照着 MC 模组该做的动作来一遍。

帧格式（和 Assets/Scripts/Interop/Portal/PortalFrame.cs 一致）：
    [4 字节大端 header 长度][header JSON(UTF-8)][body 原始字节]
body 长度写在 header 的 "body" 字段里。

消息一览（客户端 -> 服务端带 ?，服务端 -> 客户端带 !）：
    ? hello          {version, client}                     ! hello.ok {version, server, kinds}
    ? list.request   {kind}                                ! list.ok {kind, items:[{id,name,bytes,sendable}]}
    ? upload.begin   {kind, id, name, file_count, total_bytes}
                                                           ! upload.begin.ok {upload_id, kind, id}
    ? upload.file    {path, sha256, body} + body 字节       ! upload.file.ok {path, bytes}
    ? upload.commit  {}                                    ! upload.commit.ok {kind, id, name}
    ? upload.abort   {}                                    ! upload.abort.ok
    ? ping           {}                                    ! pong
    ? send.ack       {id, ok, message}                     （服务端在等这个，收到 ok 才删存档）
    ! send.begin     {kind, id, name, total_bytes, data}
    ! send.file      {path, sha256, body} + body 字节
    ! send.end       {id, total_bytes}
    ! error          {request, message}                    （任何请求出错都回这个）

kind 取值：maid / house / sound。id 为空时服务端会随机生成一个 uuid 并回带。
一次只跑一个传输；上传先写 tmp/portal/<upload_id>，commit 时才顶掉正式目录。

用法：
    python Tools/portal_test_client.py list maid
    python Tools/portal_test_client.py upload maid <女仆文件夹> [--id xxx] [--name 酒狐]
    python Tools/portal_test_client.py upload sound <音效包文件夹>
    python Tools/portal_test_client.py watch          # 守着等服务端发女仆过来
    python Tools/portal_test_client.py demo           # hello + list 三样都给一遍
"""

import argparse
import hashlib
import json
import os
import socket
import struct
import sys
import time
import uuid

PROTOCOL_VERSION = 1
CHUNK = 64 * 1024

# 请求 op -> 服务端应答的 op（见 PortalServer.Handle）
REPLY_OPS = {
    "hello": "hello.ok",
    "list.request": "list.ok",
    "upload.begin": "upload.begin.ok",
    "upload.file": "upload.file.ok",
    "upload.commit": "upload.commit.ok",
    "upload.abort": "upload.abort.ok",
    "ping": "pong",
}


def log(text):
    sys.stdout.write(text + "\n")
    sys.stdout.flush()


class PortalClient:
    def __init__(self, host, port, out_dir, timeout=30.0):
        self.host = host
        self.port = port
        self.out_dir = out_dir
        self.timeout = timeout
        self.sock = None
        self.download = None  # send.begin 之后记在这儿

    # ------------------------------------------------------------ 连接

    def connect(self):
        self.sock = socket.create_connection((self.host, self.port), timeout=self.timeout)
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        log("[连上] %s:%d" % (self.host, self.port))

    def close(self):
        if self.sock is not None:
            try:
                self.sock.close()
            finally:
                self.sock = None

    # ------------------------------------------------------------ 帧读写

    def send_frame(self, header, body=b""):
        raw = json.dumps(header, ensure_ascii=False).encode("utf-8")
        self.sock.sendall(struct.pack(">I", len(raw)))
        self.sock.sendall(raw)
        if body:
            self.sock.sendall(body)

    def send_header_only(self, header):
        raw = json.dumps(header, ensure_ascii=False).encode("utf-8")
        self.sock.sendall(struct.pack(">I", len(raw)))
        self.sock.sendall(raw)

    def recv_exact(self, count):
        chunks = []
        got = 0
        while got < count:
            chunk = self.sock.recv(min(CHUNK, count - got))
            if not chunk:
                raise ConnectionError("连接被对方关闭")
            chunks.append(chunk)
            got += len(chunk)
        return b"".join(chunks)

    def recv_frame(self):
        length = struct.unpack(">I", self.recv_exact(4))[0]
        header = json.loads(self.recv_exact(length).decode("utf-8"))
        body = self.recv_exact(header["body"]) if header.get("body") else b""
        return header, body

    # ------------------------------------------------------------ 通知分发

    def handle_notification(self, header, body):
        """处理服务端主动推的帧。返回 True 表示认领了。"""
        op = header.get("op")
        if op == "send.begin":
            self.on_send_begin(header)
            return True
        if op == "send.file":
            self.on_send_file(header, body)
            return True
        if op == "send.end":
            self.on_send_end(header)
            return True
        return False

    def wait_for(self, wanted, timeout=None):
        """读帧直到拿到 wanted 里的 op，中途的 send.* 通知顺手处理掉。"""
        deadline = time.time() + (timeout if timeout is not None else self.timeout)
        while True:
            if time.time() > deadline:
                raise TimeoutError("等 " + str(wanted) + " 超时")
            header, body = self.recv_frame()
            op = header.get("op")
            if op in wanted:
                return header, body
            if self.handle_notification(header, body):
                continue
            log("[通知] " + op)

    def request(self, header, timeout=None):
        self.send_frame(header)
        expected = REPLY_OPS.get(header["op"])
        if expected is None:
            raise RuntimeError("没登记应答 op: " + header["op"])
        reply, _ = self.wait_for(("error", expected), timeout)
        if reply.get("op") == "error":
            raise RuntimeError(reply.get("message", "服务端报错"))
        return reply

    # ------------------------------------------------------------ 各个动作

    def hello(self, name="python-test"):
        reply = self.request({"op": "hello", "version": PROTOCOL_VERSION, "client": name})
        log("[握手] 服务端 %s，协议 %s，支持的类型 %s"
            % (reply.get("server"), reply.get("version"), ",".join(reply.get("kinds", []))))
        return reply

    def list_kind(self, kind):
        reply = self.request({"op": "list.request", "kind": kind})
        items = reply.get("items", [])
        log("[列表] %s 共 %d 项" % (kind, len(items)))
        for item in items:
            log("   %-38s %-14s %8s  %s"
                % (item.get("id", ""), item.get("name", ""),
                   human(item.get("bytes", 0)),
                   "可发送" if item.get("sendable") else "缺 maid_data.maid"))
        return items

    def upload(self, kind, folder, item_id=None, name=None):
        files = collect_files(folder)
        if not files:
            raise RuntimeError("文件夹里没有文件: " + folder)

        total = sum(size for _, _, size in files)
        if item_id is None:
            item_id = str(uuid.uuid4()) if kind == "maid" else os.path.basename(os.path.abspath(folder))
        if name is None:
            name = ""

        log("[上传] %s -> %s（%d 个文件，%s）" % (kind, item_id, len(files), human(total)))
        reply = self.request({
            "op": "upload.begin",
            "kind": kind,
            "id": item_id,
            "name": name,
            "file_count": len(files),
            "total_bytes": total,
        })
        log("[上传] 服务端分配的 upload_id = %s" % reply.get("upload_id"))

        sent = 0
        started = time.time()
        for path, relative, size in files:
            digest = sha256_file(path)
            self.send_header_only({
                "op": "upload.file",
                "path": relative,
                "sha256": digest,
                "body": size,
            })
            with open(path, "rb") as handle:
                while True:
                    chunk = handle.read(CHUNK)
                    if not chunk:
                        break
                    self.sock.sendall(chunk)
                    sent += len(chunk)
                    show_progress(sent, total, started)
            self.wait_for(("error", "upload.file.ok"))
            if size > 0:
                log("")
            log("   ↑ %-40s %s" % (relative, human(size)))

        reply = self.request({"op": "upload.commit"})
        log("[上传] 完成 kind=%s id=%s name=%s"
            % (reply.get("kind"), reply.get("id"), reply.get("name")))
        return reply

    # ------------------------------------------------------------ 接收服务端发来的女仆

    def on_send_begin(self, header):
        root = os.path.join(self.out_dir, header.get("id", "unknown"))
        os.makedirs(root, exist_ok=True)
        self.download = {
            "root": root,
            "id": header.get("id", ""),
            "name": header.get("name", ""),
            "total": header.get("total_bytes", 0),
            "got": 0,
            "files": {},
        }
        log("[接收] 服务端开始发 %s (%s)，%s，随包 json = %s"
            % (self.download["id"], self.download["name"],
               human(self.download["total"]), json.dumps(header.get("data", {}), ensure_ascii=False)))

    def on_send_file(self, header, body):
        if self.download is None:
            raise RuntimeError("没收到 send.begin 就来了 send.file")
        relative = header.get("path", "")
        safe = os.path.normpath(relative).replace("\\", "/")
        if safe.startswith("../") or safe.startswith("/"):
            raise RuntimeError("服务端给了不安全的路径: " + relative)
        target = os.path.join(self.download["root"], safe)
        parent = os.path.dirname(target)
        if parent:
            os.makedirs(parent, exist_ok=True)
        digest = hashlib.sha256(body).hexdigest()
        if header.get("sha256") and header["sha256"].lower() != digest:
            raise RuntimeError("sha256 对不上: " + relative)
        with open(target, "wb") as handle:
            handle.write(body)
        self.download["files"][relative] = target
        self.download["got"] += len(body)
        log("   ↓ %-40s %s" % (relative, human(len(body))))

    def on_send_end(self, header):
        if self.download is None:
            raise RuntimeError("没收到 send.begin 就来了 send.end")
        log("[接收] 收完 %s，写入 %s" % (human(self.download["got"]), self.download["root"]))
        # 确认收到之后服务端才会删存档
        self.send_frame({"op": "send.ack", "id": self.download["id"], "ok": True})
        self.download = None


def collect_files(folder):
    result = []
    for root, _, names in os.walk(folder):
        for name in names:
            path = os.path.join(root, name)
            relative = os.path.relpath(path, folder).replace("\\", "/")
            result.append((path, relative, os.path.getsize(path)))
    result.sort(key=lambda item: item[1])
    return result


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            chunk = handle.read(CHUNK)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def human(value):
    value = int(value or 0)
    if value < 1024:
        return "%d B" % value
    if value < 1024 * 1024:
        return "%.1f KB" % (value / 1024.0)
    return "%.2f MB" % (value / 1048576.0)


def show_progress(done, total, started):
    if total <= 0:
        return
    percent = int(done * 100 / total)
    elapsed = max(0.001, time.time() - started)
    sys.stdout.write("\r   进度 %3d%%  %s/%s  %s/s"
                     % (percent, human(done), human(total), human(done / elapsed)))
    sys.stdout.flush()


def run_watch(client, once):
    log("[监听] 等 Ctrl+C 退出；服务端一发女仆过来就写盘并回 ack")
    while True:
        try:
            header, body = client.recv_frame()
        except KeyboardInterrupt:
            log("")
            return
        if client.handle_notification(header, body):
            if once and client.download is None:
                return
            continue
        log("[通知] " + str(header.get("op")))


def main():
    parser = argparse.ArgumentParser(description="MaidHome Portal 测试客户端")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7411)
    parser.add_argument("--out", default=os.path.join("Tools", "out", "portal_received"),
                        help="watch 收到文件的落盘目录")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("hello")
    list_parser = sub.add_parser("list")
    list_parser.add_argument("kind", choices=["maid", "house", "sound"])

    upload_parser = sub.add_parser("upload")
    upload_parser.add_argument("kind", choices=["maid", "house", "sound"])
    upload_parser.add_argument("folder")
    upload_parser.add_argument("--id", dest="item_id", default=None)
    upload_parser.add_argument("--name", default=None)

    watch_parser = sub.add_parser("watch")
    watch_parser.add_argument("--once", action="store_true", help="收到一只就退出")

    sub.add_parser("demo")

    args = parser.parse_args()
    client = PortalClient(args.host, args.port, os.path.abspath(args.out))
    os.makedirs(client.out_dir, exist_ok=True)

    client.connect()
    try:
        client.hello()
        if args.command == "hello":
            pass
        elif args.command == "list":
            client.list_kind(args.kind)
        elif args.command == "upload":
            client.upload(args.kind, args.folder, args.item_id, args.name)
        elif args.command == "watch":
            run_watch(client, args.once)
        elif args.command == "demo":
            client.list_kind("maid")
            client.list_kind("house")
            client.list_kind("sound")
            run_watch(client, False)
    except Exception as error:
        log("[错误] " + str(error))
        return 1
    finally:
        client.close()

    return 0


if __name__ == "__main__":
    sys.exit(main())
