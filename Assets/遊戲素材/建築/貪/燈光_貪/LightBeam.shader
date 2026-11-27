// 假體積光柱（Additive）：彩繪玻璃透進來的聖光
// - 邊緣用 Fresnel 柔化，看不到網格邊
// - 沿光柱長度做淡入淡出（uv.y：0=窗邊、1=地面）
// - 沿長度顏色漸層：窗邊帶彩繪玻璃色（_Color）→ 往下散成白光（_ColorBottom）
// - 與場景深度相交處柔化（需 URP Depth Texture，PC_RPAsset 已開）
// - 鏡頭靠近/穿過光柱時自動淡出，避免整片畫面變亮
Shader "PilgrimOfSin/LightBeam"
{
    Properties
    {
        [HDR] _Color ("Color (窗邊／上段)", Color) = (0.8, 0.75, 1.0, 1)
        [HDR] _ColorBottom ("Color Bottom (地面／下段)", Color) = (0.8, 0.75, 1.0, 1)
        _GradientStart ("Gradient Start (開始變色位置 0=窗邊)", Range(0, 1)) = 0.1
        _GradientEnd ("Gradient End (變完位置 1=地面)", Range(0, 1)) = 0.7
        _Intensity ("Intensity", Range(0, 5)) = 0.35
        _EdgePower ("Edge Softness (Fresnel Power)", Range(0.5, 8)) = 2.5
        _TopFade ("Top Fade (窗邊淡入長度)", Range(0.001, 0.5)) = 0.08
        _BottomFade ("Bottom Fade (地面淡出長度)", Range(0.001, 1)) = 0.35
        _DepthSoftness ("Depth Softness (與物體交界柔化)", Range(0.01, 10)) = 2.0
        _CameraFade ("Camera Near Fade (鏡頭貼近淡出距離)", Range(0.1, 20)) = 4.0
        _StreakAmount ("Streak Amount (光絲明暗)", Range(0, 1)) = 0.35
        _StreakSpeed ("Streak Speed", Range(0, 2)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "LightBeam"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _ColorBottom;
                half _GradientStart;
                half _GradientEnd;
                half _Intensity;
                half _EdgePower;
                half _TopFade;
                half _BottomFade;
                half _DepthSoftness;
                half _CameraFade;
                half _StreakAmount;
                half _StreakSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fogCoord = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 n = normalize(i.normalWS);
                // 正對視線的中心亮、切邊暗（雙面都適用）
                half edge = pow(saturate(abs(dot(n, viewDir))), _EdgePower);

                // 沿長度淡入淡出
                half len = smoothstep(0.0, _TopFade, i.uv.y) * (1.0 - smoothstep(1.0 - _BottomFade, 1.0, i.uv.y));

                // 緩慢流動的光絲
                half t = _Time.y * _StreakSpeed;
                half streak = 0.5 + 0.5 * sin(i.uv.x * 6.2831 * 7.0 + t * 6.0) * sin(i.uv.x * 6.2831 * 3.0 - t * 4.0 + i.uv.y * 2.0);
                half streakMul = lerp(1.0, 0.4 + 0.6 * streak, _StreakAmount);

                // 與場景相交柔化
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                half depthFade = saturate((sceneEye - fragEye) / _DepthSoftness);

                // 鏡頭貼近淡出
                half camFade = saturate((fragEye - _ProjectionParams.y) / _CameraFade);

                half a = edge * len * streakMul * depthFade * camFade * _Intensity;
                half grad = smoothstep(_GradientStart, max(_GradientEnd, _GradientStart + 0.001), i.uv.y);
                half3 col = lerp(_Color.rgb, _ColorBottom.rgb, grad) * a;
                // Additive：霧中逐漸隱沒（往黑色混）
                col = MixFogColor(col, half3(0, 0, 0), i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
