// Doğma koruması (§10.4) kalkanı — uzak oyuncunun gövdesinin ÜSTÜNE binen kabuk renderer'ında
// çizilir (RemoteAvatar.BuildShieldShell). Karakterin kendi materyaline dokunulmaz: korunan
// oyuncu normal görünmeye devam etmeli, kalkan onun üstüne eklenen bir katmandır.
//
// ⚠️ QUEST'İN GERÇEK KISITLARINA GÖRE YAZILMIŞTIR ve o kısıtlar bu dosyanın biçimini belirler
// (Mobile_RPAsset): **depth texture KAPALI** → `Scene Depth`'e dayanan hiçbir katman yazılmaz
// (kesişim parlaması, yumuşak geçiş); **HDR KAPALI** → parlaklık 1.0'da kırpılır, bloom yoktur,
// yani "parlıyor" hissi emission'ı büyüterek DEĞİL desen/kenar/silüet kontrastıyla üretilir.
// Editörde (PC_RPAsset) ikisi de açık olduğu için burada güzel görünen bir efekt gözlükte sessizce
// sönük çıkabilir — hüküm her zaman APK'da verilir (Docs/Sistem-Ozeti.md §7).
//
// ⚠️ Unlit'tir ve öyle KALIR: kalkan bir kuvvet alanı, sahne ışığının karartacağı bir yüzey değil.
//
// ⚠️ Kalkanın rengi TAKIM RENGİ DEĞİLDİR ve olmayacak: takım renginde bir kalkan, takım renginde
// çizilen hayaletle (M_AvatarGhost, mavi-saydam) karışır. İkisi ayrı bilgi, ayrı görünüm.
//
// İki pass:
//   1) "Fill"   — gövdeye yapışık enerji derisi, ALFA harmanlı. Additif olmamasının sebebi
//                 IceWorld gibi karlı/parlak haritalardır: additif dolgu orada beyaza doyar ve
//                 kalkan kaybolur. Alfa harman her zeminde okunur.
//   2) "Bubble" — normal boyunca dışarı ötelenmiş kabarcık, ADDİTİF ve çift yüzlü. Silüeti
//                 büyüten katman budur; "bu adama dokunulamaz" okumasının çoğu buradan gelir.
//
// ⚠️ Şişirme VERTEX'te yapılır, mesh çoğaltarak DEĞİL: karakter FBX'lerinde Read/Write kapalı ve
// açmak Quest'te oyuncu başına mesh belleği demek. Shader tarafı hem bedava hem de mesh'in
// import ayarına bağlı değil.
Shader "VortexArena/CharacterShieldV2"
{
    Properties
    {
        [MainColor] _ShieldColor ("Kalkan rengi", Color) = (0.30, 1.0, 0.55, 1)
        _Intensity ("Genel şiddet", Range(0, 3)) = 1

        [Header(Govde derisi)]
        _FillAlpha ("Gövde dolgusu (alfa)", Range(0, 1)) = 0.22
        _Pattern ("Altıgen ızgara", 2D) = "white" {}
        _PatternScale ("Desen ölçeği (1/m)", Range(0.5, 20)) = 6
        _ScrollSpeed ("Desen kayma hızı", Range(-2, 2)) = 0.35

        [Header(Kenar ve kabarcik)]
        _RimPower ("Kenar keskinliği", Range(0.5, 8)) = 2.5
        _RimStrength ("Kenar şiddeti", Range(0, 4)) = 1.8
        _Inflate ("Kabarcık kalınlığı (m)", Range(0, 0.12)) = 0.03

        [Header(Hareket)]
        _PulseSpeed ("Nabız hızı (Hz)", Range(0, 6)) = 1.6
        _PulseAmount ("Nabız derinliği", Range(0, 1)) = 0.35
        _BandSpeed ("Bant hızı (m/sn)", Range(0, 4)) = 0.9
        _BandWidth ("Bant kalınlığı (m)", Range(0.01, 0.6)) = 0.14
        _BandStrength ("Bant şiddeti", Range(0, 3)) = 1.2

        // Kod yazar (RemoteAvatar → MaterialPropertyBlock): doğuşta kısa bir parlama, koruma
        // bitince sıfıra erime. 1 = normal. Materyaldeki değer yalnız editör önizlemesidir.
        _Fade ("Açılış/kapanış (kod yazar)", Range(0, 2)) = 1
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #define VA_TWO_PI 6.2831853

        // Bandın döngü boyu (m). Sabittir: oyuncunun boyu ~1.8 m, 2 m'lik döngü gövdeyi bir
        // taramada geçer. Ayar alanı yapılmadı — bant kalınlığı zaten metre cinsinden ayarlanıyor.
        #define VA_BAND_PERIOD 2.0

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
            float3 positionOS : TEXCOORD2;
            float3 normalOS : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        TEXTURE2D(_Pattern);
        SAMPLER(sampler_Pattern);

        // SRP Batcher uyumu için TÜM sayısal property'ler bu blokta ve hepsi float
        // (AvatarGhost.shader ile aynı gerekçe: karışık half/float yerleşimi batcher'ı düşürüyor).
        CBUFFER_START(UnityPerMaterial)
            float4 _ShieldColor;
            float _Intensity;
            float _FillAlpha;
            float _PatternScale;
            float _ScrollSpeed;
            float _RimPower;
            float _RimStrength;
            float _Inflate;
            float _PulseSpeed;
            float _PulseAmount;
            float _BandSpeed;
            float _BandWidth;
            float _BandStrength;
            float _Fade;
        CBUFFER_END

        // İki pass de aynı köşe işini yapar, yalnız öteleme mesafesi farklıdır.
        // ⚠️ Desen ÖTELENMEMİŞ konumdan üretilir (output.positionOS): kabarcık kalınlığı
        // değiştiğinde desenin gövde üstündeki yeri kaymasın.
        Varyings ShieldVert(Attributes input, float offset)
        {
            Varyings output = (Varyings)0;

            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 normalOS = normalize(input.normalOS);
            float3 offsetOS = input.positionOS.xyz + normalOS * offset;

            VertexPositionInputs positions = GetVertexPositionInputs(offsetOS);
            VertexNormalInputs normals = GetVertexNormalInputs(normalOS);

            output.positionCS = positions.positionCS;
            output.positionWS = positions.positionWS;
            output.normalWS = normals.normalWS;
            output.positionOS = input.positionOS.xyz;
            output.normalOS = normalOS;
            return output;
        }

        float ShieldPulse()
        {
            return 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed * VA_TWO_PI);
        }

        // Altıgen ızgara OBJE uzayından projekte edilir, karakterin kendi UV'sinden DEĞİL: atlas
        // UV'si gövde boyunca gerilme ve dikiş üretiyor. İki eksenli projeksiyon (ön yüz + yan yüz)
        // normale göre harmanlanır — tek eksenli projeksiyon kolların yanında çizgiye dönüşür.
        float ShieldPattern(float3 positionOS, float3 normalOS)
        {
            float3 p = positionOS * _PatternScale;
            float3 n = abs(normalize(normalOS));

            float blend = n.z / max(n.x + n.z, 1e-4);
            float2 uv = lerp(p.zy, p.xy, blend);
            uv.y += _Time.y * _ScrollSpeed;

            return SAMPLE_TEXTURE2D(_Pattern, sampler_Pattern, uv).r;
        }

        // Yukarı süzülen tarama bandı DÜNYA uzayında yürür: obje uzayında yürüseydi oyuncu eğilip
        // döndükçe bant da eğilirdi. Dünya uzayında her zaman yatay kalır ve "taranıyor" okunur.
        float ShieldBand(float3 positionWS)
        {
            float phase = frac((positionWS.y - _Time.y * _BandSpeed) / VA_BAND_PERIOD);
            float distanceMeters = abs(phase - 0.5) * VA_BAND_PERIOD * 0.5;
            float band = saturate(1.0 - distanceMeters / max(_BandWidth, 1e-3));
            return band * band;
        }

        // twoSided: çift yüzlü çizimde (Cull Off) arka yüzlerin normali kameradan UZAĞA bakar;
        // abs() olmadan kabarcığın iç yüzeyi tümden sönerdi (AvatarGhost.shader'daki aynı tuzak).
        float ShieldRim(float3 normalWS, float3 positionWS, bool twoSided)
        {
            float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
            float facing = dot(normalize(normalWS), viewDir);
            facing = twoSided ? abs(facing) : saturate(facing);
            return pow(saturate(1.0 - facing), _RimPower);
        }
        ENDHLSL

        // ── 1. Gövde derisi ─────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShieldFill"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex VertFill
            #pragma fragment FragFill
            #pragma multi_compile_instancing

            // Gövdeyle aynı köşelerde durduğu için milimetrik bir öteleme: karakteri çizen shader
            // (URP Lit) farklı bir köşe yolu izlediğinden derinlikler bit-birebir aynı olmayabilir.
            #define VA_FILL_OFFSET 0.0015

            Varyings VertFill(Attributes input)
            {
                return ShieldVert(input, VA_FILL_OFFSET);
            }

            half4 FragFill(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float pattern = ShieldPattern(input.positionOS, input.normalOS);
                float rim = ShieldRim(input.normalWS, input.positionWS, false) * _RimStrength;
                float band = ShieldBand(input.positionWS) * _BandStrength;

                // Taban dolgu ızgarayla modüle edilir (çizgiler parlar, gözler sönük kalır);
                // kenar ve bant onun ÜSTÜNE eklenir — silüet her zaman en okunur yer olsun.
                float energy = _FillAlpha * (0.35 + 0.65 * pattern) + rim + band;
                energy *= ShieldPulse() * _Intensity * _Fade;

                // Renk de enerjiyle biraz açılır: HDR kapalı olduğu için parlaklık farkı ancak
                // böyle okunuyor (emission büyütmenin karşılığı yok, 1.0'da kırpılıyor).
                half3 rgb = _ShieldColor.rgb * (0.65 + 0.75 * saturate(energy));
                return half4(rgb, saturate(energy));
            }
            ENDHLSL
        }

        // ── 2. Kabarcık ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShieldBubble"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex VertBubble
            #pragma fragment FragBubble
            #pragma multi_compile_instancing

            // Kabarcığın toplam katkı tavanı. Additif olduğu için karlı zeminde beyaza doymasın
            // diye baskılanır — parlaklık şiddetten değil KENAR kontrastından gelir.
            #define VA_BUBBLE_GAIN 0.6

            Varyings VertBubble(Attributes input)
            {
                // Sönerken kabarcık içeri çöker: _Fade doğuş parlamasında 1'i aşabildiği için
                // saturate — kalınlık parlamayla birlikte şişip inmemeli.
                return ShieldVert(input, _Inflate * saturate(_Fade));
            }

            half4 FragBubble(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float pattern = ShieldPattern(input.positionOS, input.normalOS);
                float rim = ShieldRim(input.normalWS, input.positionWS, true);
                float band = ShieldBand(input.positionWS) * _BandStrength * 0.5;

                // ⚠️ Kabarcık AĞIRLIKLI OLARAK KENARDIR: iç yüzeyin katkısı düşük tutulur, yoksa
                // karakter yeşil bir bulutun içinde kaybolur ve kim olduğu okunmaz.
                float energy = rim * _RimStrength * (0.45 + 0.55 * pattern) + band;
                energy *= ShieldPulse() * _Intensity * _Fade * VA_BUBBLE_GAIN;

                // Additif: alfa kanalının karşılığı yok, katkı doğrudan renkte taşınır.
                return half4(_ShieldColor.rgb * saturate(energy), 1.0);
            }
            ENDHLSL
        }
    }

    // Fallback YOK (AvatarGhost.shader ile aynı gerekçe): pembe çizmek, sessizce opak bir kabuk
    // çizip karakteri tümden gizlemekten iyidir.
    Fallback Off
}
