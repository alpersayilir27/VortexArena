// Ketchup stain on the player's own visor (BurgerKetchupVision). Same contract as ScreenVignette:
// ⚠️ "Queue" = "Overlay" + ZTest Always so it draws over the obstacle blackout, and it is NOT a
// ScreenFade source. ⚠️ The centre is forced clear by the ring mask whatever the texture holds —
// the player walks in a real room. Ring radii read as angles like ScreenVignette (0.18 ≈ 21°).
// Colour, alpha and texture come from code (MaterialPropertyBlock).
Shader "VortexArena/Burger/KetchupVision"
{
    Properties
    {
        [MainTexture] _BaseMap ("Leke dokusu (alfa = leke)", 2D) = "white" {}
        [MainColor] _BaseColor ("Renk + alfa (kod yazar)", Color) = (0.62, 0.07, 0.06, 0.0)
        _InnerRadius ("Şeffaf göbek yarıçapı", Range(0, 1)) = 0.2
        _OuterRadius ("Tam leke yarıçapı", Range(0, 1.5)) = 0.42
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "KetchupVision"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // SRP Batcher: every material property lives in this block.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _InnerRadius;
                float _OuterRadius;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float distance = length(input.uv - 0.5) * 2.0;
                float ring = smoothstep(_InnerRadius, max(_OuterRadius, _InnerRadius + 1e-4), distance);
                half stain = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(input.uv, _BaseMap)).a;
                return half4(_BaseColor.rgb, _BaseColor.a * ring * stain);
            }
            ENDHLSL
        }
    }

    // No fallback: an opaque fallback quad would cover the whole view.
    Fallback Off
}
