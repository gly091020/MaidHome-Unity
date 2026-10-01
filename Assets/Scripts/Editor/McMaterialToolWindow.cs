using System.Collections.Generic;
using System.Text;
using MaidHome.Interop.Gltf;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 把选中物体的材质换成 MaidHome/MinecraftBlock。
    /// 场景里直接拖进来的 glb 用 glTFast 的 PBR 材质（glTF/PbrMetallicRoughness），
    /// 跟场景的 MC 平光对不上，跑一下这个就统一了。运行时加载的模型不需要用这个。
    /// </summary>
    public sealed class McMaterialToolWindow : EditorWindow
    {
        const string ShaderName = "MaidHome/MinecraftBlock";
        const string MaterialsFolder = "Assets/Materials";
        const string OutputFolder = "Assets/Materials/Generated";

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int BaseColorTextureId = Shader.PropertyToID("baseColorTexture");
        static readonly int BaseColorFactorId = Shader.PropertyToID("baseColorFactor");
        static readonly int GtlfCutoffId = Shader.PropertyToID("alphaCutoff");
        static readonly int GtlfCullId = Shader.PropertyToID("_CullMode");

        [MenuItem("Tools/MaidHome/MC 材质")]
        static void Open()
        {
            McMaterialToolWindow window = GetWindow<McMaterialToolWindow>("MC 材质");
            window.minSize = new Vector2(480f, 420f);
        }

        bool _pointFilter = true;
        string _report = "";
        Vector2 _scroll;

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "把选中物体（含子物体）的材质换成 " + ShaderName + "。\n" +
                "只取 baseColor 的贴图和颜色，金属度 / 粗糙度 / 法线 / 遮蔽丢掉。\n" +
                "新材质存进 " + OutputFolder + "，同一张贴图会复用同一个材质。",
                MessageType.Info);

            _pointFilter = EditorGUILayout.ToggleLeft("顺带把工程里的贴图资源改成点采样", _pointFilter);
            EditorGUILayout.Space();

            if (GUILayout.Button("把选中物体换成 MC 材质", GUILayout.Height(30f)))
            {
                Apply(false);
            }

            if (GUILayout.Button("只列现状，不改", GUILayout.Height(22f)))
            {
                Apply(true);
            }

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void Apply(bool dryRun)
        {
            Renderer[] renderers = Collect();
            if (renderers.Length == 0)
            {
                _report = "先在场景里选中模型（根节点就行）。";
                return;
            }

            StringBuilder report = new StringBuilder();
            int converted = 0;
            int created = 0;
            int reused = 0;
            int skipped = 0;
            int textures = 0;

            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                bool dirty = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null)
                    {
                        skipped++;
                        continue;
                    }

                    string sourceShader = source.shader != null ? source.shader.name : "null";
                    if (sourceShader == ShaderName)
                    {
                        report.AppendLine("已是 MC 材质，跳过: " + renderer.name + " / " + source.name);
                        skipped++;
                        continue;
                    }

                    if (dryRun)
                    {
                        report.AppendLine(renderer.name + " / " + source.name + "  [" + sourceShader + "]");
                        continue;
                    }

                    Material mc = GetOrCreate(source, ref created, ref reused);
                    if (mc == null)
                    {
                        report.AppendLine("生成失败: " + renderer.name + " / " + source.name);
                        skipped++;
                        continue;
                    }

                    if (_pointFilter)
                    {
                        MakePointSampled(mc.mainTexture, ref textures);
                    }

                    materials[i] = mc;
                    dirty = true;
                    converted++;
                    report.AppendLine(renderer.name + " / " + source.name + "  [" + sourceShader + "] -> " + mc.name);
                }

                if (dirty)
                {
                    Undo.RecordObject(renderer, "换成 MC 材质");
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                }
            }

            if (!dryRun && created > 0)
            {
                AssetDatabase.SaveAssets();
            }

            string head = dryRun
                ? "只检查，没动东西。\n"
                : string.Format("换了 {0} 个材质槽，新建 {1} 个材质，复用 {2} 个，跳过 {3} 个，改点采样 {4} 张贴图。\n",
                    converted, created, reused, skipped, textures);
            _report = head + report;
            Repaint();
        }

        static Renderer[] Collect()
        {
            List<Renderer> list = new List<Renderer>();
            foreach (GameObject go in Selection.gameObjects)
            {
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && !list.Contains(renderer))
                    {
                        list.Add(renderer);
                    }
                }
            }

            return list.ToArray();
        }

        Material GetOrCreate(Material source, ref int created, ref int reused)
        {
            Texture2D texture = GetBaseColorTexture(source);
            Color color = GetBaseColor(source);
            float cutoff = GetCutoff(source);
            bool cullBack = GetCullBack(source);

            EnsureOutputFolder();

            string baseName = texture != null && !string.IsNullOrEmpty(texture.name)
                ? Sanitize(texture.name)
                : Sanitize(source.name);

            for (int i = 1; i < 100; i++)
            {
                string name = i == 1 ? baseName + "_mc" : baseName + "_mc_" + i;
                string path = OutputFolder + "/" + name + ".mat";
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing == null)
                {
                    Material createdMaterial = McGltfMaterialGenerator.CreateMaterial(name, color, texture, cutoff, cullBack);
                    createdMaterial.name = name;
                    AssetDatabase.CreateAsset(createdMaterial, path);
                    created++;
                    return createdMaterial;
                }

                // 同一张贴图就复用：重跑工具不会覆盖你手动调过的染色、裁切这些
                if (existing.shader != null && existing.shader.name == ShaderName
                    && existing.mainTexture == texture)
                {
                    reused++;
                    return existing;
                }
            }

            return null;
        }

        static Texture2D GetBaseColorTexture(Material source)
        {
            // glTFast 的材质用 baseColorTexture，其它材质退回 _MainTex
            if (source.HasProperty(BaseColorTextureId))
            {
                Texture gltfTexture = source.GetTexture(BaseColorTextureId);
                if (gltfTexture != null)
                {
                    return gltfTexture as Texture2D;
                }
            }

            return source.mainTexture as Texture2D;
        }

        static Color GetBaseColor(Material source)
        {
            // glTFast 写进去的时候已经是 gamma 值，本工程就是 Gamma 空间，直接用
            if (source.HasProperty(BaseColorFactorId))
            {
                return source.GetColor(BaseColorFactorId);
            }

            if (source.HasProperty(ColorId))
            {
                return source.GetColor(ColorId);
            }

            return Color.white;
        }

        static float GetCutoff(Material source)
        {
            if (source.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                // glTFast 的 shader 叫 alphaCutoff，Standard / Unlit 叫 _Cutoff
                if (source.HasProperty(GtlfCutoffId))
                {
                    return source.GetFloat(GtlfCutoffId);
                }

                if (source.HasProperty(CutoffId))
                {
                    return source.GetFloat(CutoffId);
                }

                return 0.5f;
            }

            if (source.IsKeywordEnabled("_ALPHABLEND_ON") || source.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
            {
                return 0.5f;
            }

            // 不透明：必须完全不裁，否则 alpha 整条是 0 的贴图会把模型裁没
            return 0f;
        }

        static bool GetCullBack(Material source)
        {
            if (source.HasProperty(GtlfCullId))
            {
                return source.GetFloat(GtlfCullId) > 0.5f;
            }

            if (source.HasProperty(CullId))
            {
                return source.GetFloat(CullId) > 0.5f;
            }

            return true;
        }

        static void MakePointSampled(Texture texture, ref int changed)
        {
            if (texture == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.filterMode == FilterMode.Point)
            {
                // 模型内嵌的贴图没有 TextureImporter，改不了，跳过
                return;
            }

            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            changed++;
        }

        static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder(MaterialsFolder, "Generated");
            }
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "mc";
            }

            StringBuilder builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }
    }
}
