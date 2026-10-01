// 方块/模型用的 MC 风格着色器：AlphaTest 裁切 + 原版逐面亮度 + 线性雾。
// 不用 Standard，是因为 Standard 的环境反射和镜面高光会让表面出现写实的明暗渐变。
// 只吃一盏平行光（ForwardBase），刚好对应场景里的太阳。
Shader "MaidHome/MinecraftBlock"
{
    Properties
    {
        _MainTex ("贴图", 2D) = "white" {}
        _Color ("乘色", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha 剔除", Range(0, 1)) = 0.5
        _AmbientBoost ("环境光强度", Range(0, 2)) = 1
        _SunPower ("阳光强度", Range(0, 2)) = 0.85
        _SunAngleInfluence ("太阳方向影响", Range(0, 1)) = 0.3
        _TintColor ("染色覆盖颜色", Color) = (1, 1, 1, 1)
        _TintStrength ("染色覆盖强度", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除模式", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 100
        Cull [_Cull]

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _Cutoff;
            half _AmbientBoost;
            half _SunPower;
            half _SunAngleInfluence;
            fixed4 _TintColor;
            half _TintStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                fixed3 ambient : TEXCOORD2;
                fixed2 shade : TEXCOORD3;
                LIGHTING_COORDS(4, 5)
                UNITY_FOG_COORDS(6)
                fixed3 vcolor : TEXCOORD7;
            };

            // 原版那套逐面亮度：顶 1.0、南北 0.8、东西 0.6、底 0.5。
            // 用模型空间法线算，骨骼转动手臂/裙摆时明暗不会跟着跳（原版就是这么做的）。
            half FaceShade (half3 n)
            {
                half3 a = abs(n);
                half shade = 0.5;
                shade = n.y > 0.5 ? 1.0 : shade;
                shade = (a.z >= a.x && a.z >= a.y) ? 0.8 : shade;
                shade = (a.x > a.z && a.x >= a.y) ? 0.6 : shade;
                return shade;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                half3 worldNormal = UnityObjectToWorldNormal(v.normal);
                o.ambient = ShadeSH9(half4(worldNormal, 1));
                o.shade.x = FaceShade(v.normal);
                o.shade.y = saturate(dot(normalize(worldNormal), normalize(_WorldSpaceLightPos0.xyz)));
                // 草方块这类原版靠生物群系染色的贴图是灰度的，染色被烘进了顶点色。
                // 实测导出器写的是 sRGB 值（例：0.745,0.716,0.332 = #BFB755 草原草色），
                // 所以这里直接用，不做 LinearToGamma；换成规范写线性的模型再补转换。
                // 强度拉到 1 就用 _TintColor 顶掉顶点色，方便直接指定草的颜色。
                o.vcolor = lerp(v.color.rgb, _TintColor.rgb, _TintStrength);
                TRANSFER_VERTEX_TO_FRAGMENT(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff);

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

                // 原版一个方块只有一个光照等级，跟太阳方位无关，方向感全部来自逐面亮度；
                // 太阳只负责整体亮度和阴影。_SunAngleInfluence 用来加一点方向感，0 就是纯原版。
                half directionFactor = lerp(1.0, i.shade.y, _SunAngleInfluence);
                half3 light = i.ambient * _AmbientBoost
                            + _LightColor0.rgb * (_SunPower * atten * directionFactor);
                light = min(light, 1.0);

                fixed3 col = c.rgb * i.vcolor * i.shade.x * light;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return fixed4(col, c.a);
            }
            ENDCG
        }

        // 阴影投射：不写这个的话物体受阴影但自己不投影
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }
    }

    Fallback Off
}
