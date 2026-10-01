using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using UnityEngine;

namespace MaidHome.Interop.Gltf
{
    /// <summary>
    /// 把 glTF 的 PBR 材质直接生成成 MC 风格材质：只取 baseColor 贴图和因子，
    /// 金属度 / 粗糙度 / 法线 / 遮蔽全部丢掉——这些正是让模型看起来写实、跟场景对不上的原因。
    /// 贴图强制点采样（wrapMode 保留 glTF 自己的设置）。
    /// </summary>
    public sealed class McGltfMaterialGenerator : IMaterialGenerator
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        static readonly int CullId = Shader.PropertyToID("_Cull");

        Material _defaultMaterial;
        ICodeLogger _logger;

        public McGltfMaterialGenerator()
        {
            if (Shader.Find("MaidHome/MinecraftBlock") == null)
            {
                Debug.LogError("找不到 MaidHome/MinecraftBlock，glTF 材质退回 Standard");
            }
        }

        public void SetLogger(ICodeLogger logger)
        {
            _logger = logger;
        }

        public Material GetDefaultMaterial(bool pointsSupport = false)
        {
            if (_defaultMaterial == null)
            {
                _defaultMaterial = CreateMaterial("gltf_mc_default", Color.white, null, 0f, true);
            }

            return _defaultMaterial;
        }

        public Material GenerateMaterial(GLTFast.Schema.Material gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
        {
            if (gltfMaterial == null)
            {
                return GetDefaultMaterial(pointsSupport);
            }

            GLTFast.Schema.PbrMetallicRoughness pbr = gltfMaterial.pbrMetallicRoughness;

            // baseColorFactor 是线性值，跟 glTFast 自己的材质生成一致：转成 gamma 再塞（工程是 Gamma 空间）
            Color baseColor = Color.white;
            if (pbr != null && pbr.baseColorFactor != null && pbr.baseColorFactor.Length >= 4)
            {
                baseColor = new Color(
                    pbr.baseColorFactor[0],
                    pbr.baseColorFactor[1],
                    pbr.baseColorFactor[2],
                    pbr.baseColorFactor[3]).gamma;
            }

            Texture2D texture = null;
            if (pbr != null && pbr.baseColorTexture != null && pbr.baseColorTexture.index >= 0)
            {
                texture = gltf.GetTexture(pbr.baseColorTexture.index);
                if (texture != null)
                {
                    texture.filterMode = FilterMode.Point;
                }
            }

            // OPAQUE 必须完全不裁：很多导出的贴图 alpha 通道全 0，按月牙裁会把整个模型裁没
            float cutoff = 0f;
            if (gltfMaterial.GetAlphaMode() == GLTFast.Schema.Material.AlphaMode.Mask)
            {
                cutoff = gltfMaterial.alphaCutoff > 0f ? gltfMaterial.alphaCutoff : 0.5f;
            }
            else if (gltfMaterial.GetAlphaMode() == GLTFast.Schema.Material.AlphaMode.Blend)
            {
                // 这个 MC 着色器没有混合模式，退化成裁切
                cutoff = 0.5f;
                if (_logger != null)
                {
                    _logger.Warning("alphaMode=BLEND 按裁切处理: " + gltfMaterial.name);
                }
            }

            return CreateMaterial(gltfMaterial.name, baseColor, texture, cutoff, !gltfMaterial.doubleSided);
        }

        /// <summary>
        /// 只设点采样，不动 wrapMode：glTF 的 sampler 说 REPEAT 就得是 REPEAT，
        /// 方块贴图靠它平铺，强制成 Clamp 会把平铺弄坏（女仆图集才是 Clamp）。
        /// </summary>
        public static Material CreateMaterial(string name, Color color, Texture2D texture, float cutoff, bool cullBack)
        {
            Shader shader = Shader.Find("MaidHome/MinecraftBlock");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = string.IsNullOrEmpty(name) ? "gltf_mc" : name;
            material.SetColor(ColorId, color);
            material.SetFloat(CutoffId, cutoff);
            material.SetFloat(CullId, cullBack ? 2f : 0f);
            if (texture != null)
            {
                material.mainTexture = texture;
            }

            return material;
        }
    }
}
