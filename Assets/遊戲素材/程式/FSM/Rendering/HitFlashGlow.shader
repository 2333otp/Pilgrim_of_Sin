// 受擊暖色光暈：由 HitFlashRendererFeature 用「覆寫材質」重畫一次被打中的模型，疊加（Additive）一層暖光。
// 不依賴原本材質的自發光開關（URP 打包會把沒用到的自發光變體拿掉），編輯器與正式版效果一致。
// 亮度 _HitGlow 由 HitFeedback 用 MaterialPropertyBlock 逐個模型設定（一打中最亮，再平滑退掉）。
Shader "PilgrimOfSin/HitFlashGlow"
{
    Properties
    {
        _HitGlowColor ("光暈顏色", Color) = (1, 0.72, 0.4, 1)
        _HitGlow ("光暈強度（程式設定）", Range(0, 2)) = 0
        _RimPower ("邊緣光集中度", Range(0.5, 8)) = 2.5
        _FlatAmount ("整體底光比例（0 = 只有邊緣光）", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "HitFlashGlow"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HitGlowColor;
                half _HitGlow;
                half _RimPower;
                half _FlatAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                half3 v = normalize(input.viewDirWS);
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);
                // 底光讓整個模型都亮一點（深色部位也看得到），邊緣光讓輪廓特別亮、保留立體感
                half amount = _HitGlow * (_FlatAmount + (1.0h - _FlatAmount) * rim);
                return half4(_HitGlowColor.rgb * amount, 0);
            }
            ENDHLSL
        }
    }
}
