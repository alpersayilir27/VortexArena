Shader "VortexArena/GripSocket"
{
    // Ön kabza soketi: yarı saydam cam küre. Merkez neredeyse boş, kenar (fresnel) parlar —
    // hacim okunur ama içindeki el/silah kapanmaz. Üstüne akan gürültü + tarama bandı +
    // nefes/titreme: küre "canlı" görünsün, oyuncunun gözü onu boşluktan ayırsın.
    //
    // ⚠️ Alfayı KOD sürer: Weapon.TickSecondaryGripIndicator her karede Material.color yazar
    // (yaklaşırken 0.30, kabul hacminin içinde 0.50). Bu yüzden _BaseColor [MainColor]'dır ve
    // buradaki alfa yalnız EDİTÖRDE görülen varsayılandır; oyunda ezilir. Aşağıdaki tüm süs
    // çarpanları o alfanın ÜSTÜNE çarpan olarak biner — hiçbiri "içerideyim/dışarıdayım"
    // okumasını bozmasın diye 1'in etrafında salınır.
    //
    // ⚠️ Doku YOKTUR ve eklenmez: gürültü prosedüreldir (hash tabanlı value noise). Quest'te
    // tek küçük küre için doku örneklemesi bant genişliği harcar, üstelik ikinci bir asset olurdu.
    Properties
    {
        [MainColor] _BaseColor ("Renk (alfayı kod sürer)", Color) = (0.55, 0.82, 1, 0.5)

        [Header(Kenar)]
        _RimPower ("Kenar keskinliği", Range(0.5, 8)) = 3.0
        _RimBoost ("Kenar parlaklığı", Range(0, 4)) = 2.2
        _CoreAlpha ("Merkez doluluğu", Range(0, 1)) = 0.10
        _InnerAlpha ("İç yüz yoğunluğu", Range(0, 1)) = 0.45

        [Header(Gurultu)]
        _NoiseScale ("Gürültü sıklığı", Range(1, 40)) = 9
        _NoiseAmount ("Gürültü miktarı", Range(0, 1)) = 0.35
        _NoiseSpeed ("Gürültü akış hızı", Range(0, 4)) = 0.35
        _GrainAmount ("İnce tanecik", Range(0, 1)) = 0.15

        [Header(Tarama)]
        _ScanScale ("Tarama sıklığı", Range(0, 80)) = 26
        _ScanSpeed ("Tarama hızı", Range(0, 10)) = 1.5
        _ScanAmount ("Tarama miktarı", Range(0, 1)) = 0.18

        [Header(Nabiz)]
        _PulseSpeed ("Nabız hızı", Range(0, 6)) = 1.2
        _PulseAmount ("Nabız miktarı", Range(0, 0.6)) = 0.22
        _FlickerSpeed ("Titreme hızı", Range(0, 30)) = 9
        _FlickerAmount ("Titreme miktarı", Range(0, 0.6)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher sözleşmesi: materyalin TÜM property'leri bu CBUFFER'da olmalı.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _RimPower;
            float _RimBoost;
            float _CoreAlpha;
            float _InnerAlpha;
            float _NoiseScale;
            float _NoiseAmount;
            float _NoiseSpeed;
            float _GrainAmount;
            float _ScanScale;
            float _ScanSpeed;
            float _ScanAmount;
            float _PulseSpeed;
            float _PulseAmount;
            float _FlickerSpeed;
            float _FlickerAmount;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float3 positionOS : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 cell = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float n000 = Hash13(cell);
            float n100 = Hash13(cell + float3(1, 0, 0));
            float n010 = Hash13(cell + float3(0, 1, 0));
            float n110 = Hash13(cell + float3(1, 1, 0));
            float n001 = Hash13(cell + float3(0, 0, 1));
            float n101 = Hash13(cell + float3(1, 0, 1));
            float n011 = Hash13(cell + float3(0, 1, 1));
            float n111 = Hash13(cell + float3(1, 1, 1));

            float x00 = lerp(n000, n100, f.x);
            float x10 = lerp(n010, n110, f.x);
            float x01 = lerp(n001, n101, f.x);
            float x11 = lerp(n011, n111, f.x);

            return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
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
            // ⚠️ Süs deseni OBJE uzayında hesaplanır: dünya uzayında hesaplansaydı desen sabit
            // kalır, küre silahla birlikte hareket ederken içinden kayan bir sis gibi görünürdü.
            output.positionOS = input.positionOS.xyz;
            return output;
        }

        half4 ShadeSocket(Varyings input, float alphaScale)
        {
            float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
            float3 normal = normalize(input.normalWS);

            // İç yüz pass'inde normal kameradan kaçar; çevirmezsek o yüzde kenar hiç parlamaz.
            normal = dot(normal, viewDir) < 0.0 ? -normal : normal;

            float rim = pow(1.0 - saturate(dot(normal, viewDir)), _RimPower);

            float time = _Time.y;

            // Akan bulut + ince tanecik.
            float3 noisePos = input.positionOS * _NoiseScale + float3(0.0, -time * _NoiseSpeed, time * _NoiseSpeed * 0.6);
            float cloud = ValueNoise(noisePos);
            float grain = Hash13(floor(input.positionOS * _NoiseScale * 6.0) + floor(time * 12.0));

            // Yukarı akan tarama bandı.
            float scan = sin(input.positionOS.y * _ScanScale - time * _ScanSpeed);

            float detail = 1.0
                         + _NoiseAmount * (cloud * 2.0 - 1.0)
                         + _GrainAmount * (grain - 0.5)
                         + _ScanAmount * scan;
            detail = max(0.0, detail);

            // Yavaş nefes.
            float breathe = 1.0 + _PulseAmount * sin(time * _PulseSpeed * 6.2831853);

            // Hızlı, düzensiz titreme (kare adımlar arası yumuşatılır — sert kırpma göz yorar).
            float flickerTime = time * _FlickerSpeed;
            float flickerA = Hash13(float3(floor(flickerTime), 7.0, 13.0));
            float flickerB = Hash13(float3(floor(flickerTime) + 1.0, 7.0, 13.0));
            float flicker = 1.0 - _FlickerAmount * lerp(flickerA, flickerB, smoothstep(0.0, 1.0, frac(flickerTime)));

            float alpha = _BaseColor.a
                        * saturate(_CoreAlpha + rim * _RimBoost)
                        * detail * breathe * flicker * alphaScale;

            half3 color = _BaseColor.rgb * (1.0 + rim * 0.6 + _NoiseAmount * (cloud - 0.5));

            return half4(color, saturate(alpha));
        }
        ENDHLSL

        // Sıra önemli: cam küre doğru görünsün diye önce İÇ yüz, sonra DIŞ yüz çizilir.
        // ZWrite kapalı olduğu için sıralamayı pass sırası verir; tek pass "Cull Off" bunu garanti etmez.
        Pass
        {
            Name "GripSocketInner"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragInner
            #pragma multi_compile_instancing
            #pragma target 3.0

            half4 FragInner(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return ShadeSocket(input, _InnerAlpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "GripSocketOuter"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOuter
            #pragma multi_compile_instancing
            #pragma target 3.0

            half4 FragOuter(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return ShadeSocket(input, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
