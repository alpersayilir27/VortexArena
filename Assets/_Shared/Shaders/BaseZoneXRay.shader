// Dead player's emphasis slot on their own base strip (added by BaseZoneVisibility).
//
// Pass 1 (ZTest Greater): ghost visible only where other geometry is IN FRONT of the strip.
// Pass 2 (ZTest LEqual, additive): brightens the directly visible strip — "dead = stronger".
// Hue is the strip's team color normalized to full brightness: the strip's own color is a dark
// glow base and would make an invisible ghost.
//
// ⚠️ Inverted depth test also hits the player's OWN weapon, hand and body avatar: standing in
// the base looking down, the ghost would draw over the weapon. _NearFade* prevents that.
Shader "VortexArena/BaseZoneXRay"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk (kod takım şeridinden okur)", Color) = (0.85, 0.15, 0.15, 1)
        _Alpha ("Duvar arkası alfa", Range(0, 1)) = 0.5
        _OverlayStrength ("Görünen şeride ek parlaklık", Range(0, 1)) = 0.35
        _NearFadeStart ("Yakın sönüm: tamamen görünmez (m)", Float) = 2
        _NearFadeEnd ("Yakın sönüm: tam alfa (m)", Float) = 3.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // SRP Batcher: ALL properties in this block, all float (mixed half/float layout disables
        // the batcher on some platforms).
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Alpha;
            float _OverlayStrength;
            float _NearFadeStart;
            float _NearFadeEnd;
        CBUFFER_END

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;

            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = positions.positionCS;
            output.positionWS = positions.positionWS;
            return output;
        }

        // Team hue at full brightness (dark red 0.3 -> red 1.0).
        float3 TeamHue()
        {
            float peak = max(max(_BaseColor.r, _BaseColor.g), _BaseColor.b);
            return _BaseColor.rgb / max(peak, 0.0001);
        }
        ENDHLSL

        Pass
        {
            Name "BaseZoneXRayOccluded"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Greater
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOccluded
            #pragma multi_compile_instancing

            half4 FragOccluded(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float viewDist = length(_WorldSpaceCameraPos - input.positionWS);
                float span = max(_NearFadeEnd - _NearFadeStart, 0.0001);
                float fade = saturate((viewDist - _NearFadeStart) / span);

                return half4(TeamHue(), _Alpha * fade);
            }
            ENDHLSL
        }

        // ⚠️ URP draws SRPDefaultUnlit alongside UniversalForward — two passes on one material slot.
        // Offset: same mesh at the same depth as the opaque strip, avoids z-fighting.
        Pass
        {
            Name "BaseZoneXRayVisibleBoost"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest LEqual
            ZWrite Off
            Cull Back
            Offset -1, -1
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVisible
            #pragma multi_compile_instancing

            half4 FragVisible(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(TeamHue() * _OverlayStrength, 0);
            }
            ENDHLSL
        }
    }

    // No fallback: drawing pink on error beats silently drawing a ghost in the wrong place.
    Fallback Off
}
