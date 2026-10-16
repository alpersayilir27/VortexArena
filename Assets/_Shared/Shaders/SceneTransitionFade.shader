// Scene transition black on the HMD: a flat colour quad whose alpha the code ramps (SceneTransitionFade
// → MaterialPropertyBlock). Its only consumer is the transition quad under CenterEyeAnchor.
//
// ⚠️ ZTest Always is DELIBERATE: the held weapon and the hands sit nearer than the quad (0.5 m); with a
// depth test they, and the ammo text on the weapon, would stay lit on the black screen.
// ⚠️ Layering against the UI comes from the renderer's sortingOrder (HmdOverlayBuilder), not from the
// queue — the queue stays at the UI's 3000 so only sortingOrder decides.
Shader "VortexArena/SceneTransitionFade"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + alfa (kod yazar)", Color) = (0, 0, 0, 0)
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
            Name "SceneTransitionFade"
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // SRP Batcher compatibility: ALL properties must live in this block.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(_BaseColor);
            }
            ENDHLSL
        }
    }

    // NO fallback: drawing pink beats silently drawing an opaque rectangle — the player would be blind.
    Fallback Off
}
