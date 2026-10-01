"""核对 Gameplay 层用到的工程内部 API 是否存在（不需要 Unity、不需要编译）。

跨层调用最容易写错名字（MaidAssets 的字段、BedrockAnimationPlayer 的方法……），
这里拿各层源码逐个比对。改了 Gameplay 代码之后跑一遍:
    python Tools/gameplay_api_check.py
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# (被调用方的源文件, 正则, 说明)
API_CHECKS = [
    ("Assets/Scripts/Core/Storage/AppPaths.cs",
     r"public static string MaidSaveRoot", "AppPaths.MaidSaveRoot"),
    ("Assets/Scripts/Interop/Maid/MaidSaveData.cs",
     r"public static List<MaidSaveData> ScanRoot\(string maidRoot\)", "MaidSaveData.ScanRoot"),
    ("Assets/Scripts/Interop/Maid/MaidSaveData.cs",
     r"public string Id = ", "MaidSaveData.Id"),
    ("Assets/Scripts/Interop/Maid/MaidSaveData.cs",
     r"public string Name = ", "MaidSaveData.Name"),
    ("Assets/Scripts/Interop/Maid/MaidAssetLoader.cs",
     r"public static MaidAssets Load\(MaidSaveData maid\)", "MaidAssetLoader.Load"),
    ("Assets/Scripts/Interop/Maid/MaidAssets.cs",
     r"public GameObject Root;", "MaidAssets.Root"),
    ("Assets/Scripts/Interop/Maid/MaidAssets.cs",
     r"public readonly List<AnimationClip> Clips", "MaidAssets.Clips"),
    ("Assets/Scripts/Interop/Maid/MaidAssets.cs",
     r"public bool FromCache;", "MaidAssets.FromCache"),
    ("Assets/Scripts/Interop/Maid/MaidAssets.cs",
     r"public void Dispose\(\)", "MaidAssets.Dispose"),
    ("Assets/Scripts/Interop/Bedrock/BedrockAnimationPlayer.cs",
     r"public void SetClips\(IList<AnimationClip> clips\)", "BedrockAnimationPlayer.SetClips"),
    ("Assets/Scripts/Interop/Bedrock/BedrockAnimationPlayer.cs",
     r"public bool HasClip\(string name\)", "BedrockAnimationPlayer.HasClip"),
    ("Assets/Scripts/Interop/Bedrock/BedrockAnimationPlayer.cs",
     r"public bool Play\(string name\)", "BedrockAnimationPlayer.Play"),
]

# 本层自己要用到的成员
SELF_CHECKS = [
    ("Assets/Scripts/Gameplay/Maid/WanderArea.cs", r"public Vector3 Center", "WanderArea.Center"),
    ("Assets/Scripts/Gameplay/Maid/WanderArea.cs", r"public Vector3 Size", "WanderArea.Size"),
    ("Assets/Scripts/Gameplay/Maid/WanderArea.cs", r"public float MinDistance", "WanderArea.MinDistance"),
    ("Assets/Scripts/Gameplay/Maid/WanderArea.cs",
     r"public bool TryPick\(Vector3 from, System\.Random rng, int maxTries, out Vector3 point\)",
     "WanderArea.TryPick"),
    ("Assets/Scripts/Gameplay/Maid/MaidLoader.cs", r"public static MaidAssets Load\(MaidSaveData maid\)",
     "MaidLoader.Load"),
    ("Assets/Scripts/Gameplay/Maid/MaidPlacement.cs",
     r"public static GameObject Place\(MaidAssets assets, Vector3 position, Quaternion rotation\)",
     "MaidPlacement.Place"),
    ("Assets/Scripts/Interop/House/HouseGrid.cs", r"public readonly int SizeX;", "HouseGrid.SizeX"),
    ("Assets/Scripts/Interop/House/HouseGrid.cs", r"public readonly int SizeY;", "HouseGrid.SizeY"),
    ("Assets/Scripts/Interop/House/HouseGrid.cs", r"public readonly int SizeZ;", "HouseGrid.SizeZ"),
    ("Assets/Scripts/Interop/House/HouseGrid.cs", r"public int Count", "HouseGrid.Count"),
    ("Assets/Scripts/Interop/House/HouseGrid.cs", r"public int Index\(int x, int y, int z\)",
     "HouseGrid.Index"),
    ("Assets/Scripts/Interop/House/HouseGrid.cs", r"public bool IsWalkable\(int x, int y, int z\)",
     "HouseGrid.IsWalkable"),
    ("Assets/Scripts/Interop/House/HouseSaveData.cs", r"public bool\[\] Walkable",
     "HouseSaveData.Walkable"),
    ("Assets/Scripts/Interop/House/HouseSaveData.cs", r"public int SizeX;", "HouseSaveData.SizeX"),
    ("Assets/Scripts/Interop/House/HouseSaveData.cs",
     r"public static List<HouseSaveData> ScanRoot\(string houseRoot\)", "HouseSaveData.ScanRoot"),
    ("Assets/Scripts/Core/Storage/AppPaths.cs", r"public static string HouseSaveRoot", "AppPaths.HouseSaveRoot"),
    ("Assets/Scripts/Gameplay/House/HouseGridView.cs", r"public HouseGrid Grid", "HouseGridView.Grid"),
    ("Assets/Scripts/Gameplay/House/HouseGridView.cs",
     r"public Vector3 CellFeet\(Vector3Int cell\)", "HouseGridView.CellFeet"),
    ("Assets/Scripts/Gameplay/House/HouseImporter.cs",
     r"public static async Task<GameObject> LoadAsync\(HouseSaveData house", "HouseImporter.LoadAsync"),
    ("Assets/Scripts/Gameplay/Maid/MaidNavigator.cs",
     r"public bool SetDestination\(Vector3 from, Vector3 to\)", "MaidNavigator.SetDestination"),
    ("Assets/Scripts/Gameplay/Maid/MaidNavigator.cs",
     r"public bool TryPickRandomDestination\(Vector3 from, float minDistance, System\.Random rng, int maxTries,",
     "MaidNavigator.TryPickRandomDestination"),
    ("Assets/Scripts/Gameplay/Maid/MaidNavigator.cs",
     r"public bool TrySnap\(Vector3 world, float maxDistance, out Vector3 snapped\)", "MaidNavigator.TrySnap"),
    ("Assets/Scripts/Gameplay/Maid/MaidWanderer.cs",
     r"public void SetHouse\(HouseNavMesh nav\)", "MaidWanderer.SetHouse"),
    ("Assets/Scripts/Gameplay/House/HouseNavMesh.cs",
     r"public bool Build\(\)", "HouseNavMesh.Build"),
    ("Assets/Scripts/Gameplay/House/HouseNavMesh.cs",
     r"public bool IsBuilt", "HouseNavMesh.IsBuilt"),
    ("Assets/Scripts/Interop/House/HouseGrid.cs",
     r"public int MarkEnclosedNonWalkable\(bool\[\] marks\)", "HouseGrid.MarkEnclosedNonWalkable"),
    ("Assets/Scripts/Interop/House/HouseGridMapper.cs",
     r"public static Vector3Int ToCell\(GridAxis axis, int sizeX, int sizeZ, Vector3 local\)",
     "HouseGridMapper.ToCell"),
    ("Assets/Scripts/Interop/House/HouseGridMapper.cs",
     r"public static Vector3 ToLocal\(GridAxis axis, int sizeX, int sizeZ, Vector3Int cell\)",
     "HouseGridMapper.ToLocal"),
    ("Assets/Scripts/Gameplay/House/HouseGridView.cs",
     r"public GridAxis Axis", "HouseGridView.Axis"),
]


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []
    for rel, pattern, label in API_CHECKS + SELF_CHECKS:
        path = os.path.join(ROOT, rel)
        if not os.path.isfile(path):
            errors.append("缺文件: %s" % rel)
            continue
        if not re.search(pattern, read(path)):
            errors.append("源码里找不到 %s（%s）" % (label, rel))
        else:
            print("  ok  %s" % label)

    print("检查 %d 项" % (len(API_CHECKS) + len(SELF_CHECKS)))
    if errors:
        print("\n发现问题:")
        for item in errors:
            print("  " + item)
        return 1

    print("检查通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
