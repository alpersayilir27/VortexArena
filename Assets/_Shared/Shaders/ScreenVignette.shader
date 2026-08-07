// HMD'ye asılı radyal vinyet: ortası tamamen şeffaf, kenarlara doğru dolan renkli bir çerçeve.
// Tek tüketicisi can kaybı göstergesidir (DamageVignette).
//
// ⚠️ "Queue" = "Overlay" + ZTest Always BİLİNÇLİDİR ve değiştirilmez: bu katmanın tek varlık
// sebebi ENGEL KARARTMASININ ÜSTÜNE çizilmektir. Karartma quad'ı (Transparent) tam siyahken
// (alfa 1) aynı kuyruktaki bir kırmızı mesafe sıralamasına kalırdı ve oyuncu kapkaranlık bir
// ekranda canının gittiğini göremezdi — düzeltilmek istenen hata tam olarak budur. Aynı sebeple
// bu efekt ScreenFade hakemine kaynak olarak da eklenmez ("en yüksek alfa kazanır" onu yutar).
//
// ⚠️ Renk ve alfa KODDAN gelir (DamageVignette → MaterialPropertyBlock); materyaldeki değerler
// yalnız editör önizlemesidir.
Shader "VortexArena/ScreenVignette"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + alfa (kod yazar)", Color) = (0.75, 0.03, 0.03, 0.0)
        _InnerRadius ("Şeffaf göbek yarıçapı", Range(0, 1)) = 0.32
        _OuterRadius ("Tam alfa yarıçapı", Range(0, 1.5)) = 0.78
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

            // SRP Batcher uyumu için TÜM property'ler bu blokta olmalı (ve hepsi float).
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

                // Merkezden uzaklık: göbekte 0, kenar ortasında 1, köşelerde ~1.41.
                float distance = length(input.uv - 0.5) * 2.0;
                float ring = smoothstep(_InnerRadius, max(_OuterRadius, _InnerRadius + 1e-4), distance);

                return half4(_BaseColor.rgb, _BaseColor.a * ring);
            }
            ENDHLSL
        }
    }

    // Fallback YOK: pembe çizmek, sessizce opak bir dikdörtgen çizmekten iyidir — opak bir vinyet
    // oyuncunun görüşünü tümden kapatırdı.
    Fallback Off
}
