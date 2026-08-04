// Ölü ya da kalibresiz uzak oyuncunun HAYALET gövdesi.
//
// Yarı saydam ve İKİ YÜZÜ de çizilir (Cull Off + ZWrite Off): oyuncu gövdenin içini görür —
// istenen "gizmo gibi bakınca içi görünen" okuma buradan gelir, tel kafesten DEĞİL. Gerçek
// wireframe geometry shader ister ve mobil URP'de (Quest) o yol yoktur.
//
// ⚠️ ZTest LEqual'dır ve öyle KALIR: ters derinlik testi hayaleti duvarların arkasından görünür
// kılardı, yani duvar arkası avantaj üretirdi (Docs/Sistem-Ozeti.md, ters derinlik testi maddesi).
//
// ⚠️ Renk KODDAN gelir (RemoteAvatar → MaterialPropertyBlock): dost mavi, düşman kırmızı,
// kalibresizde turuncuya nabız. Materyaldeki değerler yalnız editör önizlemesidir; buradaki
// varsayılanlara bakıp "renk şu" diye karar verilmez.
//
// Gölge/derinlik pass'i YOKTUR ve eklenmez: hayalet gövdenin opak bir gölge düşürmesi onu ölü
// değil canlı gösterirdi.
Shader "VortexArena/AvatarGhost"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + taban alfa (kod yazar)", Color) = (0.20, 0.45, 0.90, 0.28)
        _RimPower ("Kenar keskinliği", Range(0.5, 8)) = 2.5
        _RimStrength ("Kenar alfa katkısı", Range(0, 3)) = 1.4
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

            // SRP Batcher uyumu için TÜM property'ler bu blokta olmalı (ve hepsi float:
            // karışık half/float yerleşimi bazı platformlarda batcher'ı devre dışı bırakır).
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _RimPower;
                float _RimStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);

                // ⚠️ abs() ŞART: Cull Off olduğu için arka yüzlerin normali kameradan UZAĞA bakar.
                // abs()'siz bir fresnel gövdenin içini tümden söndürür ve "içini görme" hissi —
                // yani bu shader'ın tek varlık sebebi — kaybolurdu.
                float facing = abs(dot(normalize(input.normalWS), viewDir));
                float rim = pow(saturate(1.0 - facing), _RimPower);

                // Taban alfa koddan, kenar parlaması onun ÜSTÜNE eklenir: silüet okunur kalır,
                // gövdenin ortası şeffaf kalır.
                float alpha = saturate(_BaseColor.a + rim * _RimStrength);
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    // Fallback YOK: pembe çizmek, sessizce OPAK bir gövde çizmekten iyidir (opak hayalet,
    // ölü oyuncuyu canlı gibi gösterir — tam da düzeltilmek istenen hata).
    Fallback Off
}
