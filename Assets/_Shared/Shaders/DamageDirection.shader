// HMD'ye asılı YÖN göstergesi: görüşün kenarında, quad'ın ÜST tarafına (UV +Y) yerleşmiş yumuşak
// bir yay. Merkez tamamen temiz kalır. Tek tüketicisi DamageDirectionIndicator'dır; yayı ekranın
// doğru kenarına taşıyan şey shader değil, quad'ın local Z ekseni etrafındaki dönüşüdür.
//
// ⚠️ "Queue" = "Overlay+1" BİLİNÇLİDİR: gösterge, hasar vinyetiyle (Overlay) BİREBİR AYNI local
// derinlikte durur — farklı derinlik iki katmana farklı stereo ayrışması verir ve çift görüntü
// olarak okunur. Bu yüzden katmanlama derinlikle değil KUYRUKLA yapılır; buradaki +1 vinyetin
// üstünde çizilmeyi garanti eder. ZTest Always + ZWrite Off aynı sebeple ScreenVignette ile aynıdır:
// bu katmanın varlık sebebi engel karartmasının ÜSTÜNE çizilmektir.
//
// ⚠️ Renk ve alfa KODDAN gelir (DamageDirectionIndicator → MaterialPropertyBlock); materyaldeki
// değerler yalnız editör önizlemesidir.
Shader "VortexArena/DamageDirection"
{
    Properties
    {
        [MainColor] _BaseColor ("Renk + alfa (kod yazar)", Color) = (0.5569, 0.1216, 0.1216, 0.0)
        _InnerRadius ("Yayın başladığı yarıçap", Range(0, 1)) = 0.62
        _OuterRadius ("Tam alfa yarıçapı", Range(0, 1.5)) = 1.0
        _ArcHalfAngle ("Yay yarı genişliği (derece)", Range(1, 90)) = 30
        _ArcFeather ("Yay kenar yumuşaması (derece)", Range(1, 90)) = 28
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

            // SRP Batcher uyumu için TÜM property'ler bu blokta olmalı (ve hepsi float).
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

                // Radial: merkez temiz, kenara doğru dolar (ScreenVignette ile aynı ölçek).
                float distance = length(offset) * 2.0;
                float radial = smoothstep(_InnerRadius, max(_OuterRadius, _InnerRadius + 1e-4), distance);

                // Açısal koni, quad'ın ÜST yönünden (+Y) ölçülür. atan2 yerine kosinüs: hem ucuz hem
                // tek değerli — açı sarmaladığı için atan2 ±180'de dikiş verirdi, kosinüs vermez.
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

    // Fallback YOK: pembe çizmek, sessizce opak bir dikdörtgen çizmekten iyidir — opak bir katman
    // oyuncunun görüşünü tümden kapatırdı.
    Fallback Off
}
