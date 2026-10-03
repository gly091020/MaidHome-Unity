using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MaidHome.Core.Save;
using MaidHome.Core.Storage;
using MaidHome.Gameplay.Audio;
using MaidHome.Interop.House;
using MaidHome.Interop.Maid;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 存档编辑器：一处看全 saves/ 下面三类数据，能改能删能复制。
    ///
    /// 女仆：改 maid.json 的字段、在背包/已放置之间切、转模型写缓存、复制、删除；
    /// 房子：设为当前房子、删除；
    /// 音效包：看事件数、删除。
    /// 删除和覆盖都会先弹确认，女仆的 maid.json 覆盖前会自动留一份 .bak。
    /// </summary>
    public sealed class MaidSaveToolWindow : EditorWindow
    {
        [MenuItem("Tools/MaidHome/存档编辑器")]
        static void Open()
        {
            MaidSaveToolWindow window = GetWindow<MaidSaveToolWindow>("存档编辑器");
            window.minSize = new Vector2(560f, 620f);
        }

        enum Tab
        {
            Maid,
            House,
            Sound
        }

        static readonly string[] TabNames = { "女仆", "房子", "音效包" };

        readonly List<MaidSaveData> _maids = new List<MaidSaveData>();
        readonly List<bool> _maidCached = new List<bool>();
        readonly List<HouseSaveData> _houses = new List<HouseSaveData>();
        readonly List<string> _soundIds = new List<string>();

        MaidWorldSave _world;
        Tab _tab;
        int _maidIndex;
        int _houseIndex;
        int _soundIndex;
        Vector2 _listScroll;
        Vector2 _detailScroll;
        string _status = "";
        GameObject _spawned;

        // 女仆字段的编辑缓冲，选中换人时才从存档里重新读一遍
        int _editLoadedIndex = -1;
        string _editName = "";
        int _editLevel;
        float _editScale = 1f;
        string _editSound = "";
        bool _editSimple;

        void OnEnable()
        {
            RefreshAll();
            if (string.IsNullOrEmpty(_status))
            {
                _status = "选一项看详情。女仆那页能改 maid.json、切背包状态、转模型、复制和删除；"
                    + "删除和覆盖前都会弹确认，maid.json 覆盖前会自动留一份 .bak。";
            }
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("存档编辑器", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("存档目录: " + AppPaths.SavesRoot, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开存档目录"))
            {
                AppPaths.EnsureDirectory(AppPaths.SavesRoot);
                EditorUtility.RevealInFinder(AppPaths.SavesRoot);
            }

            if (GUILayout.Button("打开缓存目录"))
            {
                AppPaths.EnsureDirectory(Path.Combine(AppPaths.CacheRoot, "maid"));
                EditorUtility.RevealInFinder(Path.Combine(AppPaths.CacheRoot, "maid"));
            }

            if (GUILayout.Button("刷新"))
            {
                RefreshAll();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            _tab = (Tab)GUILayout.Toolbar((int)_tab, TabNames);
            EditorGUILayout.Space();

            DrawList();
            EditorGUILayout.Space();

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));
            if (_tab == Tab.Maid)
            {
                DrawMaidDetail();
            }
            else if (_tab == Tab.House)
            {
                DrawHouseDetail();
            }
            else
            {
                DrawSoundDetail();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_status, MessageType.None);
        }

        void DrawList()
        {
            if (_tab == Tab.Maid)
            {
                EditorGUILayout.LabelField("女仆 (" + _maids.Count + ")", EditorStyles.boldLabel);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(140f));
                for (int i = 0; i < _maids.Count; i++)
                {
                    MaidSaveData maid = _maids[i];
                    string label = (string.IsNullOrEmpty(maid.Name) ? maid.Id : maid.Name)
                        + "  Lv" + maid.Level
                        + (IsInBag(maid.Id) ? "  [背包]" : "  [已放置]")
                        + (maid.SimpleBedrockModel ? "  [SimpleBedrock]" : "")
                        + (_maidCached[i] ? "  [已缓存]" : "")
                        + (HasMaidData(maid) ? "" : "  [缺 maid_data.maid]")
                        + (maid.Warnings.Count > 0 ? "  ⚠" : "");
                    if (GUILayout.Toggle(_maidIndex == i, label, "Button"))
                    {
                        _maidIndex = i;
                    }
                }

                EditorGUILayout.EndScrollView();
                return;
            }

            if (_tab == Tab.House)
            {
                EditorGUILayout.LabelField("房子 (" + _houses.Count + ")   当前: " + _world.HouseId, EditorStyles.boldLabel);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(140f));
                for (int i = 0; i < _houses.Count; i++)
                {
                    HouseSaveData house = _houses[i];
                    string label = (string.IsNullOrEmpty(house.Name) ? house.Id : house.Name)
                        + "  " + house.SizeX + "x" + house.SizeY + "x" + house.SizeZ
                        + (house.Id == _world.HouseId ? "  [当前]" : "")
                        + (house.Warnings.Count > 0 ? "  ⚠" : "");
                    if (GUILayout.Toggle(_houseIndex == i, label, "Button"))
                    {
                        _houseIndex = i;
                    }
                }

                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField("音效包 (" + _soundIds.Count + ")", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(140f));
            for (int i = 0; i < _soundIds.Count; i++)
            {
                string label = _soundIds[i] + "  " + CountOgg(FolderOfSound(_soundIds[i])) + " 个 ogg";
                if (GUILayout.Toggle(_soundIndex == i, label, "Button"))
                {
                    _soundIndex = i;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------ 女仆

        void DrawMaidDetail()
        {
            MaidSaveData maid = CurrentMaid();
            if (maid == null)
            {
                EditorGUILayout.LabelField("没扫到女仆。目录里应该是一个 uuid 一个文件夹，每个文件夹里有 maid.json。");
                return;
            }

            SyncEditBuffer(maid);

            EditorGUILayout.LabelField("id", maid.Id);
            EditorGUILayout.LabelField("目录", maid.Folder);
            EditorGUILayout.LabelField("模型 / 贴图 / 动画",
                maid.ModelFile + " / " + maid.TextureFile + " / " + maid.AnimationFile);
            EditorGUILayout.LabelField("大小", Size(maid.Folder) + "   缓存 " + (_maidCached[_maidIndex] ? "有" : "无"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("maid.json", EditorStyles.boldLabel);
            _editName = EditorGUILayout.TextField("名字", _editName);
            _editLevel = EditorGUILayout.IntField("等级", _editLevel);
            _editScale = EditorGUILayout.FloatField("缩放", _editScale);
            _editSound = EditorGUILayout.TextField("音效包 id", _editSound);
            _editSimple = EditorGUILayout.Toggle("SimpleBedrockModel", _editSimple);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存 maid.json"))
            {
                ApplyEdit(maid);
            }

            if (GUILayout.Button("还原"))
            {
                _editLoadedIndex = -1;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("世界状态（slot0.json）", EditorStyles.boldLabel);
            bool inBag = IsInBag(maid.Id);
            bool newInBag = EditorGUILayout.Toggle("在背包里", inBag);
            if (newInBag != inBag)
            {
                SetInBag(maid, newInBag);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("模型转换", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("转换并写缓存"))
            {
                Convert(maid);
            }

            if (GUILayout.Button("清缓存"))
            {
                MaidAssetCache.Delete(maid);
                _status = "已清掉 " + maid.Id + " 的缓存。";
                RefreshAll();
            }

            if (GUILayout.Button("生成到场景"))
            {
                Spawn(maid);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("管理", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开目录"))
            {
                EditorUtility.RevealInFinder(maid.Folder);
            }

            if (GUILayout.Button("复制一份"))
            {
                Duplicate(maid);
            }

            if (GUILayout.Button("删除…"))
            {
                DeleteMaid(maid);
            }

            EditorGUILayout.EndHorizontal();

            if (maid.Warnings.Count > 0)
            {
                EditorGUILayout.Space();
                for (int i = 0; i < maid.Warnings.Count; i++)
                {
                    EditorGUILayout.HelpBox(maid.Warnings[i], MessageType.Warning);
                }
            }
        }

        void SyncEditBuffer(MaidSaveData maid)
        {
            if (_editLoadedIndex == _maidIndex)
            {
                return;
            }

            _editLoadedIndex = _maidIndex;
            _editName = maid.Name;
            _editLevel = maid.Level;
            _editScale = maid.Scale;
            _editSound = maid.SoundPackId;
            _editSimple = maid.SimpleBedrockModel;
        }

        void ApplyEdit(MaidSaveData maid)
        {
            maid.Name = _editName;
            maid.Level = _editLevel;
            maid.Scale = _editScale <= 0f ? 1f : _editScale;
            maid.SoundPackId = _editSound;
            maid.SimpleBedrockModel = _editSimple;

            string error;
            if (maid.TrySave(out error))
            {
                _status = "已写回 " + Path.Combine(maid.Folder, "maid.json") + "（原文件留了一份 .bak）";
            }
            else
            {
                _status = "保存失败: " + error;
            }

            RefreshAll();
            _editLoadedIndex = _maidIndex;
        }

        void Convert(MaidSaveData maid)
        {
            MaidAssets assets = MaidAssetLoader.Build(maid);
            StringBuilder text = new StringBuilder();
            text.Append(Summary(assets));

            if (assets.Root != null)
            {
                try
                {
                    MaidAssetCache.Write(maid, assets);
                    text.Append("  已写缓存 " + Size(MaidAssetCache.FolderOf(maid)));
                }
                catch (Exception error)
                {
                    text.Append("  写缓存失败: " + error.Message);
                }
            }

            for (int i = 0; i < assets.Warnings.Count; i++)
            {
                text.AppendLine();
                text.Append("警告: " + assets.Warnings[i]);
            }

            DestroyNow(assets.Root);
            _status = text.ToString();
            RefreshAll();
        }

        void Spawn(MaidSaveData maid)
        {
            DestroyNow(_spawned);
            MaidAssets assets = MaidAssetLoader.Load(maid);
            _spawned = assets.Root;
            if (_spawned != null)
            {
                Undo.RegisterCreatedObjectUndo(_spawned, "生成女仆模型");
                Selection.activeGameObject = _spawned;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            _status = Summary(assets);
        }

        void Duplicate(MaidSaveData maid)
        {
            string newId = Guid.NewGuid().ToString();
            string target = Path.Combine(AppPaths.MaidSaveRoot, newId);
            try
            {
                CopyDirectory(maid.Folder, target);
                MaidSaveData copy = MaidSaveData.ParseFile(Path.Combine(target, "maid.json"));
                copy.Name = (string.IsNullOrEmpty(maid.Name) ? maid.Id : maid.Name) + " 副本";
                string error;
                if (!copy.TrySave(out error))
                {
                    _status = "复制出来了但改名失败: " + error;
                }
                else
                {
                    _status = "复制成 " + newId + "（名字加了个「副本」）";
                }
            }
            catch (Exception error)
            {
                _status = "复制失败: " + error.Message;
            }

            RefreshAll();
        }

        void DeleteMaid(MaidSaveData maid)
        {
            string name = string.IsNullOrEmpty(maid.Name) ? maid.Id : maid.Name;
            if (!EditorUtility.DisplayDialog("删除女仆",
                "把「" + name + "」的存档目录、缓存和 slot0.json 里的记录一起删掉？\n\n" + maid.Folder,
                "删除", "取消"))
            {
                return;
            }

            try
            {
                MaidAssetCache.Delete(maid);
                if (Directory.Exists(maid.Folder))
                {
                    Directory.Delete(maid.Folder, true);
                }

                if (_world.Remove(maid.Id))
                {
                    MaidWorldSaveStore.Save(_world);
                }

                _status = "已删除 " + maid.Id;
            }
            catch (Exception error)
            {
                _status = "删除失败: " + error.Message;
            }

            RefreshAll();
        }

        // ------------------------------------------------------------ 房子

        void DrawHouseDetail()
        {
            HouseSaveData house = CurrentHouse();
            if (house == null)
            {
                EditorGUILayout.LabelField("没扫到房子。目录里应该是一个 uuid 一个文件夹，每个文件夹里有 house.json。");
                return;
            }

            EditorGUILayout.LabelField("id", house.Id);
            EditorGUILayout.LabelField("名字", house.Name);
            EditorGUILayout.LabelField("目录", house.Folder);
            EditorGUILayout.LabelField("尺寸", house.SizeX + " x " + house.SizeY + " x " + house.SizeZ);
            EditorGUILayout.LabelField("可走格", HouseGrid.From(house).WalkableCount().ToString());
            EditorGUILayout.LabelField("模型文件", house.ModelPath);
            EditorGUILayout.LabelField("大小", Size(house.Folder));

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            bool isCurrent = house.Id == _world.HouseId;
            EditorGUI.BeginDisabledGroup(isCurrent);
            if (GUILayout.Button(isCurrent ? "已经是当前房子" : "设为当前房子"))
            {
                _world.HouseId = house.Id;
                MaidWorldSaveStore.Save(_world);
                _status = "当前房子设为 " + house.Id;
                RefreshAll();
            }

            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("打开目录"))
            {
                EditorUtility.RevealInFinder(house.Folder);
            }

            if (GUILayout.Button("删除…"))
            {
                DeleteHouse(house);
            }

            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < house.Warnings.Count; i++)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(house.Warnings[i], MessageType.Warning);
            }
        }

        void DeleteHouse(HouseSaveData house)
        {
            string name = string.IsNullOrEmpty(house.Name) ? house.Id : house.Name;
            if (!EditorUtility.DisplayDialog("删除房子",
                "把「" + name + "」整个目录删掉？\n\n" + house.Folder, "删除", "取消"))
            {
                return;
            }

            try
            {
                if (Directory.Exists(house.Folder))
                {
                    Directory.Delete(house.Folder, true);
                }

                if (_world.HouseId == house.Id)
                {
                    _world.HouseId = "";
                    MaidWorldSaveStore.Save(_world);
                }

                _status = "已删除 " + house.Id;
            }
            catch (Exception error)
            {
                _status = "删除失败: " + error.Message;
            }

            RefreshAll();
        }

        // ------------------------------------------------------------ 音效包

        void DrawSoundDetail()
        {
            if (_soundIds.Count == 0)
            {
                EditorGUILayout.LabelField("没扫到音效包。目录里应该是一个 id 一个文件夹，里面是 maid/<分类>/<事件>.ogg。");
                return;
            }

            string id = _soundIds[Mathf.Clamp(_soundIndex, 0, _soundIds.Count - 1)];
            string folder = FolderOfSound(id);
            MaidSoundPack pack = MaidSoundLibrary.Get(id);

            EditorGUILayout.LabelField("id", id);
            EditorGUILayout.LabelField("目录", folder);
            EditorGUILayout.LabelField("ogg 文件", CountOgg(folder).ToString());
            EditorGUILayout.LabelField("事件数", pack == null ? "0" : pack.EventCount.ToString());
            EditorGUILayout.LabelField("大小", Size(folder));

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开目录"))
            {
                EditorUtility.RevealInFinder(folder);
            }

            if (GUILayout.Button("重新扫描"))
            {
                MaidSoundLibrary.Reload();
                RefreshAll();
            }

            if (GUILayout.Button("删除…"))
            {
                DeleteSound(id, folder);
            }

            EditorGUILayout.EndHorizontal();

            if (pack != null && pack.EventCount > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("事件", EditorStyles.boldLabel);
                StringBuilder text = new StringBuilder();
                foreach (string eventId in pack.EventIds)
                {
                    text.Append(eventId).Append("   ");
                }

                EditorGUILayout.LabelField(text.ToString(), EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space();
            List<string> users = MaidIdsUsingSound(id);
            if (users.Count > 0)
            {
                EditorGUILayout.HelpBox("这些女仆在用这个音效包:\n" + string.Join("\n", users.ToArray()), MessageType.Info);
            }
        }

        void DeleteSound(string id, string folder)
        {
            List<string> users = MaidIdsUsingSound(id);
            string extra = users.Count > 0
                ? "\n\n有 " + users.Count + " 只女仆在用这个音效包，删了她们就没声音了:\n" + string.Join("\n", users.ToArray())
                : "";
            if (!EditorUtility.DisplayDialog("删除音效包",
                "把音效包「" + id + "」整个目录删掉？" + extra + "\n\n" + folder, "删除", "取消"))
            {
                return;
            }

            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }

                MaidSoundLibrary.Reload();
                _status = "已删除音效包 " + id;
            }
            catch (Exception error)
            {
                _status = "删除失败: " + error.Message;
            }

            RefreshAll();
        }

        // ------------------------------------------------------------ 数据

        void RefreshAll()
        {
            _world = MaidWorldSaveStore.Load();

            _maids.Clear();
            _maids.AddRange(MaidSaveData.ScanRoot(AppPaths.MaidSaveRoot));
            _maidCached.Clear();
            for (int i = 0; i < _maids.Count; i++)
            {
                _maidCached.Add(MaidAssetCache.IsFresh(_maids[i]));
            }

            _houses.Clear();
            _houses.AddRange(HouseSaveData.ScanRoot(AppPaths.HouseSaveRoot));

            _soundIds.Clear();
            if (Directory.Exists(AppPaths.SoundsRoot))
            {
                string[] folders = Directory.GetDirectories(AppPaths.SoundsRoot);
                for (int i = 0; i < folders.Length; i++)
                {
                    _soundIds.Add(Path.GetFileName(folders[i]));
                }

                _soundIds.Sort(StringComparer.OrdinalIgnoreCase);
            }

            _maidIndex = Clamp(_maidIndex, _maids.Count);
            _houseIndex = Clamp(_houseIndex, _houses.Count);
            _soundIndex = Clamp(_soundIndex, _soundIds.Count);
            _editLoadedIndex = -1;
        }

        static int Clamp(int index, int count)
        {
            return count == 0 ? 0 : Mathf.Clamp(index, 0, count - 1);
        }

        MaidSaveData CurrentMaid()
        {
            return _maids.Count == 0 ? null : _maids[Clamp(_maidIndex, _maids.Count)];
        }

        HouseSaveData CurrentHouse()
        {
            return _houses.Count == 0 ? null : _houses[Clamp(_houseIndex, _houses.Count)];
        }

        bool IsInBag(string maidId)
        {
            MaidWorldRecord record = _world.Find(maidId);
            return record == null || record.InBag;
        }

        void SetInBag(MaidSaveData maid, bool inBag)
        {
            try
            {
                MaidWorldRecord record = _world.GetOrCreate(maid.Id);
                record.InBag = inBag;
                MaidWorldSaveStore.Save(_world);
                _status = inBag
                    ? maid.Id + " 已收回背包"
                    : maid.Id + " 标记成已放置（位置用 slot0 里记的那个，游戏里位置不对就收回来）";
            }
            catch (Exception error)
            {
                _status = "写 slot0.json 失败: " + error.Message;
            }
        }

        List<string> MaidIdsUsingSound(string soundId)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < _maids.Count; i++)
            {
                if (string.Equals(_maids[i].SoundPackId, soundId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(string.IsNullOrEmpty(_maids[i].Name) ? _maids[i].Id : _maids[i].Name);
                }
            }

            return result;
        }

        static bool HasMaidData(MaidSaveData maid)
        {
            return maid != null && !string.IsNullOrEmpty(maid.Folder)
                && File.Exists(Path.Combine(maid.Folder, "maid_data.maid"));
        }

        static string FolderOfSound(string id)
        {
            return Path.Combine(AppPaths.SoundsRoot, id);
        }

        static int CountOgg(string folder)
        {
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.ogg", SearchOption.AllDirectories).Length
                : 0;
        }

        static string Summary(MaidAssets assets)
        {
            if (assets == null || assets.Root == null)
            {
                return "转换失败（" + (assets == null ? 0 : assets.Warnings.Count) + " 条警告）";
            }

            MeshFilter[] filters = assets.Root.GetComponentsInChildren<MeshFilter>(true);
            Transform[] transforms = assets.Root.GetComponentsInChildren<Transform>(true);
            int vertices = 0;
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh != null)
                {
                    vertices += filters[i].sharedMesh.vertexCount;
                }
            }

            int keyframes = 0;
            for (int i = 0; i < assets.ClipData.Count; i++)
            {
                // 运行时是按需读的，编辑器里要统计就得先把采样读全
                assets.ClipData[i].EnsureTracks();
                keyframes += assets.ClipData[i].KeyframeCount;
            }

            return "骨骼=" + Mathf.Max(0, transforms.Length - 1) + " 网格=" + filters.Length
                + " 顶点=" + vertices + " 动画=" + assets.ClipData.Count + " 关键帧=" + keyframes
                + " 耗时=" + assets.BuildSeconds.ToString("0.00") + "秒"
                + (assets.FromCache ? "  [来自缓存]" : "");
        }

        static string Size(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return "0 KB";
            }

            long bytes = 0;
            string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                bytes += new FileInfo(files[i]).Length;
            }

            return (bytes / 1024f).ToString("0.0") + " KB";
        }

        static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            string[] files = Directory.GetFiles(source);
            for (int i = 0; i < files.Length; i++)
            {
                File.Copy(files[i], Path.Combine(target, Path.GetFileName(files[i])), true);
            }

            string[] folders = Directory.GetDirectories(source);
            for (int i = 0; i < folders.Length; i++)
            {
                CopyDirectory(folders[i], Path.Combine(target, Path.GetFileName(folders[i])));
            }
        }

        static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
