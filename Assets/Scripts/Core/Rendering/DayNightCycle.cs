using System;
using MaidHome.Core.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace MaidHome.Core.Rendering
{
    /// <summary>
    /// 常驻昼夜时钟。MC 模式把 UTC 流逝折算成 24000 tick，现实模式把本地时钟映射到
    /// 06:00 日出、12:00 正午、18:00 日落、00:00 午夜；两种模式都会在退出游戏后继续计算。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightCycle : MonoBehaviour
    {
        const float RefreshSeconds = 0.1f;
        static DayNightCycle _instance;

        GameSettings _settings;
        DayNightMode _lastMode;
        double _currentTick;
        float _nextRefreshAt;
        bool _bound;

        Light _sun;
        Material _sourceSkybox;
        Material _skybox;
        bool _ownsSkybox;
        McStyleRig _rig;
        McStyleProfile _baseProfile;
        McStyleProfile _workingProfile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null || FindObjectOfType<DayNightCycle>() != null)
            {
                return;
            }

            GameObject root = new GameObject("DayNightCycle");
            DontDestroyOnLoad(root);
            root.AddComponent<DayNightCycle>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeClock();
            GameSettingsStore.Changed += OnSettingsChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            BindScene();
            ApplyNow();
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            GameSettingsStore.Changed -= OnSettingsChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnbindScene();
        }

        void Update()
        {
            if (!_bound || Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }

            ApplyNow();
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                ApplyNow();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                ApplyNow();
            }
        }

        void OnSceneLoaded(UnityScene scene, LoadSceneMode mode)
        {
            BindScene();
            ApplyNow();
        }

        void InitializeClock()
        {
            _settings = GameSettingsStore.Current;
            _lastMode = _settings.DayNight;
            bool newAnchor = _settings.DayNightAnchorUtcTicks <= 0
                || float.IsNaN(_settings.DayNightAnchorTick)
                || float.IsInfinity(_settings.DayNightAnchorTick);
            if (newAnchor)
            {
                _settings.DayNightAnchorUtcTicks = DateTime.UtcNow.Ticks;
                _settings.DayNightAnchorDay = 0L;
                _settings.DayNightAnchorTick = 0f;
                GameSettingsStore.Save();
            }

            _currentTick = ResolveTick(DateTime.UtcNow);
        }

        void OnSettingsChanged()
        {
            GameSettings settings = GameSettingsStore.Current;
            bool modeChanged = settings.DayNight != _lastMode;
            if (modeChanged)
            {
                DateTime now = DateTime.UtcNow;
                settings.DayNightAnchorDay = ResolveDay(_lastMode, now);
                settings.DayNightAnchorUtcTicks = now.Ticks;
                settings.DayNightAnchorTick = (float)MinecraftDayNight.Normalize(_currentTick);
                _lastMode = settings.DayNight;
                GameSettingsStore.Save();
            }

            _settings = settings;
            _lastMode = settings.DayNight;
            ApplyNow();
        }

        double ResolveTick(DateTime utcNow)
        {
            if (_settings.DayNight == DayNightMode.RealTime)
            {
                return MinecraftDayNight.FromLocalTime(DateTime.Now);
            }

            return MinecraftDayNight.FromAnchor(_settings.DayNightAnchorUtcTicks,
                _settings.DayNightAnchorTick, utcNow);
        }

        void BindScene()
        {
            UnbindScene();

            // Portal 和 AR 不用房屋的 MC 天空；只有已经开启线性雾的玩法场景才接管。
            if (!RenderSettings.fog || RenderSettings.skybox == null)
            {
                return;
            }

            _sun = ResolveSun();
            if (_sun == null)
            {
                return;
            }

            _sourceSkybox = RenderSettings.skybox;
            _baseProfile = CaptureProfile(_sourceSkybox, _sun);
            _workingProfile = CloneProfile(_baseProfile);
            _rig = FindObjectOfType<McStyleRig>();

            if (_rig != null)
            {
                _baseProfile = CloneProfile(_rig.Profile);
                _workingProfile = _rig.Profile;
                _skybox = _sourceSkybox;
                _ownsSkybox = false;
            }
            else
            {
                _skybox = new Material(_sourceSkybox);
                _skybox.name = _sourceSkybox.name + " (Runtime Day/Night)";
                RenderSettings.skybox = _skybox;
                _ownsSkybox = true;
            }

            _bound = true;
        }

        void UnbindScene()
        {
            if (_ownsSkybox && _skybox != null)
            {
                if (RenderSettings.skybox == _skybox)
                {
                    RenderSettings.skybox = _sourceSkybox;
                }

                Destroy(_skybox);
            }

            _bound = false;
            _sun = null;
            _sourceSkybox = null;
            _skybox = null;
            _rig = null;
            _baseProfile = null;
            _workingProfile = null;
            _ownsSkybox = false;
        }

        void ApplyNow()
        {
            if (!_bound)
            {
                return;
            }

            _nextRefreshAt = Time.unscaledTime + RefreshSeconds;
            DateTime utcNow = DateTime.UtcNow;
            _currentTick = ResolveTick(utcNow);

            McStyleProfile target = _rig != null ? _rig.Profile : _workingProfile;
            Vector3 sunDirection;
            float nightAmount;
            MinecraftDayNight.Apply(_baseProfile, target, _currentTick,
                out sunDirection, out nightAmount);

            Vector3 celestialLight = sunDirection.y >= 0f ? sunDirection : -sunDirection;
            Quaternion lightRotation = Quaternion.LookRotation(-celestialLight, Vector3.up);
            if (_rig != null)
            {
                target.SunEulerAngles = lightRotation.eulerAngles;
                _rig.Apply();
                _skybox = RenderSettings.skybox;
            }
            else
            {
                ApplyEnvironment(target);
                _sun.transform.rotation = lightRotation;
            }

            ApplyNightSky(sunDirection, nightAmount, utcNow);
        }

        void ApplyEnvironment(McStyleProfile profile)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = profile.FogColor;
            RenderSettings.fogStartDistance = profile.FogStartDistance;
            RenderSettings.fogEndDistance = profile.FogEndDistance;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = profile.AmbientSkyColor;
            RenderSettings.ambientEquatorColor = profile.AmbientEquatorColor;
            RenderSettings.ambientGroundColor = profile.AmbientGroundColor;
            RenderSettings.ambientIntensity = profile.AmbientIntensity;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.haloStrength = 0f;
            RenderSettings.flareStrength = 0f;

            _sun.color = profile.SunColor;
            _sun.intensity = profile.SunIntensity;
            _sun.shadows = profile.SunShadows;
            RenderSettings.sun = _sun;

            _skybox.SetColor("_TopColor", profile.SkyTopColor);
            _skybox.SetColor("_HorizonColor", profile.SkyHorizonColor);
            _skybox.SetColor("_GroundColor", profile.SkyGroundColor);
            _skybox.SetColor("_SunColor", profile.SunDiscColor);
            _skybox.SetFloat("_SunSize", profile.SunSize);
            _skybox.SetFloat("_SunGlow", profile.SunGlow);
            _skybox.SetFloat("_Exponent", profile.SkyExponent);
            RenderSettings.skybox = _skybox;
        }

        void ApplyNightSky(Vector3 sunDirection, float nightAmount, DateTime utcNow)
        {
            if (_skybox == null)
            {
                return;
            }

            Vector3 sun = sunDirection.normalized;
            Vector3 moon = -sun;
            _skybox.SetVector("_SunDirection", new Vector4(sun.x, sun.y, sun.z, 0f));
            _skybox.SetVector("_MoonDirection", new Vector4(moon.x, moon.y, moon.z, 0f));
            _skybox.SetColor("_MoonColor", new Color(0.82f, 0.88f, 1f, 1f));
            _skybox.SetFloat("_MoonSize", 0.105f);
            _skybox.SetFloat("_MoonGlow", 0f);
            _skybox.SetFloat("_MoonPhase", ResolveMoonPhase(utcNow));
            _skybox.SetFloat("_CelestialAngle", (float)(_currentTick
                / MinecraftDayNight.TicksPerDay * Math.PI * 2d));
            _skybox.SetFloat("_NightAmount", nightAmount);
            _skybox.SetFloat("_StarAmount", Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(-0.08f, -0.30f, sun.y)));
        }

        int ResolveMoonPhase(DateTime utcNow)
        {
            long day = _settings.DayNight == DayNightMode.RealTime
                ? LocalDay()
                : MinecraftDayNight.CompleteDaysFromAnchor(_settings.DayNightAnchorDay,
                    _settings.DayNightAnchorUtcTicks, _settings.DayNightAnchorTick, utcNow);
            return MinecraftDayNight.MoonPhase(day);
        }

        long ResolveDay(DayNightMode mode, DateTime utcNow)
        {
            return mode == DayNightMode.RealTime
                ? LocalDay()
                : MinecraftDayNight.CompleteDaysFromAnchor(_settings.DayNightAnchorDay,
                    _settings.DayNightAnchorUtcTicks, _settings.DayNightAnchorTick, utcNow);
        }

        static long LocalDay()
        {
            return DateTime.Now.Date.Ticks / TimeSpan.TicksPerDay;
        }

        Light ResolveSun()
        {
            if (RenderSettings.sun != null)
            {
                return RenderSettings.sun;
            }

            Light[] lights = FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }

            return null;
        }

        static McStyleProfile CaptureProfile(Material skybox, Light sun)
        {
            McStyleProfile profile = new McStyleProfile();
            profile.SkyTopColor = GetColor(skybox, "_TopColor", profile.SkyTopColor);
            profile.SkyHorizonColor = GetColor(skybox, "_HorizonColor", profile.SkyHorizonColor);
            profile.SkyGroundColor = GetColor(skybox, "_GroundColor", profile.SkyGroundColor);
            profile.SunDiscColor = GetColor(skybox, "_SunColor", profile.SunDiscColor);
            profile.SunSize = GetFloat(skybox, "_SunSize", profile.SunSize);
            profile.SunGlow = GetFloat(skybox, "_SunGlow", profile.SunGlow);
            profile.SkyExponent = GetFloat(skybox, "_Exponent", profile.SkyExponent);

            profile.FogColor = RenderSettings.fogColor;
            profile.FogStartDistance = RenderSettings.fogStartDistance;
            profile.FogEndDistance = RenderSettings.fogEndDistance;
            profile.AmbientSkyColor = RenderSettings.ambientSkyColor;
            profile.AmbientEquatorColor = RenderSettings.ambientEquatorColor;
            profile.AmbientGroundColor = RenderSettings.ambientGroundColor;
            profile.AmbientIntensity = RenderSettings.ambientIntensity;
            profile.SunColor = sun.color;
            profile.SunIntensity = sun.intensity;
            profile.SunEulerAngles = sun.transform.eulerAngles;
            profile.SunShadows = sun.shadows;
            return profile;
        }

        static McStyleProfile CloneProfile(McStyleProfile source)
        {
            if (source == null)
            {
                return new McStyleProfile();
            }

            return new McStyleProfile
            {
                SkyTopColor = source.SkyTopColor,
                SkyHorizonColor = source.SkyHorizonColor,
                SkyGroundColor = source.SkyGroundColor,
                SunDiscColor = source.SunDiscColor,
                SunSize = source.SunSize,
                SunGlow = source.SunGlow,
                SkyExponent = source.SkyExponent,
                FogColor = source.FogColor,
                FogStartDistance = source.FogStartDistance,
                FogEndDistance = source.FogEndDistance,
                AmbientSkyColor = source.AmbientSkyColor,
                AmbientEquatorColor = source.AmbientEquatorColor,
                AmbientGroundColor = source.AmbientGroundColor,
                AmbientIntensity = source.AmbientIntensity,
                SunColor = source.SunColor,
                SunIntensity = source.SunIntensity,
                SunEulerAngles = source.SunEulerAngles,
                SunShadows = source.SunShadows,
            };
        }

        static Color GetColor(Material material, string property, Color fallback)
        {
            return material != null && material.HasProperty(property)
                ? material.GetColor(property)
                : fallback;
        }

        static float GetFloat(Material material, string property, float fallback)
        {
            return material != null && material.HasProperty(property)
                ? material.GetFloat(property)
                : fallback;
        }
    }
}
