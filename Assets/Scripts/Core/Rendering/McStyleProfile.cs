using System;
using UnityEngine;

namespace MaidHome.Core.Rendering
{
    /// <summary>
    /// MC 风格渲染的一组参数。都是 Gamma 空间下的直出颜色，不做色彩管理。
    /// 默认值对着原版白天调：天顶 #78A7FF、地平线 #C0D8FF。
    /// </summary>
    [Serializable]
    public sealed class McStyleProfile
    {
        [Header("天空盒")]
        public Color SkyTopColor = new Color(0.47f, 0.65f, 1f, 1f);
        public Color SkyHorizonColor = new Color(0.75f, 0.85f, 1f, 1f);
        public Color SkyGroundColor = new Color(0.75f, 0.85f, 1f, 1f);
        public Color SunDiscColor = new Color(1f, 0.97f, 0.90f, 1f);
        [Range(0.0005f, 0.2f)] public float SunSize = 0.02f;
        [Range(0f, 1f)] public float SunGlow = 0.4f;
        [Range(0.5f, 8f)] public float SkyExponent = 1.5f;

        [Header("雾")]
        public Color FogColor = new Color(0.75f, 0.85f, 1f, 1f);
        public float FogStartDistance = 60f;
        public float FogEndDistance = 220f;

        [Header("环境光（Trilight）")]
        public Color AmbientSkyColor = new Color(0.66f, 0.78f, 0.94f, 1f);
        public Color AmbientEquatorColor = new Color(0.55f, 0.58f, 0.60f, 1f);
        public Color AmbientGroundColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        [Range(0f, 2f)] public float AmbientIntensity = 1f;

        [Header("太阳")]
        public Color SunColor = new Color(1f, 0.96f, 0.88f, 1f);
        [Range(0f, 2f)] public float SunIntensity = 1f;
        public Vector3 SunEulerAngles = new Vector3(50f, -30f, 0f);
        public LightShadows SunShadows = LightShadows.Hard;
    }
}
