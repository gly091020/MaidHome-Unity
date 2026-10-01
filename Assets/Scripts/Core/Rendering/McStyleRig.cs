using UnityEngine;
using UnityEngine.Rendering;

namespace MaidHome.Core.Rendering
{
    /// <summary>
    /// 把场景调成 MC 风格：渐变天空盒 + 线性雾 + 三色环境光 + 关掉反射和光晕。
    /// 相机不归它管（正交、HDR、MSAA 由相机自己定），只保证渲染环境和相机无关。
    /// 挂在任意物体上，[ExecuteAlways] 让编辑器里也能直接看到效果。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class McStyleRig : MonoBehaviour
    {
        [SerializeField] private McStyleProfile _profile = new McStyleProfile();
        [SerializeField] private Material _skyboxMaterial;
        [SerializeField] private Light _sun;
        [SerializeField] private bool _applyOnEnable = true;

        public McStyleProfile Profile
        {
            get { return _profile; }
        }

        void OnEnable()
        {
            if (_applyOnEnable)
            {
                Apply();
            }
        }

        void OnValidate()
        {
            if (_applyOnEnable && isActiveAndEnabled)
            {
                Apply();
            }
        }

        // 太阳被别的脚本转了以后，天空盒里的太阳圆盘要跟上
        void Update()
        {
            if (_skyboxMaterial == null)
            {
                return;
            }

            Light sun = ResolveSun();
            if (sun != null)
            {
                _skyboxMaterial.SetVector("_SunDirection", SunDirection(sun));
            }
        }

        /// <summary>把 profile 写进 RenderSettings、天空盒材质和太阳。</summary>
        public void Apply()
        {
            if (_profile == null)
            {
                _profile = new McStyleProfile();
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = _profile.FogColor;
            RenderSettings.fogStartDistance = _profile.FogStartDistance;
            RenderSettings.fogEndDistance = _profile.FogEndDistance;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _profile.AmbientSkyColor;
            RenderSettings.ambientEquatorColor = _profile.AmbientEquatorColor;
            RenderSettings.ambientGroundColor = _profile.AmbientGroundColor;
            RenderSettings.ambientIntensity = _profile.AmbientIntensity;

            // 环境反射和光晕是"写实感"的主要来源，MC 风格不需要
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.haloStrength = 0f;
            RenderSettings.flareStrength = 0f;

            Light sun = ResolveSun();
            if (sun != null)
            {
                sun.color = _profile.SunColor;
                sun.intensity = _profile.SunIntensity;
                sun.shadows = _profile.SunShadows;
                sun.transform.rotation = Quaternion.Euler(_profile.SunEulerAngles);
                RenderSettings.sun = sun;
            }

            if (_skyboxMaterial == null)
            {
                return;
            }

            _skyboxMaterial.SetColor("_TopColor", _profile.SkyTopColor);
            _skyboxMaterial.SetColor("_HorizonColor", _profile.SkyHorizonColor);
            _skyboxMaterial.SetColor("_GroundColor", _profile.SkyGroundColor);
            _skyboxMaterial.SetColor("_SunColor", _profile.SunDiscColor);
            _skyboxMaterial.SetFloat("_SunSize", _profile.SunSize);
            _skyboxMaterial.SetFloat("_SunGlow", _profile.SunGlow);
            _skyboxMaterial.SetFloat("_Exponent", _profile.SkyExponent);
            _skyboxMaterial.SetVector("_SunDirection", SunDirection(sun));
            RenderSettings.skybox = _skyboxMaterial;
        }

        Light ResolveSun()
        {
            if (_sun != null)
            {
                return _sun;
            }

            _sun = RenderSettings.sun;
            if (_sun != null)
            {
                return _sun;
            }

            Light[] lights = FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    _sun = lights[i];
                    break;
                }
            }

            return _sun;
        }

        static Vector4 SunDirection(Light sun)
        {
            if (sun == null)
            {
                return new Vector4(0.3f, 0.8f, 0.5f, 0f);
            }

            // 平行光的 forward 是光照方向，太阳在它的反方向
            Vector3 direction = -sun.transform.forward;
            return new Vector4(direction.x, direction.y, direction.z, 0f);
        }
    }
}
