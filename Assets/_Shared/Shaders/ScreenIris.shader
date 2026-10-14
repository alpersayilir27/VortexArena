// Iris closing on the HMD: opaque black everywhere outside an elliptical clear hole whose radius the
// code shrinks to 0. Its only consumer is the floor portal dwell (PortalIris).
//
// ⚠️ THE QUAD'S UV IS NOT THE SCREEN. The quad spans about ±64° from the view centre (0.95 m
// half-extent at 0.46 m) while a Quest eye shows roughly ±50° across and ±45° down, so the FOV corner
// sits near distance 0.7 and a clear radius above that is invisible — the "fully open" radius must stay
// above it or the frame would never look clean.
//
// ⚠️ _Aspect widens the hole horizontally to match the wider horizontal FOV; at 1 the darkness would
// reach the left and right edges late and the close would read as a vertical squeeze.
//
// ⚠️ Radii come from CODE (PortalIris → MaterialPropertyBlock); the material values are editor preview
// only. ZTest Always + no ZWrite: this is a view overlay, geometry must never occlude it.
Shader "VortexArena/ScreenIris"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + alfa", Color) = (0, 0, 0, 1)
        _ClearRadius ("Şeffaf göbek yarıçapı (kod yazar)", Range(0, 1.5)) = 0.75
        _EdgeSoftness ("Kenar yumuşaklığı (kod yazar)", Range(0.001, 0.5)) = 0.18
        _Aspect ("Yatay genişleme", Range(0.5, 2)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ScreenIris"
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

            // SRP Batcher compatibility: ALL properties must live in this block (and all be float).
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _ClearRadius;
                float _EdgeSoftness;
                float _Aspect;
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

                float2 offset = (input.uv - 0.5) * 2.0;
                offset.x /= max(_Aspect, 0.001);
                float distance = length(offset);

                float inner = _ClearRadius;
                float outer = inner + max(_EdgeSoftness, 1e-4);
                float dark = smoothstep(inner, outer, distance);

                return half4(_BaseColor.rgb, _BaseColor.a * dark);
            }
            ENDHLSL
        }
    }

    // NO fallback: drawing pink beats silently drawing an opaque rectangle — the player would be blind.
    Fallback Off
}
