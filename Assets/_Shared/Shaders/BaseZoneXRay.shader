// Taban bölgesi şeridinin "duvar arkasından görünen" ikinci çizimi.
//
// Şerit mesh'i bu materyalle İKİNCİ bir slot olarak çizilir (BaseZoneVisibility ekler):
// ZTest Greater sayesinde yalnız şeridin ÖNÜNDE başka bir geometri olduğu piksellerde görünür.
// Önü açıkken hiç çizilmez — orada zaten opak takım materyali duruyor.
//
// ⚠️ Ters derinlik testi oyuncunun KENDİ silahı, eli ve gövde avatarı için de geçerlidir:
// tabanın içinde durup aşağı bakınca hayalet silahın üstüne çizilirdi. _NearFade* bunu keser.
Shader "VortexArena/BaseZoneXRay"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk (kod takım şeridinden okur)", Color) = (0.85, 0.15, 0.15, 1)
        _Alpha ("Alfa", Range(0, 1)) = 0.25
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
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // SRP Batcher uyumu için TÜM property'ler bu blokta olmalı (ve hepsi float:
            // karışık half/float yerleşimi bazı platformlarda batcher'ı devre dışı bırakır).
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Alpha;
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float viewDist = length(_WorldSpaceCameraPos - input.positionWS);
                float span = max(_NearFadeEnd - _NearFadeStart, 0.0001);
                float fade = saturate((viewDist - _NearFadeStart) / span);

                return half4(_BaseColor.rgb, _Alpha * fade);
            }
            ENDHLSL
        }
    }

    // Fallback YOK: hata durumunda pembe çizmek, sessizce yanlış yerde bir hayalet çizmekten iyidir.
    Fallback Off
}
