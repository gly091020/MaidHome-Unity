using System.Collections.Generic;
using System.IO;
using System.Text;
using MaidHome.Core.Storage;
using MaidHome.Interop.Bedrock;
using MaidHome.Interop.Maid;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 读女仆存档（saves/maid/&lt;uuid&gt;/maid.json），把模型和动画转成 Unity 用的网格 + AnimationClip，
    /// 结果写进 persistentDataPath/cache/maid/&lt;uuid&gt;/。运行时的入口是 MaidAssetLoader.Load。
    /// </summary>
    public sealed class MaidSaveToolWindow : EditorWindow
    {
        [MenuItem("Tools/MaidHome/女仆存档")]
        static void Open()
        {
            MaidSaveToolWindow window = GetWindow<MaidSaveToolWindow>("女仆存档");
            window.minSize = new Vector2(460f, 520f);
        }

        string _maidRoot = "";
        readonly List<MaidSaveData> _maids = new List<MaidSaveData>();
        readonly List<bool> _cached = new List<bool>();
        int _selected;
        Vector2 _listScroll;
        Vector2 _reportScroll;
        string _report = "";
        GameObject _spawned;

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_maidRoot))
            {
                _maidRoot = AppPaths.MaidSaveRoot;
            }

            RefreshList();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("女仆存档 -> Unity 资源", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _maidRoot = EditorGUILayout.TextField("存档目录", _maidRoot);
            if (GUILayout.Button("选择…", GUILayout.Width(70f)))
            {
                string picked = EditorUtility.OpenFolderPanel("选择女仆存档目录", _maidRoot, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    _maidRoot = picked;
                    RefreshList();
                }
            }

            if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            {
                RefreshList();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("缓存目录: " + AppPaths.CacheRoot, EditorStyles.miniLabel);
            if (GUILayout.Button("打开缓存目录"))
            {
                AppPaths.EnsureDirectory(Path.Combine(AppPaths.CacheRoot, "maid"));
                EditorUtility.RevealInFinder(Path.Combine(AppPaths.CacheRoot, "maid"));
            }

            EditorGUILayout.Space();
            DrawList();
            EditorGUILayout.Space();
            DrawActions();

            EditorGUILayout.Space();
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void DrawList()
        {
            EditorGUILayout.LabelField("女仆 (" + _maids.Count + ")", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(140f));
            for (int i = 0; i < _maids.Count; i++)
            {
                MaidSaveData maid = _maids[i];
                string label = maid.Name + "  Lv" + maid.Level + "  " + maid.ModelFile
                    + (Mathf.Abs(maid.Scale - 1f) > 1e-4f ? "  ×" + maid.Scale.ToString("0.###") : "")
                    + (maid.SimpleBedrockModel ? "  [SimpleBedrock]" : "")
                    + (_cached[i] ? "  [已缓存]" : "") + (maid.Warnings.Count > 0 ? "  ⚠" : "");
                if (GUILayout.Toggle(_selected == i, label, "Button"))
                {
                    _selected = i;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("转换所选（写缓存）"))
            {
                Convert(Current());
            }

            if (GUILayout.Button("转换全部（写缓存）"))
            {
                ConvertAll();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("读缓存/转换并生成到场景"))
            {
                Spawn(Current());
            }

            if (GUILayout.Button("清所选缓存"))
            {
                ClearCache(false);
            }

            if (GUILayout.Button("清全部缓存"))
            {
                ClearCache(true);
            }

            EditorGUILayout.EndHorizontal();
        }

        MaidSaveData Current()
        {
            return _maids.Count == 0 ? null : _maids[Mathf.Clamp(_selected, 0, _maids.Count - 1)];
        }

        void RefreshList()
        {
            _maids.Clear();
            _cached.Clear();
            _maids.AddRange(MaidSaveData.ScanRoot(_maidRoot));
            for (int i = 0; i < _maids.Count; i++)
            {
                _cached.Add(MaidAssetCache.IsFresh(_maids[i]));
            }

            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _maids.Count - 1));
            _report = _maids.Count == 0
                ? "没扫到女仆。目录里应该是一个 uuid 一个文件夹，每个文件夹里有 maid.json。"
                : "扫到 " + _maids.Count + " 个女仆。";
        }

        void Convert(MaidSaveData maid)
        {
            if (maid == null)
            {
                _report = "先选一个女仆。";
                return;
            }

            MaidAssets assets = MaidAssetLoader.Build(maid);
            StringBuilder text = Describe(assets);
            if (assets.Root != null)
            {
                try
                {
                    MaidAssetCache.Write(maid, assets);
                    if (_maids.Count > 0)
                    {
                        _cached[Mathf.Clamp(_selected, 0, _cached.Count - 1)] = true;
                    }

                    text.AppendLine();
                    text.AppendLine("已写入缓存: " + MaidAssetCache.FolderOf(maid));
                    text.AppendLine("  缓存大小: " + Size(MaidAssetCache.FolderOf(maid)));
                }
                catch (System.Exception error)
                {
                    text.AppendLine("写缓存失败: " + error.Message);
                }
            }

            DestroyNow(assets.Root);
            _report = text.ToString();
        }

        void ConvertAll()
        {
            StringBuilder text = new StringBuilder();
            float start = Time.realtimeSinceStartup;
            int done = 0;
            for (int i = 0; i < _maids.Count; i++)
            {
                MaidSaveData maid = _maids[i];
                if (EditorUtility.DisplayCancelableProgressBar("转换女仆", maid.Name + " (" + (i + 1) + "/" + _maids.Count + ")", (float)i / _maids.Count))
                {
                    break;
                }

                MaidAssets assets = MaidAssetLoader.Build(maid);
                try
                {
                    if (assets.Root != null)
                    {
                        MaidAssetCache.Write(maid, assets);
                        _cached[i] = true;
                        done++;
                    }
                }
                catch (System.Exception error)
                {
                    text.AppendLine(maid.Name + " 写缓存失败: " + error.Message);
                }

                text.AppendLine(maid.Name + "  " + Summary(assets));
                DestroyNow(assets.Root);
            }

            EditorUtility.ClearProgressBar();
            text.AppendLine();
            text.AppendLine("完成 " + done + "/" + _maids.Count + "，共用时 " + (Time.realtimeSinceStartup - start).ToString("0.0") + " 秒");
            _report = text.ToString();
        }

        void Spawn(MaidSaveData maid)
        {
            if (maid == null)
            {
                _report = "先选一个女仆。";
                return;
            }

            DestroyNow(_spawned);
            MaidAssets assets = MaidAssetLoader.Load(maid);
            _spawned = assets.Root;
            if (_spawned != null)
            {
                Undo.RegisterCreatedObjectUndo(_spawned, "生成女仆模型");
                Selection.activeGameObject = _spawned;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            _report = Describe(assets).ToString();
        }

        void ClearCache(bool all)
        {
            if (all)
            {
                int count = 0;
                for (int i = 0; i < _maids.Count; i++)
                {
                    MaidAssetCache.Delete(_maids[i]);
                    count++;
                }

                _report = "清掉 " + count + " 份缓存。";
                return;
            }

            MaidSaveData maid = Current();
            if (maid == null)
            {
                _report = "先选一个女仆。";
                return;
            }

            MaidAssetCache.Delete(maid);
            _report = "已清掉 " + maid.Name + " 的缓存。";
        }

        static StringBuilder Describe(MaidAssets assets)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("女仆: " + (assets.Maid == null ? "?" : assets.Maid.Name + " (Lv" + assets.Maid.Level + ")"));
            text.AppendLine(Summary(assets));
            if (assets.Maid != null)
            {
                text.AppendLine("模型: " + assets.Maid.ModelFile + "   贴图: " + assets.Maid.TextureFile + "   动画: " + assets.Maid.AnimationFile);
            }

            for (int i = 0; i < assets.Warnings.Count; i++)
            {
                text.AppendLine("警告: " + assets.Warnings[i]);
            }

            return text;
        }

        static string Summary(MaidAssets assets)
        {
            if (assets.Root == null)
            {
                return "转换失败（" + assets.Warnings.Count + " 条警告）";
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
                keyframes += assets.ClipData[i].KeyframeCount;
            }

            string hidden = "";
            for (int i = 0; i < assets.HiddenNodes.Count; i++)
            {
                hidden += (i == 0 ? "" : ",") + assets.HiddenNodes[i];
            }

            return "骨骼=" + Mathf.Max(0, transforms.Length - 1) + " 网格=" + filters.Length + " 顶点=" + vertices
                + " 动画=" + assets.Clips.Count + " 关键帧=" + keyframes
                + (hidden.Length > 0 ? " 已隐藏=" + hidden : "")
                + (assets.Maid != null && assets.Maid.SimpleBedrockModel ? "  SimpleBedrock" : "")
                + (assets.Maid != null && Mathf.Abs(assets.Maid.Scale - 1f) > 1e-4f ? " 缩放=×" + assets.Maid.Scale.ToString("0.###") : "")
                + " 耗时=" + assets.BuildSeconds.ToString("0.00") + "秒"
                + (assets.FromCache ? "  [来自缓存]" : "");
        }

        static string Size(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return "0";
            }

            long bytes = 0;
            string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                bytes += new FileInfo(files[i]).Length;
            }

            return (bytes / 1024f).ToString("0.0") + " KB";
        }

        static void DestroyNow(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
