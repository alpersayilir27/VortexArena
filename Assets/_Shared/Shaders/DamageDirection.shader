// DIRECTION indicator hung on the HMD: a soft arc at the edge of the view, authored on the quad's TOP
// side (UV +Y). The centre stays fully clear. Its only consumer is DamageDirectionIndicator; what
// carries the arc to the correct screen edge is not the shader but the quad's rotation about local Z.
//
// ⚠️ "Queue" = "Overlay+1" is DELIBERATE: the indicator sits at EXACTLY the same local depth as the
// damage vignette (Overlay) — a different depth would give the two layers different stereo disparity
// and read as double vision. So layering is done by QUEUE, not by depth, and the +1 here guarantees
// drawing above the vignette. ZTest Always + ZWrite Off match ScreenVignette for the same reason: this
// layer exists to draw ON TOP OF the obstacle blackout.
//
// ⚠️ THE QUAD'S UV IS NOT THE SCREEN — see the same warning in ScreenVignette.shader. The quad reaches
// ±65° while a Quest eye shows about ±50°/±45°, so a radius above ~0.55 never produces a single visible
// pixel. Read the numbers as angles: distance ≈ tan(angle) × 0.46, i.e. 0.22 ≈ 26°, 0.34 ≈ 36°.
//
// ⚠️ Colour and alpha come from CODE (DamageDirectionIndicator → MaterialPropertyBlock); the values on
// the material are editor preview only.
Shader "VortexArena/DamageDirection"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + alfa (kod yazar)", Color) = (0.5569, 0.1216, 0.1216, 0.0)
        _InnerRadius ("Yayın başladığı yarıçap", Range(0, 1)) = 0.22
        _OuterRadius ("Tam alfa yarıçapı", Range(0, 1.5)) = 0.34
        _ArcHalfAngle ("Yay yarı genişliği (derece)", Range(1, 90)) = 34
        _ArcFeather ("Yay kenar yumuşaması (derece)", Range(1, 90)) = 30
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Overlay+1"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "DamageDirection"
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
                float _ArcHalfAngle;
                float _ArcFeather;
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

                float2 offset = input.uv - 0.5;

                // Radial: clear centre, filling toward the edge (same scale as ScreenVignette).
                float distance = length(offset) * 2.0;
                float radial = smoothstep(_InnerRadius, max(_OuterRadius, _InnerRadius + 1e-4), distance);

                // Angular cone measured from the quad's TOP (+Y). cos() instead of atan2: cheaper and
                // single-valued — atan2 would seam at ±180 where the angle wraps, cos does not.
                float2 direction = offset * rsqrt(max(dot(offset, offset), 1e-8));
                float cosToTop = direction.y;

                float cosInner = cos(radians(_ArcHalfAngle));
                float cosOuter = cos(radians(min(_ArcHalfAngle + _ArcFeather, 180.0)));
                float arc = smoothstep(cosOuter, cosInner, cosToTop);

                return half4(_BaseColor.rgb, _BaseColor.a * radial * arc);
            }
            ENDHLSL
        }
    }

    // NO fallback: drawing pink beats silently drawing an opaque rectangle — an opaque layer would
    // block the player's view entirely.
    Fallback Off
}
