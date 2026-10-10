using System.Collections.Generic;
using System.IO;
using System.Text;
using MaidHome.Gameplay.Maid;
using MaidHome.Interop.Bedrock;
using MaidHome.Interop.Maid;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 女仆布娃娃工具（简化版）：
    ///
    /// 1. 选一个存档（或模型 JSON）→「加载到场景」，编辑模式只做这一步和看效果；
    /// 2. 「看效果」在 Scene 里画出会生成的碰撞盒和关节连线，不用进 Play；
    /// 3. 「挂上 MaidRagdoll 组件」—— 之后**进 Play 就自动生成并接管**，不用再点任何按钮。
    ///
    /// 编辑模式**不会**提前建刚体（以前那套进 Play 以后运行时列表是空的、身上挂着
    /// 上一轮组件的坑就是这么来的）。Play 里如果想手动控制，还有进入/退出两个按钮。
    /// </summary>
    public sealed class MaidRagdollToolWindow : EditorWindow
    {
        [MenuItem("Tools/MaidHome/女仆布娃娃")]
        static void Open()
        {
            MaidRagdollToolWindow window = GetWindow<MaidRagdollToolWindow>("女仆布娃娃");
            window.minSize = new Vector2(440f, 500f);
        }

        // 存档
        readonly List<MaidSaveData> _maids = new List<MaidSaveData>();
        int _maidIndex = -1;
        MaidAssets _assets;

        // 模型
        string _modelPath = "";
        Texture2D _texture;
        float _pixelsPerUnit = 16f;
        string _assetFolder = "Assets/Generated/BedrockModels";

        // 布娃娃
        GameObject _target;
        MaidRagdollPlan _plan;
        readonly List<KeyValuePair<int, int>> _overlaps = new List<KeyValuePair<int, int>>();
        bool _drawPreview = true;
        string _report = "";
        Vector2 _scroll;

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            RefreshMaids(false);
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("女仆布娃娃（简化版）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("选存档/模型 → 加载到场景 → 看效果 → 挂上组件 → 进 Play 自动变成布娃娃",
                EditorStyles.miniLabel);
            EditorGUILayout.Space();

            DrawSaveSection();
            EditorGUILayout.Space();
            DrawModelSection();
            EditorGUILayout.Space();
            DrawRagdollSection();

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void DrawSaveSection()
        {
            EditorGUILayout.LabelField("从存档选择", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新列表", GUILayout.Width(80f)))
            {
                RefreshMaids(true);
            }

            if (GUILayout.Button("打开存档目录", GUILayout.Width(100f)))
            {
                EditorUtility.RevealInFinder(MaidHome.Core.Storage.AppPaths.MaidSaveRoot);
            }

            EditorGUILayout.LabelField(_maids.Count + " 只", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (_maids.Count == 0)
            {
                EditorGUILayout.LabelField("存档目录里没有女仆（" + MaidHome.Core.Storage.AppPaths.MaidSaveRoot + "）",
                    EditorStyles.miniLabel);
                return;
            }

            string[] labels = new string[_maids.Count];
            for (int i = 0; i < _maids.Count; i++)
            {
                MaidSaveData maid = _maids[i];
                labels[i] = (string.IsNullOrEmpty(maid.Name) ? maid.Id : maid.Name)
                    + "  (" + Path.GetFileName(maid.ModelFile) + ")";
            }

            _maidIndex = EditorGUILayout.Popup("女仆", Mathf.Clamp(_maidIndex, 0, _maids.Count - 1), labels);
            if (GUILayout.Button("加载到场景"))
            {
                LoadFromSave();
            }
        }

        void DrawModelSection()
        {
            EditorGUILayout.LabelField("或：从模型文件", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _modelPath = EditorGUILayout.TextField("模型 JSON", _modelPath);
            if (GUILayout.Button("选择…", GUILayout.Width(70f)))
            {
                string picked = EditorUtility.OpenFilePanel("选择基岩模型 JSON", "", "json");
                if (!string.IsNullOrEmpty(picked))
                {
                    _modelPath = picked;
                    _texture = null;
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _texture = (Texture2D)EditorGUILayout.ObjectField("贴图", _texture, typeof(Texture2D), false);
            _pixelsPerUnit = EditorGUILayout.FloatField("1 格像素", _pixelsPerUnit, GUILayout.Width(150f));
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("生成模型到场景"))
            {
                BuildPreview();
            }
        }

        void DrawRagdollSection()
        {
            EditorGUILayout.LabelField("布娃娃（网格方案）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("头 / 身体 / 裙子会把自己子树里的网格合成一块；"
                + "手、脚、尾巴按段各自成块（手肘膝盖尾巴各节能弯）；其余网格各自一块。",
                EditorStyles.miniLabel);
            _drawPreview = EditorGUILayout.ToggleLeft("在 Scene 里画效果（碰撞盒 + 关节连线）", _drawPreview);

            EditorGUILayout.LabelField("目标：" + (_target != null ? _target.name : "（先从上面加载一个模型）"),
                EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("看效果（只算不建）"))
            {
                Analyze();
            }

            if (GUILayout.Button("挂上 MaidRagdoll 组件"))
            {
                AttachComponent();
            }

            if (GUILayout.Button("把刚体全拆掉"))
            {
                ClearComponents();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            if (GUILayout.Button("进入布娃娃"))
            {
                Enter();
            }

            if (GUILayout.Button("退出布娃娃"))
            {
                Exit();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(Application.isPlaying
                ? "Play 中：挂了组件的话会自动进场，也能用上面两个按钮手动切。"
                : "编辑模式只搭结构；进 Play 以后组件会自动生成布娃娃。", EditorStyles.miniLabel);
        }

        // ------------------------------------------------------------ 场景预览

        void OnSceneGui(SceneView view)
        {
            if (!_drawPreview || _plan == null)
            {
                return;
            }

            for (int i = 0; i < _plan.Parts.Count; i++)
            {
                MaidRagdollPart part = _plan.Parts[i];
                if (part.Bone == null)
                {
                    continue;
                }

                bool overlapping = false;
                for (int o = 0; o < _overlaps.Count; o++)
                {
                    if (_overlaps[o].Key == part.Index || _overlaps[o].Value == part.Index)
                    {
                        overlapping = true;
                        break;
                    }
                }

                Handles.color = part.Parent < 0
                    ? new Color(1f, 0.7f, 0.2f, 0.9f)
                    : overlapping ? new Color(1f, 0.3f, 0.3f, 0.95f) : new Color(0.3f, 0.9f, 0.4f, 0.9f);
                for (int c = 0; c < part.Colliders.Count; c++)
                {
                    MaidRagdollCollider entry = part.Colliders[c];
                    if (entry.Bone == null)
                    {
                        continue;
                    }

                    Handles.matrix = entry.Bone.localToWorldMatrix;
                    Bounds bounds = entry.IsMesh
                        ? entry.Mesh.bounds
                        : new Bounds(entry.Center, entry.Size);
                    Handles.DrawWireCube(bounds.center, bounds.size);
                }

                Handles.matrix = Matrix4x4.identity;
                if (part.Parent >= 0)
                {
                    MaidRagdollPart parent = _plan.At(part.Parent);
                    if (parent != null && parent.Bone != null)
                    {
                        Handles.color = new Color(0.3f, 0.7f, 1f, 0.8f);
                        Vector3 from = part.UseJointAnchor ? part.JointAnchor : part.Bone.position;
                        Handles.DrawLine(from, parent.Bone.position);
                    }
                }
            }

            // 重叠的部位之间画红线：这些对不碰撞、关节收紧角度
            Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
            for (int o = 0; o < _overlaps.Count; o++)
            {
                MaidRagdollPart a = _plan.At(_overlaps[o].Key);
                MaidRagdollPart b = _plan.At(_overlaps[o].Value);
                if (a != null && b != null && a.Bone != null && b.Bone != null)
                {
                    Handles.DrawLine(a.Bone.position, b.Bone.position);
                }
            }

            Handles.matrix = Matrix4x4.identity;
        }

        // ------------------------------------------------------------ 操作

        void RefreshMaids(bool report)
        {
            _maids.Clear();
            _maids.AddRange(MaidLoader.List());
            if (_maidIndex >= _maids.Count)
            {
                _maidIndex = _maids.Count - 1;
            }

            if (report)
            {
                _report = "找到 " + _maids.Count + " 只女仆。";
            }
        }

        void LoadFromSave()
        {
            if (_maidIndex < 0 || _maidIndex >= _maids.Count)
            {
                _report = "先选一只女仆。";
                return;
            }

            MaidSaveData maid = _maids[_maidIndex];
            MaidAssets assets = MaidLoader.Load(maid);
            if (assets == null || assets.Root == null)
            {
                _report = "加载失败：" + maid.Id;
                return;
            }

            ClearPreview();
            _assets = assets;
            _target = assets.Root;
            _target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _target.SetActive(true);
            Undo.RegisterCreatedObjectUndo(_target, "加载女仆模型");
            Selection.activeGameObject = _target;
            SceneView.lastActiveSceneView?.FrameSelected();
            Analyze();
        }

        void BuildPreview()
        {
            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                _report = "先选一个存在的模型文件。";
                return;
            }

            if (_texture == null)
            {
                string png = Path.ChangeExtension(_modelPath, ".png");
                if (File.Exists(png))
                {
                    _texture = BedrockModelAssetWriter.ImportTexture(png, _assetFolder.TrimEnd('/') + "/Textures");
                }
            }

            BedrockGeometry geometry;
            try
            {
                geometry = BedrockGeometry.ParseFile(_modelPath);
            }
            catch (System.Exception error)
            {
                _report = "解析失败: " + error.Message;
                return;
            }

            ClearPreview();
            BedrockModelBuilder builder = new BedrockModelBuilder();
            builder.PixelsPerUnit = Mathf.Max(1f, _pixelsPerUnit);
            builder.RootName = Path.GetFileNameWithoutExtension(_modelPath);
            BedrockModelBuildResult result = builder.Build(geometry, _texture);
            _target = result.Root;
            if (_target != null)
            {
                Undo.RegisterCreatedObjectUndo(_target, "生成女仆模型");
                Selection.activeGameObject = _target;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            Analyze();
        }

        void Analyze()
        {
            if (_target == null)
            {
                _report = "先加载一个模型（存档或模型 JSON）。";
                return;
            }

            _plan = MaidRagdollPlanner.Plan(_target.transform);
            // 精确版重叠判定：临时搭一遍碰撞体问 PhysX（凸包/盒是几何精确的），判完就拆
            _overlaps.Clear();
            _overlaps.AddRange(MaidRagdollOverlap.Find(_plan));
            StringBuilder text = new StringBuilder();
            text.AppendLine("目标: " + _target.name);
            text.AppendLine();
            text.Append(_plan.Describe());
            text.AppendLine();
            text.AppendLine();
            text.AppendLine("重叠 " + _overlaps.Count + " 对（红框/红线）：这些部位之间不互相碰撞，关角度默认跟普通关节一样。");
            text.AppendLine("Scene 里：绿框 = 碰撞盒，蓝线 = 关节，橙框 = 根块，红 = 重叠的那几块。");
            text.Append("满意就点「挂上 MaidRagdoll 组件」，然后进 Play —— 会自动变成布娃娃。");
            _report = text.ToString();
            SceneView.RepaintAll();
        }

        void AttachComponent()
        {
            if (_target == null)
            {
                _report = "先加载一个模型。";
                return;
            }

            MaidRagdoll ragdoll = _target.GetComponent<MaidRagdoll>();
            if (ragdoll == null)
            {
                ragdoll = Undo.AddComponent<MaidRagdoll>(_target);
            }

            _report = "已挂上 MaidRagdoll。进 Play 会自动生成并接管；"
                + "在 Play 里也可以用窗口的进入/退出按钮手动控制。";
        }

        void ClearComponents()
        {
            if (_target == null)
            {
                _report = "先加载一个模型。";
                return;
            }

            MaidRagdoll ragdoll = _target.GetComponent<MaidRagdoll>();
            if (ragdoll != null)
            {
                ragdoll.Exit();
                ragdoll.Clear();
                Undo.DestroyObjectImmediate(ragdoll);
            }

            _report = "刚体 / 关节 / 碰撞体都拆了。";
            SceneView.RepaintAll();
        }

        void Enter()
        {
            MaidRagdoll ragdoll = _target != null ? _target.GetComponent<MaidRagdoll>() : null;
            if (ragdoll == null)
            {
                _report = "先在编辑模式点「挂上 MaidRagdoll 组件」。";
                return;
            }

            ragdoll.Rebuild();
            ragdoll.Enter();
            _report = "已交给物理：" + ragdoll.PartCount + " 根刚体。"
                + "\n自碰撞：" + (ragdoll.SelfCollisionDisabled ? "全关" : "开")
                + "；角度限制：" + (ragdoll.AngleLimitsDisabled ? "全关（关节自由）" : "开")
                + "；重叠 " + ragdoll.OverlapPairCount + " 对。";
        }

        void Exit()
        {
            MaidRagdoll ragdoll = _target != null ? _target.GetComponent<MaidRagdoll>() : null;
            if (ragdoll == null)
            {
                _report = "没有 MaidRagdoll 组件。";
                return;
            }

            ragdoll.Exit();
            _report = "已退回动画。";
        }

        void ClearPreview()
        {
            if (_assets != null)
            {
                if (_assets.Root != null)
                {
                    Undo.DestroyObjectImmediate(_assets.Root);
                    _assets.Root = null;
                }

                _assets = null;
            }

            if (_target != null)
            {
                Undo.DestroyObjectImmediate(_target);
            }

            _target = null;
            _plan = null;
        }
    }
}
