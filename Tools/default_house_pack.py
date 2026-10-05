"""把一栋房子打成"随包默认房子"，供游戏第一次启动时拷进存档目录。

做的事：
  1. 把源房子目录整棵拷到 Assets/StreamingAssets/DefaultHouse/（跳过 *.usda，
     那是导出给 Blender 的中间文件，9MB 左右，运行时不读）；
  2. 在目标目录写一份 files.txt（每行一个相对路径，用 / 分隔），
     运行时不列目录、照这份清单一个个读，Android 上也不用遍历 apk；
  3. 校验 glTF 里引用的 buffer / 贴图都在包里，并打印总大小。

用法：
  python Tools/default_house_pack.py                     # 用默认源（下面的 DEFAULT_SOURCE）
  python Tools/default_house_pack.py <源房子目录>        # 换一栋
"""

import json
import os
import shutil
import sys

DEFAULT_SOURCE = os.path.join(
    os.path.expanduser("~"), "Documents", "MaidHome", "saves", "house",
    "0891be0d-4cde-42dd-9c08-7a23bf702677")
TARGET = os.path.join("Assets", "StreamingAssets", "DefaultHouse")
MANIFEST = "files.txt"
SKIP_EXT = {".usda"}


def collect(source):
    files = []
    for root, _dirs, names in os.walk(source):
        for name in names:
            full = os.path.join(root, name)
            rel = os.path.relpath(full, source).replace(os.sep, "/")
            if os.path.splitext(name)[1].lower() in SKIP_EXT:
                continue
            if rel == MANIFEST:
                continue
            files.append(rel)
    files.sort()
    return files


def check_gltf_references(source, files):
    gltfs = [f for f in files if f.lower().endswith(".gltf")]
    missing = []
    for rel in gltfs:
        with open(os.path.join(source, rel), encoding="utf-8") as fh:
            gltf = json.load(fh)
        refs = [b.get("uri") for b in gltf.get("buffers", [])]
        refs += [i.get("uri") for i in gltf.get("images", [])]
        for uri in refs:
            if not uri or uri.startswith("data:"):
                continue
            if uri not in files:
                missing.append((rel, uri))
    return gltfs, missing


def main():
    source = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SOURCE
    if not os.path.isdir(source):
        print("源房子目录不存在: " + source)
        return 1

    files = collect(source)
    if not files:
        print("源目录里没文件: " + source)
        return 1

    gltfs, missing = check_gltf_references(source, files)
    if missing:
        print("glTF 引用的文件不在包里，先查一下：")
        for gltf, uri in missing:
            print("  %s -> %s" % (gltf, uri))
        return 1

    if os.path.isdir(TARGET):
        shutil.rmtree(TARGET)
    os.makedirs(TARGET)

    total = 0
    for rel in files:
        src = os.path.join(source, rel.replace("/", os.sep))
        dst = os.path.join(TARGET, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)
        total += os.path.getsize(dst)

    with open(os.path.join(TARGET, MANIFEST), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(files) + "\n")

    print("源: %s" % source)
    print("目标: %s" % TARGET)
    print("文件 %d 个，共 %.1f MB（跳过 %s）" % (len(files), total / 1024 / 1024,
                                             ", ".join(sorted(SKIP_EXT))))
    print("glTF: %s，引用的 buffer/贴图全部在包里" % ", ".join(gltfs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
