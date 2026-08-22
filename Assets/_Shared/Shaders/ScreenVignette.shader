// Radial vignette hung on the HMD: fully clear in the middle, filling with colour toward the edges.
// Its only consumer is the health-loss indicator (DamageVignette).
//
// ⚠️ "Queue" = "Overlay" + ZTest Always is DELIBERATE and does not change: this layer exists solely to
// draw ON TOP OF THE OBSTACLE BLACKOUT. With the blackout quad (Transparent) at full black (alpha 1) a
// red in the same queue would be left to distance sorting, and the player could not see their health
// draining on a pitch-black screen — that is the exact bug being fixed. For the same reason this effect
// is not added as a ScreenFade source either ("highest alpha wins" would swallow it).
//
// ⚠️ THE QUAD'S UV IS NOT THE SCREEN — this is where the radii come from. The quad spans about ±65°
// from the view centre (0.95 m half-extent at 0.44 m), while a Quest eye shows roughly ±50° across and
// ±45° down. So distance = 1.0 (the quad's edge midpoint) is FAR outside the field of view, and every
// radius authored above ~0.55 is invisible on the headset no matter what alpha the code writes. Read
// the numbers as angles instead: distance ≈ tan(angle) × 0.46, i.e. 0.18 ≈ 21°, 0.36 ≈ 38°.
//
// ⚠️ Colour and alpha come from CODE (DamageVignette → MaterialPropertyBlock); the values on the
// material are editor preview only.
Shader "VortexArena/ScreenVignette"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + alfa (kod yazar)", Color) = (0.75, 0.03, 0.03, 0.0)
        _InnerRadius ("Şeffaf göbek yarıçapı", Range(0, 1)) = 0.18
        _OuterRadius ("Tam alfa yarıçapı", Range(0, 1.5)) = 0.36
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
            Name "ScreenVignette"
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

                // Distance from the centre: 0 in the core, 1 at an edge midpoint, ~1.41 at the corners.
                float distance = length(input.uv - 0.5) * 2.0;
                float ring = smoothstep(_InnerRadius, max(_OuterRadius, _InnerRadius + 1e-4), distance);

                return half4(_BaseColor.rgb, _BaseColor.a * ring);
            }
            ENDHLSL
        }
    }

    // NO fallback: drawing pink beats silently drawing an opaque rectangle — an opaque vignette would
    // block the player's view entirely.
    Fallback Off
}
