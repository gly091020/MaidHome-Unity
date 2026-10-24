using System;
using MaidHome.Core.Settings;
using UnityEngine;

namespace MaidHome.Core.Rendering
{
    /// <summary>Minecraft 昼夜时钟与天空/环境光的颜色映射。</summary>
    public static class MinecraftDayNight
    {
        public const double TicksPerDay = 24000d;
        public const double TicksPerSecond = 20d;

        const double SunriseSeconds = 6d * 60d * 60d;

        static readonly Color NightSky = Color.black;
        static readonly Color NightAmbientSky = new Color(0.22f, 0.28f, 0.48f, 1f);
        static readonly Color NightAmbientEquator = new Color(0.11f, 0.14f, 0.25f, 1f);
        static readonly Color NightAmbientGround = new Color(0.045f, 0.055f, 0.10f, 1f);
        static readonly Color MoonLight = new Color(0.46f, 0.56f, 0.84f, 1f);
        static readonly Color TwilightSun = new Color(1f, 0.55f, 0.26f, 1f);

        public static double Normalize(double ticks)
        {
            double normalized = ticks % TicksPerDay;
            return normalized < 0d ? normalized + TicksPerDay : normalized;
        }

        public static double FromAnchor(long anchorUtcTicks, double anchorMinecraftTicks, DateTime utcNow)
        {
            double seconds = (utcNow.Ticks - anchorUtcTicks) / (double)TimeSpan.TicksPerSecond;
            return Normalize(anchorMinecraftTicks + seconds * TicksPerSecond);
        }

        public static long CompleteDaysFromAnchor(long anchorDay, long anchorUtcTicks,
            double anchorMinecraftTicks,
            DateTime utcNow)
        {
            double seconds = (utcNow.Ticks - anchorUtcTicks) / (double)TimeSpan.TicksPerSecond;
            double totalTicks = anchorMinecraftTicks + seconds * TicksPerSecond;
            return anchorDay + (long)Math.Floor(totalTicks / TicksPerDay);
        }

        public static double FromLocalTime(DateTime localNow)
        {
            double seconds = localNow.TimeOfDay.TotalSeconds;
            return Normalize((seconds - SunriseSeconds) * TicksPerDay / 86400d);
        }

        public static int MoonPhase(long dayIndex)
        {
            int phase = (int)(dayIndex % 8L);
            return phase < 0 ? phase + 8 : phase;
        }

        public static void Apply(McStyleProfile baseProfile, McStyleProfile target, double ticks,
            out Vector3 sunDirection, out float nightAmount)
        {
            if (baseProfile == null)
            {
                baseProfile = new McStyleProfile();
            }

            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            double angle = Normalize(ticks) / TicksPerDay * Math.PI * 2d;
            sunDirection = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0.22f).normalized;

            float daylight = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(-0.20f, 0.18f, sunDirection.y));
            float twilight = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.05f, 0.34f, Mathf.Abs(sunDirection.y)));
            nightAmount = 1f - daylight;

            // 天空只做原蓝色到纯黑的明暗过渡，避免暮光暖色把白天染黄。
            target.SkyTopColor = Color.Lerp(NightSky, baseProfile.SkyTopColor, daylight);
            target.SkyHorizonColor = Color.Lerp(NightSky, baseProfile.SkyHorizonColor, daylight);
            target.SkyGroundColor = Color.Lerp(NightSky, baseProfile.SkyGroundColor, daylight);

            target.FogColor = target.SkyHorizonColor;
            target.FogStartDistance = baseProfile.FogStartDistance;
            target.FogEndDistance = baseProfile.FogEndDistance;

            target.AmbientSkyColor = Color.Lerp(NightAmbientSky, baseProfile.AmbientSkyColor, daylight);
            target.AmbientEquatorColor = Color.Lerp(NightAmbientEquator,
                baseProfile.AmbientEquatorColor, daylight);
            target.AmbientGroundColor = Color.Lerp(NightAmbientGround,
                baseProfile.AmbientGroundColor, daylight);
            target.AmbientIntensity = baseProfile.AmbientIntensity * Mathf.Lerp(0.34f, 1f, daylight);

            target.SunColor = Color.Lerp(MoonLight, baseProfile.SunColor, daylight);
            target.SunIntensity = baseProfile.SunIntensity * Mathf.Lerp(0.16f, 1f, daylight);
            target.SunDiscColor = Color.Lerp(TwilightSun, baseProfile.SunDiscColor, twilight);
            target.SunGlow = baseProfile.SunGlow * Mathf.Lerp(0.28f, 1f, daylight);
            target.SunSize = baseProfile.SunSize;
            target.SkyExponent = baseProfile.SkyExponent;
            target.SunShadows = baseProfile.SunShadows;
        }
    }
}
