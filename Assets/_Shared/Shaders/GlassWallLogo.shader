// Şeffaf cam / enerji duvarı + üstüne basılı marka logosu. Arena sahnelerindeki Quad
// panellerinde kullanılır. İkinci bir arena bunu aynen kullanabildiği için _Shared altındadır.
//
// ⚠️ QUEST'İN GERÇEK KISITLARINA GÖRE YAZILMIŞTIR (Mobile_RPAsset) ve o kısıtlar bu dosyanın
// biçimini belirler: **depth texture KAPALI** ve **opaque texture KAPALI** → arkadaki geometriyle
// yumuşak kesişim, buzlu cam bulanıklığı ve kırılma (refraction) YAZILAMAZ; hepsi Scene Depth /
// Scene Color ister. **HDR KAPALI** → parlaklık 1.0'da kırpılır, bloom yoktur, yani "cam parlıyor"
// hissi emission'ı büyüterek DEĞİL kenar (fresnel) kontrastıyla üretilir. Editörde (PC_RPAsset)
// ikisi de açık olduğu için burada güzel görünen bir ayar gözlükte sessizce sönük çıkabilir —
// hüküm her zaman APK'da verilir (Docs/Sistem-Ozeti.md §7).
//
// ⚠️ Unlit'tir ve öyle KALIR: cam duvar, sahne ışığının karartacağı bir yüzey değil bir kaplamadır.
//
// ⚠️ Cull Off: Quad TEK yüzlü bir mesh'tir — kültelenmiş hâlde arkasından bakan oyuncu duvarı hiç
// görmezdi ve duvar bir taraftan yok olurdu.
//
// ⚠️ Logo kaynağı JPG olabilir (alfa kanalı yok, zemin beyaz). VARSAYILAN davranış beyaz zemini
// kesmektir; alfası olan bir PNG bağlandığında "Logonun kendi alfası var" işaretlenir, yoksa maske
// iki kez uygulanır ve logo incelir.
// ⚠️ Anahtarın varsayılanı bilerek TERSTİR (keyword yokken beyaz kesilir): Unity'de [Toggle]
// property'sinin varsayılan değeri 1 olsa bile shader keyword'ü AÇILMAZ — keyword yalnız Inspector'da
// tıklanınca set edilir. Varsayılan davranış keyword'e bağlansaydı koddan/araçtan kurulan her
// materyalde sessizce kapalı kalırdı.
Shader "VortexArena/GlassWallLogo"
{
    Properties
    {
        [MainColor] _GlassColor ("Cam rengi (A = saydamlık)", Color) = (0.35, 0.72, 0.85, 0.22)

        [Header(Kenar parlamasi)]
        _RimPower ("Kenar keskinliği", Range(0.5, 8)) = 3
        _RimStrength ("Kenar şiddeti", Range(0, 2)) = 0.55

        [Header(Logo)]
        [NoScaleOffset] _LogoTex ("Logo", 2D) = "white" {}
        _LogoStrength ("Logo yoğunluğu", Range(0, 1)) = 1
        _LogoSize ("Logo boyu (duvar yüksekliğinin oranı)", Range(0.02, 2)) = 0.55
        _LogoCenter ("Logo merkezi (UV — X,Y kullanılır)", Vector) = (0.5, 0.5, 0, 0)
        [Toggle(_LOGO_TILED)] _LogoTiled ("Duvarı kapla (tekrarla)", Float) = 0

        [Header(Logo zemini)]
        // İşaretsiz = beyaz zemin kesilir (alfasız JPG). İşaretli = texture'ın kendi alfası kullanılır.
        [Toggle(_LOGO_ALPHA_CHANNEL)] _LogoHasAlpha ("Logonun kendi alfası var (PNG)", Float) = 0
        _LogoCut ("Beyaz kesme eşiği", Range(0, 1)) = 0.06

        [Header(Ileri)]
        // 0 = otomatik (objenin dünya ölçeğinden). ⚠️ Quad "Static" işaretlenirse mesh dünya
        // uzayına pişer, unity_ObjectToWorld kimliğe düşer ve otomatik oran 1 çıkar — logo ezilir.
        // O durumda buraya en/boy (genişlik ÷ yükseklik) elle yazılır.
        _LogoAspect ("Duvar en/boy oranı (0 = otomatik)", Range(0, 8)) = 0
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
            Name "GlassWall"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _LOGO_TILED
            #pragma shader_feature_local _LOGO_ALPHA_CHANNEL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Kesme maskesinin yumuşama bandı. Sabittir: logonun kenarındaki antialias piksellerini
            // yumuşatacak kadar geniş, ince harfleri yiyecek kadar dar. Ayar alanı yapılmadı —
            // ikinci bir eşik alanı iki eşiğin sessizce sapması demek olurdu.
            #define VA_LOGO_EDGE 0.08

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  aspect     : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_LogoTex);
            SAMPLER(sampler_LogoTex);

            // SRP Batcher uyumu için TÜM sayısal property'ler bu blokta ve hepsi float
            // (CharacterShieldV2/AvatarGhost ile aynı gerekçe: karışık half/float yerleşimi
            // batcher'ı düşürüyor). Toggle'ların float karşılıkları da burada olmalı — Properties
            // bloğunda olup CBUFFER'da olmayan bir alan batcher uyumunu bozar.
            CBUFFER_START(UnityPerMaterial)
                float4 _GlassColor;
                float  _RimPower;
                float  _RimStrength;
                float  _LogoStrength;
                float  _LogoSize;
                float4 _LogoCenter;
                float  _LogoTiled;
                float  _LogoHasAlpha;
                float  _LogoCut;
                float  _LogoAspect;
            CBUFFER_END

            // Duvarın en/boy oranı objenin DÜNYA ölçeğinden okunur: logo kare, panel değil.
            // Oran bilinmezse kare logo panelin oranı kadar ezilirdi.
            float ObjectAspect()
            {
                float3 axisX = float3(unity_ObjectToWorld[0][0], unity_ObjectToWorld[1][0], unity_ObjectToWorld[2][0]);
                float3 axisY = float3(unity_ObjectToWorld[0][1], unity_ObjectToWorld[1][1], unity_ObjectToWorld[2][1]);
                return length(axisX) / max(length(axisY), 1e-4);
            }

            // Logo UV'si duvarın UV'sinden TÜRETİLİR, texture'ın kendi Tiling/Offset'inden değil
            // ([NoScaleOffset]): boy ve merkez metrik olarak anlamlı iki alanla tarif edilsin,
            // aynı yerleşim iki ayrı yerden yazılabilir olmasın.
            float2 LogoUV(float2 uv, float aspect, out float box)
            {
                float size = max(_LogoSize, 1e-4);
                float2 sizeUV = float2(size / max(aspect, 1e-4), size);
                float2 luv = (uv - _LogoCenter.xy) / sizeUV + 0.5;

            #if defined(_LOGO_TILED)
                box = 1.0;
                return frac(luv);
            #else
                // Merkez kipinde logonun dışı HİÇ örneklenmez: texture'ın Wrap Mode'u Repeat
                // kalırsa dışarısı logonun kopyalarıyla dolardı.
                float2 inside = step(0.0, luv) * step(luv, 1.0);
                box = inside.x * inside.y;
                return luv;
            #endif
            }

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
                output.uv = input.uv;
                output.aspect = _LogoAspect > 0.0 ? _LogoAspect : ObjectAspect();
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Cam tabanı kenara doğru açılır (fresnel). ⚠️ abs(): çift yüzlü çizimde arka
                // yüzlerin normali kameradan UZAĞA bakar, abs olmadan duvarın arkası tümden sönerdi
                // (AvatarGhost/CharacterShieldV2'deki aynı tuzak).
                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                float facing = abs(dot(normalize(input.normalWS), viewDir));
                float rim = pow(saturate(1.0 - facing), _RimPower) * _RimStrength;

                half3 rgb = _GlassColor.rgb * (1.0 + rim);
                float alpha = saturate(_GlassColor.a + rim);

                float box;
                float2 luv = LogoUV(input.uv, input.aspect, box);
                half4 logo = SAMPLE_TEXTURE2D(_LogoTex, sampler_LogoTex, luv);

            #if defined(_LOGO_ALPHA_CHANNEL)
                float logoAlpha = logo.a;
            #else
                // Beyaz zemin EN KOYU KANALDAN türetilen bir maskeyle kesilir. min(r,g,b) seçilme
                // sebebi: logonun turkuazı ve turuncusu doygun olduğu için 1'e yakın çıkar, beyaz
                // 0 verir. Luminance ile kesmek açık turuncuyu da yerdi.
                float ink = 1.0 - min(min(logo.r, logo.g), logo.b);
                float logoAlpha = smoothstep(_LogoCut, _LogoCut + VA_LOGO_EDGE, ink);
            #endif

                logoAlpha *= _LogoStrength * box;

                // Logo camın ÜSTÜNE basılır: rengi de saydamlığı da yoğunluğu kadar bastırır.
                // _LogoStrength = 1'de logo opak, 0'da duvar tertemiz cam.
                rgb = lerp(rgb, logo.rgb, logoAlpha);
                alpha = lerp(alpha, 1.0, logoAlpha);

                rgb = MixFog(rgb, input.fogFactor);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    // Fallback YOK (AvatarGhost/CharacterShieldV2 ile aynı gerekçe): pembe çizmek, sessizce opak
    // bir duvar çizip arenanın yarısını gizlemekten iyidir.
    Fallback Off
}
