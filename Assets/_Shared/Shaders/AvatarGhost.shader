// GHOST body of a dead or uncalibrated remote player, and the floor silhouette of a player on
// another floor (_FloorShift).
//
// Translucent and DOUBLE SIDED (Cull Off + ZWrite Off): the player sees the inside of the body —
// the wanted "gizmo-like, see-through" read comes from here, NOT from a wireframe. A real
// wireframe needs a geometry shader and mobile URP (Quest) has no such path.
//
// ⚠️ ZTest is LEqual and STAYS so: inverted depth test would make the ghost visible through walls,
// i.e. a see-through-wall advantage (Docs/Sistem-Ozeti.md, inverted depth test item).
//
// ⚠️ Colour comes FROM CODE (RemoteAvatar → MaterialPropertyBlock): the player's own team (red/
// blue, neutral in teamless modes), pulsing orange while uncalibrated. Material values are editor
// preview only; never decide "the colour is X" from the defaults here.
//
// There is NO shadow/depth pass and none is added: an opaque shadow under the ghost body would
// read as alive rather than dead.
Shader "VortexArena/AvatarGhost"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + taban alfa (kod yazar)", Color) = (0.20, 0.45, 0.90, 0.28)
        _RimPower ("Kenar keskinliği", Range(0.5, 8)) = 2.5
        _RimStrength ("Kenar alfa katkısı", Range(0, 3)) = 1.4
        _FloorShift ("Dünya Y kaydırması (kod yazar)", Float) = 0
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
            Name "AvatarGhost"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ALL properties must live in this block for SRP Batcher compatibility (and all as float:
            // a mixed half/float layout disables the batcher on some platforms).
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _RimPower;
                float _RimStrength;
                float _FloorShift;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                // ⚠️ positionWS is shifted BEFORE the clip transform and the shifted value is what the
                // fragment gets: the rim must be computed from the DRAWN position, not the real one.
                float3 positionWS = positions.positionWS;
                positionWS.y += _FloorShift;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normals.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);

                // ⚠️ abs() is MANDATORY: with Cull Off the back faces' normals point AWAY from the
                // camera. Without abs() the fresnel kills the body's interior and the "see inside"
                // read — the only reason this shader exists — would be lost.
                float facing = abs(dot(normalize(input.normalWS), viewDir));
                float rim = pow(saturate(1.0 - facing), _RimPower);

                // Base alpha from code, the rim glow added ON TOP: the silhouette stays readable while
                // the middle of the body stays transparent.
                float alpha = saturate(_BaseColor.a + rim * _RimStrength);
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    // NO fallback: drawing pink is better than silently drawing an OPAQUE body (an opaque ghost shows
    // a dead player as alive — exactly the fault this fixes).
    Fallback Off
}
