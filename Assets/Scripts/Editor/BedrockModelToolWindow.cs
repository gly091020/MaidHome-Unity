using System.Collections.Generic;
using System.IO;
using System.Text;
using MaidHome.Interop.Bedrock;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    public sealed class BedrockModelToolWindow : EditorWindow
    {
        [MenuItem("Tools/MaidHome/基岩模型转换")]
        static void Open()
        {
            BedrockModelToolWindow window = GetWindow<BedrockModelToolWindow>("基岩模型");
            window.minSize = new Vector2(420f, 460f);
        }

        string _modelPath = "";
        Texture2D _texture;
        float _pixelsPerUnit = 16f;
        bool _cutout = true;
        bool _flipX;
        bool _flipY;
        bool _flipZ;
        BedrockRotationOrder _rotationOrder = BedrockRotationOrder.ZYX;
        string _assetFolder = "Assets/Generated/BedrockModels";
        GameObject _preview;
        BedrockModelBuilder _previewBuilder;
        readonly List<GameObject> _previews = new List<GameObject>();
        Vector2 _reportScroll;
        string _report = "";

        string _animationPath = "";
        BedrockAnimationSet _animationSet;
        readonly List<AnimationClip> _clips = new List<AnimationClip>();
        int _selectedClip;
        float _previewTime;
        float _previewSpeed = 1f;
        bool _previewPlaying;
        double _lastUpdate = -1d;

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopPreview();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("基岩模型转换", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _modelPath = EditorGUILayout.TextField("模型 JSON", _modelPath);
            if (GUILayout.Button("选择…", GUILayout.Width(70f)))
            {
                string picked = EditorUtility.OpenFilePanel("选择基岩模型 JSON", "", "json");
                if (!string.IsNullOrEmpty(picked))
                {
                    _modelPath = picked;
                    _texture = null;
                    Analyze();
                }
            }

            EditorGUILayout.EndHorizontal();

            _texture = (Texture2D)EditorGUILayout.ObjectField("贴图", _texture, typeof(Texture2D), false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("从模型同目录读取贴图"))
            {
                TryImportTexture();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            _pixelsPerUnit = EditorGUILayout.FloatField("1 格像素数", _pixelsPerUnit);
            _cutout = EditorGUILayout.Toggle("Cutout 材质", _cutout);
            EditorGUILayout.BeginHorizontal();
            _flipX = EditorGUILayout.ToggleLeft("翻转 X", _flipX, GUILayout.Width(80f));
            _flipY = EditorGUILayout.ToggleLeft("翻转 Y", _flipY, GUILayout.Width(80f));
            _flipZ = EditorGUILayout.ToggleLeft("翻转 Z", _flipZ, GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();
            _rotationOrder = (BedrockRotationOrder)EditorGUILayout.EnumPopup("旋转顺序", _rotationOrder);
            _assetFolder = EditorGUILayout.TextField("保存目录", _assetFolder);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("解析并输出报告"))
            {
                Analyze();
            }

            if (GUILayout.Button("生成到场景"))
            {
                BuildPreview();
            }

            if (GUILayout.Button("清掉预览"))
            {
                ClearPreview();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成 48 种旋转组合对比"))
            {
                BuildRotationComparison();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存为 Prefab"))
            {
                SavePrefab();
            }

            EditorGUILayout.EndHorizontal();

            DrawAnimationSection();

            EditorGUILayout.Space();
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void DrawAnimationSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("动画（winefox.animation.json 这类文件）", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _animationPath = EditorGUILayout.TextField("动画 JSON", _animationPath);
            if (GUILayout.Button("选择…", GUILayout.Width(70f)))
            {
                string picked = EditorUtility.OpenFilePanel("选择基岩动画 JSON", "", "json");
                if (!string.IsNullOrEmpty(picked))
                {
                    _animationPath = picked;
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导入动画"))
            {
                ImportAnimations();
            }

            if (GUILayout.Button("清掉动画"))
            {
                ClearAnimations();
            }

            EditorGUILayout.EndHorizontal();

            if (_clips.Count == 0)
            {
                EditorGUILayout.LabelField("提示：先「生成到场景」拿到模型，再导入动画。", EditorStyles.miniLabel);
                return;
            }

            string[] names = new string[_clips.Count];
            for (int i = 0; i < _clips.Count; i++)
            {
                names[i] = _clips[i].name + "  (" + _clips[i].length.ToString("0.###") + " 秒)";
            }

            int selected = EditorGUILayout.Popup("动画", Mathf.Clamp(_selectedClip, 0, _clips.Count - 1), names);
            if (selected != _selectedClip)
            {
                _selectedClip = selected;
                _previewTime = 0f;
                SamplePreview();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_previewPlaying ? "暂停" : "播放"))
            {
                TogglePreview();
            }

            if (GUILayout.Button("回到开头"))
            {
                _previewTime = 0f;
                SamplePreview();
            }

            _previewSpeed = EditorGUILayout.FloatField("速度", _previewSpeed);
            EditorGUILayout.EndHorizontal();

            AnimationClip current = CurrentClip();
            float length = current == null ? 0f : current.length;
            float time = EditorGUILayout.Slider("时间", _previewTime, 0f, Mathf.Max(length, 0.001f));
            if (Mathf.Abs(time - _previewTime) > 1e-6f)
            {
                _previewTime = time;
                SamplePreview();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存所选为 .anim"))
            {
                SaveClips(false);
            }

            if (GUILayout.Button("保存全部为 .anim"))
            {
                SaveClips(true);
            }

            EditorGUILayout.EndHorizontal();
        }

        void ImportAnimations()
        {
            StopPreview();
            ClearAnimations();

            if (_preview == null)
            {
                _report = "场景里还没有模型，先点「生成到场景」。";
                return;
            }

            if (string.IsNullOrEmpty(_animationPath) || !File.Exists(_animationPath))
            {
                _report = "动画文件不存在: " + _animationPath;
                return;
            }

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                _report = "模型文件丢了，重新选一下模型 JSON。";
                return;
            }

            BedrockGeometry geometry;
            try
            {
                geometry = BedrockGeometry.ParseFile(_modelPath);
            }
            catch (System.Exception error)
            {
                _report = "模型解析失败: " + error.Message;
                return;
            }

            try
            {
                _animationSet = BedrockAnimationSet.ParseFile(_animationPath);
            }
            catch (System.Exception error)
            {
                _report = "动画解析失败: " + error.Message;
                return;
            }

            BedrockAnimationClipBuilder builder = new BedrockAnimationClipBuilder();
            if (_previewBuilder != null)
            {
                builder.PixelsPerUnit = _previewBuilder.PixelsPerUnit;
                builder.RotationSigns = _previewBuilder.RotationSigns;
                builder.RotationOrder = _previewBuilder.RotationOrder;
            }

            for (int i = 0; i < _animationSet.Animations.Count; i++)
            {
                _clips.Add(builder.Build(_animationSet.Animations[i], geometry));
            }

            Animation player = _preview.GetComponent<Animation>();
            if (player == null)
            {
                player = _preview.AddComponent<Animation>();
            }

            for (int i = 0; i < _clips.Count; i++)
            {
                if (player.GetClip(_clips[i].name) != null)
                {
                    player.RemoveClip(_clips[i].name);
                }

                player.AddClip(_clips[i], _clips[i].name);
            }

            _selectedClip = 0;
            _previewTime = 0f;
            SamplePreview();
            ReportAnimations();
        }

        void ReportAnimations()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("动画导入完成，共 " + _clips.Count + " 条。");
            text.AppendLine("预览用的是 AnimationMode（编辑器里的动画预览），点「暂停」会恢复场景原来的姿势。");
            text.AppendLine();
            for (int i = 0; i < _clips.Count; i++)
            {
                AnimationClip clip = _clips[i];
                BedrockAnimation source = _animationSet == null ? null : _animationSet.Find(clip.name);
                int bones = source == null ? 0 : source.Bones.Count;
                string note = "";
                if (source != null && source.Unbounded)
                {
                    note = source.ProceduralPeriod > 0f ? "  (无限长动画，按 anim_time 周期烘成循环)" : "  (无限长动画，静止姿势)";
                }
                else if (source != null && source.LengthFromKeys)
                {
                    note = "  (时长按最后一个关键帧推出)";
                }

                text.AppendLine(clip.name + "  时长=" + clip.length.ToString("0.###") + "秒"
                    + "  循环=" + (clip.wrapMode == WrapMode.Loop ? "是" : "否")
                    + "  骨骼=" + bones
                    + note);
            }

            if (_animationSet != null && _animationSet.Warnings.Count > 0)
            {
                text.AppendLine();
                for (int i = 0; i < _animationSet.Warnings.Count; i++)
                {
                    text.AppendLine("警告: " + _animationSet.Warnings[i]);
                }
            }

            _report = text.ToString();
        }

        void ClearAnimations()
        {
            StopPreview();
            _clips.Clear();
            _animationSet = null;
            _selectedClip = 0;
            _previewTime = 0f;
            if (_preview != null)
            {
                Animation player = _preview.GetComponent<Animation>();
                if (player != null)
                {
                    player.Stop();
                }
            }
        }

        AnimationClip CurrentClip()
        {
            if (_clips.Count == 0)
            {
                return null;
            }

            return _clips[Mathf.Clamp(_selectedClip, 0, _clips.Count - 1)];
        }

        void TogglePreview()
        {
            if (_previewPlaying)
            {
                _previewPlaying = false;
                return;
            }

            if (_preview == null || CurrentClip() == null)
            {
                return;
            }

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            _previewPlaying = true;
            _lastUpdate = -1d;
        }

        void StopPreview()
        {
            _previewPlaying = false;
            _lastUpdate = -1d;
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
        }

        void SamplePreview()
        {
            AnimationClip clip = CurrentClip();
            if (clip == null || _preview == null)
            {
                return;
            }

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            AnimationMode.SampleAnimationClip(_preview, clip, _previewTime);
            SceneView.RepaintAll();
        }

        void OnEditorUpdate()
        {
            if (!_previewPlaying)
            {
                return;
            }

            AnimationClip clip = CurrentClip();
            if (clip == null || _preview == null)
            {
                _previewPlaying = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float delta = _lastUpdate < 0d ? 0f : (float)(now - _lastUpdate);
            _lastUpdate = now;
            float length = Mathf.Max(clip.length, 1e-4f);
            _previewTime += delta * _previewSpeed;
            if (_previewTime > length)
            {
                if (clip.wrapMode == WrapMode.Loop)
                {
                    _previewTime = Mathf.Repeat(_previewTime, length);
                }
                else
                {
                    _previewTime = length;
                    _previewPlaying = false;
                }
            }

            SamplePreview();
            Repaint();
        }

        void SaveClips(bool all)
        {
            if (_clips.Count == 0)
            {
                _report = "还没有导入动画。";
                return;
            }

            string folder = _assetFolder.TrimEnd('/') + "/Animations";
            int count = 0;
            for (int i = 0; i < _clips.Count; i++)
            {
                if (!all && i != _selectedClip)
                {
                    continue;
                }

                if (BedrockAnimationAssetWriter.SaveClip(_clips[i], folder, _clips[i].name) != null)
                {
                    count++;
                }
            }

            _report = "已保存 " + count + " 个 .anim 到 " + folder;
        }

        void Analyze()
        {
            StringBuilder builder = new StringBuilder();
            if (string.IsNullOrEmpty(_modelPath))
            {
                _report = "还没选模型文件。";
                return;
            }

            if (!File.Exists(_modelPath))
            {
                _report = "文件不存在: " + _modelPath;
                return;
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

            int cubeCount = 0;
            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                cubeCount += geometry.Bones[i].Cubes.Count;
            }

            builder.AppendLine("identifier: " + geometry.Identifier);
            builder.AppendLine("format_version: " + geometry.FormatVersion);
            builder.AppendLine("声明贴图尺寸: " + geometry.TextureWidth + " x " + geometry.TextureHeight);
            builder.AppendLine("骨骼数: " + geometry.Bones.Count + "   方块数: " + cubeCount);
            builder.AppendLine("贴图: " + (_texture != null ? _texture.name + " (" + _texture.width + "x" + _texture.height + ")" : "未指定"));
            if (_texture != null && (Mathf.Abs(_texture.width - geometry.TextureWidth) > 0.5f || Mathf.Abs(_texture.height - geometry.TextureHeight) > 0.5f))
            {
                builder.AppendLine("注意: 贴图实际尺寸和模型声明不一致。UV 按模型声明的尺寸换算，如果显示错位就说明声明写错了。");
            }

            for (int i = 0; i < geometry.Warnings.Count; i++)
            {
                builder.AppendLine("警告: " + geometry.Warnings[i]);
            }

            _report = builder.ToString();
        }

        void TryImportTexture()
        {
            if (string.IsNullOrEmpty(_modelPath))
            {
                _report = "先选模型文件。";
                return;
            }

            string pngPath = Path.ChangeExtension(_modelPath, ".png");
            if (!File.Exists(pngPath))
            {
                _report = "同目录下没有找到 " + Path.GetFileName(pngPath);
                return;
            }

            string folder = _assetFolder.TrimEnd('/') + "/Textures";
            _texture = BedrockModelAssetWriter.ImportTexture(pngPath, folder);
            _report = _texture != null
                ? "已导入贴图: " + AssetDatabase.GetAssetPath(_texture) + "，已设为 Point / Clamp / 不压缩。"
                : "贴图导入失败。";
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
                TryImportTexture();
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
            builder.CutoutMaterial = _cutout;
            builder.RotationSigns = new Vector3(_flipX ? -1f : 1f, _flipY ? -1f : 1f, _flipZ ? -1f : 1f);
            builder.RotationOrder = _rotationOrder;
            builder.RootName = Path.GetFileNameWithoutExtension(_modelPath);

            BedrockModelBuildResult result = builder.Build(geometry, _texture);
            _preview = result.Root;
            _previewBuilder = builder;
            _previews.Clear();
            if (_preview != null)
            {
                _previews.Add(_preview);
                Undo.RegisterCreatedObjectUndo(_preview, "生成基岩模型");
                Selection.activeGameObject = _preview;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            StringBuilder text = new StringBuilder();
            text.AppendLine("生成完成: " + builder.RootName);
            text.AppendLine("顶点数: " + result.VertexCount + "   三角面: " + result.TriangleCount);
            for (int i = 0; i < result.Warnings.Count; i++)
            {
                text.AppendLine("警告: " + result.Warnings[i]);
            }

            _report = text.ToString();
            Repaint();
        }

        void BuildRotationComparison()
        {
            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                _report = "先选一个存在的模型文件。";
                return;
            }

            if (_texture == null)
            {
                TryImportTexture();
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
            StringBuilder text = new StringBuilder();
            text.AppendLine("按 8 列 6 行摆放，名字格式: 符号_顺序，例如 +X-Y+Z_XZY 表示 X 不翻、Y 翻转、Z 不翻，顺序 XZY。");
            text.AppendLine("找到头发姿势正确的那一个，把名字告诉我。");
            text.AppendLine();

            int index = 0;
            for (int signBits = 0; signBits < 8; signBits++)
            {
                for (int orderIndex = 0; orderIndex < 6; orderIndex++)
                {
                    BedrockModelBuilder builder = new BedrockModelBuilder();
                    builder.PixelsPerUnit = Mathf.Max(1f, _pixelsPerUnit);
                    builder.CutoutMaterial = _cutout;
                    builder.RotationSigns = new Vector3(
                        (signBits & 1) == 0 ? 1f : -1f,
                        (signBits & 2) == 0 ? 1f : -1f,
                        (signBits & 4) == 0 ? 1f : -1f);
                    builder.RotationOrder = (BedrockRotationOrder)orderIndex;
                    builder.RootName = Label(builder.RotationSigns, builder.RotationOrder);

                    BedrockModelBuildResult result = builder.Build(geometry, _texture);
                    if (result.Root == null)
                    {
                        continue;
                    }

                    result.Root.transform.position = new Vector3((index % 8) * 1.2f, 0f, -(index / 8) * 1.2f);
                    _previews.Add(result.Root);
                    index++;
                }
            }

            if (_previews.Count > 0)
            {
                Selection.activeGameObject = _previews[0];
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            _report = text.ToString();
            Repaint();
        }

        static string Label(Vector3 signs, BedrockRotationOrder order)
        {
            return (signs.x > 0f ? "+X" : "-X")
                + (signs.y > 0f ? "+Y" : "-Y")
                + (signs.z > 0f ? "+Z" : "-Z")
                + "_" + order;
        }

        void ClearPreview()
        {
            StopPreview();
            _previewBuilder = null;
            for (int i = 0; i < _previews.Count; i++)
            {
                if (_previews[i] != null)
                {
                    DestroyImmediate(_previews[i]);
                }
            }

            _previews.Clear();
            _preview = null;
        }

        void SavePrefab()
        {
            if (_preview == null)
            {
                BuildPreview();
            }

            if (_preview == null)
            {
                return;
            }

            GameObject prefab = BedrockModelAssetWriter.SaveAsPrefab(_preview, _assetFolder, _preview.name);
            _report = prefab != null
                ? "已保存: " + AssetDatabase.GetAssetPath(prefab)
                : "保存失败。";
        }
    }
}
