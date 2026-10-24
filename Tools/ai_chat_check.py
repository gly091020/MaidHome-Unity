import argparse
import gzip
import json
import pathlib


MAX_COMPRESSED = 2 * 1024 * 1024
MAX_JSON = 2 * 1024 * 1024
FORBIDDEN_KEYS = {"api_key", "apikey", "authorization", "secret_key", "bearer"}


def load(path):
    raw = pathlib.Path(path).read_bytes()
    if not raw or len(raw) > MAX_COMPRESSED:
        raise ValueError(f"compressed size invalid: {len(raw)}")
    data = gzip.decompress(raw)
    if len(data) > MAX_JSON:
        raise ValueError(f"json too large: {len(data)}")
    return json.loads(data.decode("utf-8"))


def walk_keys(value):
    if isinstance(value, dict):
        for key, child in value.items():
            yield key.lower()
            yield from walk_keys(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk_keys(child)


def validate(path):
    root = load(path)
    if root.get("schema_version") != 1:
        raise ValueError(f"unsupported schema: {root.get('schema_version')}")
    messages = root.get("messages")
    if not isinstance(messages, list) or len(messages) > 1024:
        raise ValueError("messages array invalid")
    base = root.get("base_message_count")
    if not isinstance(base, int) or not 0 <= base <= len(messages):
        raise ValueError("base_message_count invalid")
    forbidden = sorted(FORBIDDEN_KEYS.intersection(walk_keys(root)))
    if forbidden:
        raise ValueError("contains secret-like keys: " + ", ".join(forbidden))
    profile = root.get("profile") or {}
    phone = sum(1 for msg in messages if msg.get("origin") == "phone")
    print(
        f"OK: messages={len(messages)}, base={base}, phone={phone}, "
        f"persona={profile.get('persona_source', 'missing')}"
    )


def selftest():
    sample = {
        "schema_version": 1,
        "maid_uuid": "selftest",
        "base_message_count": 1,
        "profile": {
            "maid_name": "test",
            "persona_source": "model",
            "resolved_system_prompt": "persona",
        },
        "messages": [
            {"id": "1", "role": "user", "text": "hi", "origin": "mc", "status": "complete"},
            {"id": "2", "role": "assistant", "text": "hello", "origin": "phone", "status": "complete"},
        ],
    }
    path = pathlib.Path(__file__).resolve().parent / "out" / "ai_chat_selftest.json.gz"
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        path.write_bytes(gzip.compress(json.dumps(sample, ensure_ascii=False).encode("utf-8")))
        validate(path)
    finally:
        path.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("path", nargs="?")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()
    if args.selftest:
        selftest()
    elif args.path:
        validate(args.path)
    else:
        parser.error("need ai_chat.json.gz or --selftest")


if __name__ == "__main__":
    main()
