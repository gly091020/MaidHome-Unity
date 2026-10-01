// 原版天空的走法：不是贴图天空球，而是一段竖直渐变 + 一个太阳圆盘。
// 地平线颜色必须和 RenderSettings.fogColor 一致，否则远处地形和天空接不上。
Shader "MaidHome/MinecraftSkybox"
{
    Properties
    {
        _TopColor ("天顶颜色", Color) = (0.47, 0.65, 1, 1)
        _HorizonColor ("地平线颜色", Color) = (0.75, 0.85, 1, 1)
        _GroundColor ("地平线以下颜色", Color) = (0.75, 0.85, 1, 1)
        _SunColor ("太阳颜色", Color) = (1, 0.97, 0.90, 1)
        _SunDirection ("太阳方向（世界空间，指向太阳）", Vector) = (0.3, 0.8, 0.5, 0)
        _SunSize ("太阳大小", Range(0.0005, 0.2)) = 0.02
        _SunGlow ("太阳光晕", Range(0, 1)) = 0.4
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

            fixed4 _TopColor;
            fixed4 _HorizonColor;
            fixed4 _GroundColor;
            fixed4 _SunColor;
            float4 _SunDirection;
            half _SunSize;
            half _SunGlow;
            half _Exponent;
            half _OrthoSpread;

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

            fixed4 frag (v2f i) : SV_Target
            {
                // 正交相机所有视线平行，"按视线方向取色"本来是没有意义的：Unity 依然把天空盒
                // 当盒子画，盒子的顶点方向在屏幕上从上到下穿过正负值，dir.y=0 那条线正好落在屏幕
                // 正中，看起来就是天空只有一半蓝。这里在正交下用相机基向量 + 屏幕坐标造一个
                // 假的透视方向，渐变和太阳圆盘就都正常了，转相机时天空也会跟着动。
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

                half up = dir.y;

                // 地平线最亮，往天顶逐渐变深；地平线以下渐到地面色
                half t = pow(saturate(up), _Exponent);
                fixed3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, t);
                fixed3 below = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-up * 3.0));
                fixed3 col = lerp(below, sky, step(0.0, up));

                // 太阳：实心圆盘 + 外圈光晕，都在天空一侧
                float3 sunDir = normalize(_SunDirection.xyz);
                half d = (half)dot(dir, sunDir);
                half disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, d);
                half glow = pow(saturate(d), 24.0) * _SunGlow;
                col += _SunColor.rgb * max(disc, glow * (1.0 - disc));

                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
