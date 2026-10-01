"""核对 Interop/Gltf 用到的 glTFast API 在装好的包源码里确实存在（不需要 Unity、不需要编译）。

顺带检查新写的 C# 大括号配平。改了 glTF 加载器之后跑一遍:
    python Tools/gltf_api_check.py
"""

import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE_GLOB = "com.unity.cloud.gltfast@*"

# (包内相对提示, 正则, 说明)
API_CHECKS = [
    (r"GltfImport\.cs",
     r"public GltfImport\(\s*IDownloadProvider downloadProvider = null,\s*IDeferAgent deferAgent = null,\s*IMaterialGenerator materialGenerator = null,\s*ICodeLogger logger = null",
     "GltfImport 构造函数 (downloadProvider, deferAgent, materialGenerator, logger)"),
    (r"GltfImport\.cs",
     r"public async Task<bool> Load\(\s*byte\[\] data,\s*Uri uri = null,\s*ImportSettings importSettings = null",
     "Load(byte[], Uri, ImportSettings, CancellationToken)"),
    (r"GltfImport\.cs",
     r"public async Task<bool> InstantiateMainSceneAsync\(\s*Transform parent,\s*CancellationToken cancellationToken = default",
     "InstantiateMainSceneAsync(Transform, CancellationToken)"),
    (r"GltfImport\.cs", r"public void Dispose\(\)", "GltfImport.Dispose()"),
    (r"ImportSettings\.cs", r"public bool GenerateMipMaps", "ImportSettings.GenerateMipMaps"),
    (r"IGltfReadable\.cs", r"Texture2D GetTexture\(int index = 0\)", "IGltfReadable.GetTexture(int)"),
    (r"IMaterialGenerator\.cs", r"UnityEngine\.Material GenerateMaterial\(", "IMaterialGenerator.GenerateMaterial"),
    (r"IMaterialGenerator\.cs", r"UnityEngine\.Material GetDefaultMaterial\(bool pointsSupport = false\)", "IMaterialGenerator.GetDefaultMaterial"),
    (r"IMaterialGenerator\.cs", r"void SetLogger\(ICodeLogger logger\)", "IMaterialGenerator.SetLogger"),
    (r"ICodeLogger\.cs", r"void Warning\(string message\)", "ICodeLogger.Warning(string)"),
    (r"ConsoleLogger\.cs", r"class ConsoleLogger", "ConsoleLogger"),
    (r"Material\.cs", r"public PbrMetallicRoughness pbrMetallicRoughness", "Schema.Material.pbrMetallicRoughness"),
    (r"Material\.cs", r"public float alphaCutoff", "Schema.Material.alphaCutoff"),
    (r"Material\.cs", r"public bool doubleSided", "Schema.Material.doubleSided"),
    (r"MaterialPbrMetallicRoughness\.cs", r"public float\[\] baseColorFactor", "PbrMetallicRoughness.baseColorFactor"),
    (r"MaterialPbrMetallicRoughness\.cs", r"public TextureInfo baseColorTexture", "PbrMetallicRoughness.baseColorTexture"),
    (r"TextureInfo\.cs", r"public int index = -1", "TextureInfo.index"),
]

CS_FILES = [
    "Assets/Scripts/Interop/Gltf/McGltfMaterialGenerator.cs",
    "Assets/Scripts/Interop/Gltf/GltfImportHolder.cs",
    "Assets/Scripts/Interop/Gltf/GltfModelLoader.cs",
    "Assets/Scripts/Interop/Gltf/GltfModelSpawner.cs",
]


def find_package():
    candidates = [
        os.path.join(ROOT, "Library", "PackageCache"),
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "Unity", "cache", "packages", "packages.unity.com"),
    ]
    for base in candidates:
        for path in sorted(glob.glob(os.path.join(base, PACKAGE_GLOB))):
            if os.path.isdir(path):
                return path
    return None


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def collect_sources(package):
    sources = {}
    for folder, _, files in os.walk(package):
        for name in files:
            if name.endswith(".cs"):
                sources.setdefault(name, []).append(os.path.join(folder, name))
    return sources


def main():
    package = find_package()
    if not package:
        print("找不到 glTFast 包，先确认 Packages/manifest.json 里的依赖装好了")
        return 1

    print("glTFast 包: %s" % os.path.relpath(package, ROOT))
    sources = collect_sources(package)
    errors = []

    for hint, pattern, label in API_CHECKS:
        files = [p for name, paths in sources.items() if re.fullmatch(hint, name) for p in paths]
        hit = False
        for path in files:
            if re.search(pattern, read(path), re.S):
                hit = True
                break
        if not hit:
            errors.append("包源码里找不到: %s (%s)" % (label, hint))
        else:
            print("  ok  %s" % label)

    for rel in CS_FILES:
        path = os.path.join(ROOT, rel)
        if not os.path.isfile(path):
            errors.append("缺文件: %s" % rel)
            continue
        text = read(path)
        if text.count("{") != text.count("}"):
            errors.append("%s 大括号不配平" % rel)

    print("检查 %d 个 API、%d 个文件" % (len(API_CHECKS), len(CS_FILES)))
    if errors:
        print("\n发现问题:")
        for item in errors:
            print("  " + item)
        return 1

    print("检查通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
