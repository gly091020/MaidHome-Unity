// 原版天空渐变保持不变；太阳、月亮和星星作为独立的透明贴图叠加到天空上。
// 地平线颜色必须和 RenderSettings.fogColor 一致，否则远处地形和天空接不上。
Shader "MaidHome/MinecraftSkybox"
{
    Properties
    {
        _TopColor ("天顶颜色", Color) = (0.47, 0.65, 1, 1)
        _HorizonColor ("地平线颜色", Color) = (0.75, 0.85, 1, 1)
        _GroundColor ("地平线以下颜色", Color) = (0.75, 0.85, 1, 1)
        _SunTex ("太阳贴图", 2D) = "white" {}
        _SunColor ("太阳颜色", Color) = (1, 1, 1, 1)
        _SunDirection ("太阳方向（世界空间，指向太阳）", Vector) = (0.3, 0.8, 0.5, 0)
        _SunSize ("太阳大小", Range(0.0005, 0.2)) = 0.02
        [HideInInspector] _SunGlow ("太阳光晕（旧属性兼容）", Float) = 0
        _MoonTex ("月亮贴图", 2D) = "white" {}
        _MoonDirection ("月亮方向（世界空间，指向月亮）", Vector) = (-0.3, -0.8, -0.5, 0)
        _MoonColor ("月亮颜色", Color) = (1, 1, 1, 1)
        _MoonPhase ("月相 0~7", Range(0, 7)) = 0
        _MoonSpriteSize ("月亮贴图大小", Range(0.005, 0.2)) = 0.15
        _StarColor ("星星颜色", Color) = (1, 1, 1, 1)
        _CelestialAngle ("星空旋转角（弧度）", Float) = 0
        _NightAmount ("夜间程度", Range(0, 1)) = 0
        _StarAmount ("星星亮度", Range(0, 1)) = 0
        [HideInInspector] _MoonSize ("月亮大小（旧属性兼容）", Float) = 0
        [HideInInspector] _MoonGlow ("月亮光晕（旧属性兼容）", Float) = 0
        _Exponent ("渐变指数", Range(0.5, 8)) = 1.5
        _OrthoSpread ("正交相机天空展开", Range(0.3, 3)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _SunTex;
            sampler2D _MoonTex;
            float4 _TopColor;
            float4 _HorizonColor;
            float4 _GroundColor;
            float4 _SunColor;
            float4 _MoonColor;
            float4 _StarColor;
            float4 _SunDirection;
            float4 _MoonDirection;
            float _SunSize;
            float _MoonPhase;
            float _MoonSpriteSize;
            float _CelestialAngle;
            float _NightAmount;
            float _StarAmount;
            float _Exponent;
            float _OrthoSpread;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                float4 screen : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float3 celestial_up(float3 axis)
            {
                return abs(axis.y) > 0.99
                    ? float3(0.0, 0.0, 1.0)
                    : float3(0.0, 1.0, 0.0);
            }

            float2 celestial_uv(float3 dir, float3 axis, float halfSize, out float inside)
            {
                float facing = saturate(dot(dir, axis));
                float3 up = celestial_up(axis);
                float3 right = normalize(cross(up, axis));
                float3 tangent = normalize(cross(axis, right));
                float2 offset = float2(dot(dir, right), dot(dir, tangent))
                    / max(facing, 0.0001);
                float2 uv = offset / max(halfSize * 2.0, 0.0001) + 0.5;
                inside = step(0.0, uv.x) * step(uv.x, 1.0)
                       * step(0.0, uv.y) * step(uv.y, 1.0)
                       * step(0.0001, facing);
                return uv;
            }

            float star_hash(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float4 frag (v2f i) : SV_Target
            {
                // 正交相机所有视线平行，"按视线方向取色"本来是没有意义的：Unity 依然把天空盒
                // 当盒子画，盒子的顶点方向在屏幕上从上到下穿过正负值，dir.y=0 那条线正好落在屏幕
                // 正中，看起来就是天空只有一半蓝。这里在正交下用相机基向量 + 屏幕坐标造一个
                // 假的透视方向，渐变和日月都正常，转相机时天空也会跟着动。
                float3 dir;
                if (unity_OrthoParams.w > 0.5)
                {
                    float2 uv = i.screen.xy / i.screen.w;
                    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float3 camRight = unity_CameraToWorld._m00_m10_m20;
                    float3 camUp = unity_CameraToWorld._m01_m11_m21;
                    float3 camForward = unity_CameraToWorld._m02_m12_m22;
                    dir = normalize(camForward
                                  + camRight * ((uv.x * 2.0 - 1.0) * aspect * _OrthoSpread)
                                  + camUp * ((uv.y * 2.0 - 1.0) * _OrthoSpread));
                }
                else
                {
                    dir = normalize(i.dir);
                }

                // 这段计算保持旧 shader 原样，贴图只往后叠加。
                float up = dir.y;
                float t = pow(saturate(up), _Exponent);
                float3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, t);
                float3 below = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-up * 3.0));
                float3 col = lerp(below, sky, step(0.0, up));

                float3 sunDir = normalize(_SunDirection.xyz);
                float sunVisible = smoothstep(-0.03, 0.05, sunDir.y);

                // 旧圆盘的边缘角度换算成贴图半宽，尺寸与旧版太阳保持一致。
                float sunCos = max(1.0 - _SunSize, 0.001);
                float sunHalfSize = sqrt(max(1.0 - sunCos * sunCos, 0.0)) / sunCos;
                float sunInside;
                float2 sunUv = celestial_uv(dir, sunDir, sunHalfSize, sunInside);
                float4 sunSprite = tex2D(_SunTex, sunUv);
                float sunAmount = sunSprite.a * sunInside * sunVisible;
                col = lerp(col, sunSprite.rgb, sunAmount);

                float3 moonDir = normalize(_MoonDirection.xyz);
                float moonVisible = smoothstep(-0.03, 0.05, moonDir.y);
                float moonInside;
                float2 moonLocalUv = celestial_uv(dir, moonDir, _MoonSpriteSize, moonInside);
                float moonPhase = clamp(_MoonPhase, 0.0, 7.0);
                float moonRow = step(3.5, moonPhase);
                float moonCol = moonPhase - moonRow * 4.0;
                float2 moonUv = float2((moonCol + moonLocalUv.x) * 0.25,
                    (1.0 - moonRow + moonLocalUv.y) * 0.5);
                float4 moonSprite = tex2D(_MoonTex, moonUv);
                float moonAmount = moonSprite.a * moonInside * moonVisible;
                col = lerp(col, moonSprite.rgb * _MoonColor.rgb, moonAmount);

                // 星星是固定分布的像素方点；用立方体六面各自成格，避免方向格被拉成菱形。
                float ca = cos(_CelestialAngle);
                float sa = sin(_CelestialAngle);
                float3 starDir = float3(ca * dir.x + sa * dir.y,
                    -sa * dir.x + ca * dir.y, dir.z);
                float3 absStarDir = abs(starDir);
                float2 starFaceUv;
                float starFace;
                if (absStarDir.x >= absStarDir.y && absStarDir.x >= absStarDir.z)
                {
                    starFaceUv = starDir.yz / max(absStarDir.x, 0.0001);
                    starFace = starDir.x >= 0.0 ? 0.0 : 1.0;
                }
                else if (absStarDir.y >= absStarDir.z)
                {
                    starFaceUv = starDir.xz / max(absStarDir.y, 0.0001);
                    starFace = starDir.y >= 0.0 ? 2.0 : 3.0;
                }
                else
                {
                    starFaceUv = starDir.xy / max(absStarDir.z, 0.0001);
                    starFace = starDir.z >= 0.0 ? 4.0 : 5.0;
                }

                float2 starGrid = (starFaceUv * 0.5 + 0.5) * 384.0;
                float2 starCell = starGrid - frac(starGrid);
                float2 starLocal = frac(starGrid);
                float3 starSeed = float3(starFace * 31.0 + starCell.x, starCell.y, 7.0);
                float star = step(0.9983, star_hash(starSeed));
                float starSquare = step(abs(starLocal.x - 0.5), 0.17)
                                 * step(abs(starLocal.y - 0.5), 0.17);
                float starBrightness = lerp(0.62, 1.0, star_hash(starSeed + 19.0));
                float starsVisible = smoothstep(0.02, 0.22, up)
                                   * _NightAmount * _StarAmount;
                col += _StarColor.rgb * star * starSquare * starBrightness * starsVisible;

                return float4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
