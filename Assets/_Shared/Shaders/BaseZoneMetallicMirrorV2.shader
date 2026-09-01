// Taban bölgesi şeridi: koyu, ayna gibi yansıtan yüzey + takım rengiyle akan SIVI desen.
//
// Sıvı hissini üreten üç şey:
//  1. Yükseklik alanı üç FARKLI yöne giden dalganın toplamıdır. Tek yönlü dalga "kayan çizgi"
//     gibi okunur; üçü birden yönü belirsizleştirir ve yüzey akıyormuş gibi görünür.
//  2. Yansımayı bozan normal, o yükseklik alanının ANALİTİK eğiminden gelir (sinüsün türevi
//     kosinüstür). Sıvıyı satan şey desenin kendisi değil, aynanın dalgalanmasıdır.
//  3. Parlayan damarlar dünya eksenine değil YÜKSEKLİĞE göre kesilir — yani eş yükselti
//     eğrileridir. Düz bir eksene göre kesilmiş bant mekanik durur; kontur akışla kıvrılır.
//
// Desen koordinatı dünya XZ'si DEĞİL, yüzeyin kendi teğet düzlemidir: materyal düz zemine de,
// dik bir duvara da, küreye de sürülebilsin diye. Düz zeminde ikisi zaten aynı şeye iner.
//
// ⚠️ _BaseColor = TAKIM RENGİ: BaseZoneVisibility, ölen oyuncunun
// duvar-arkası şeridinin rengini şeridin materyalinden bu alanı okuyarak kopyalıyor. Yüzeyin
// koyu rengi ayrı alanda (_MirrorColor).
Shader "VortexArena/BaseZoneMetallicMirrorV2"
{
    Properties
    {
        [Header(Ayna Yuzeyi)]
        _MirrorColor        ("Yuzey Rengi (koyu zemin)", Color) = (0.54, 0, 0, 1)
        _ReflectionColor    ("Yansima Tonu", Color) = (0.42, 0, 0, 1)
        _Metallic           ("Metallic", Range(0, 1)) = 1
        _Smoothness         ("Smoothness", Range(0, 1)) = 1
        _ReflectionStrength ("Yansima Gucu", Range(0, 4)) = 1
        _FresnelPower       ("Fresnel Power", Range(1, 8)) = 4
        _FresnelStrength    ("Fresnel Gucu", Range(0, 2)) = 0.35
        _SpecularStrength   ("Isik Parlamasi", Range(0, 4)) = 0.1

        [Header(Sivi Yuzey)]
        _FlowSpeed          ("Akis Hizi", Float) = 0.35
        _WaveScale          ("Dalga Sikligi (1/m)", Float) = 1.2
        _WaveStrength       ("Yuzey Dalgalanmasi", Range(0, 1)) = 0.12
        _SwirlStrength      ("Girdap (buyuk dalga kucugu surukler)", Range(0, 3)) = 0.6
        _DetailScale        ("Ayrinti Sikligi", Float) = 2.6
        _DetailStrength     ("Ayrinti Gucu", Range(0, 1)) = 0.45

        [Header(Akan Damarlar)]
        _BaseColor          ("Takim Rengi (x-ray da bunu okur)", Color) = (0.3, 0, 0, 1)
        _GlowIntensity      ("Glow Siddeti", Range(0, 20)) = 20
        _VeinScale          ("Damar Sikligi", Float) = 1.4
        _VeinWidth          ("Damar Kalinligi", Range(0.01, 0.9)) = 0.12
        _VeinSoftness       ("Damar Yumusakligi", Range(0.005, 0.5)) = 0.12
        _GlowFlowSpeed      ("Damar Akis Hizi", Float) = 0.15
        _DepthGlow          ("Cukur Parlamasi", Range(0, 2)) = 0.35
        _TeamTint           ("Yansimaya Renk Sizmasi", Range(0, 1)) = 0.2

        [Header(Kenar)]
        _EdgeGlow           ("Kenar Parlamasi", Range(0, 5)) = 1
        _EdgeWidth          ("Kenar Kalinligi (UV)", Range(0.001, 0.5)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _MirrorColor;
            half4  _ReflectionColor;
            half   _Metallic;
            half   _Smoothness;
            half   _ReflectionStrength;
            half   _FresnelPower;
            half   _FresnelStrength;
            half   _SpecularStrength;
            float  _FlowSpeed;
            float  _WaveScale;
            half   _WaveStrength;
            float  _SwirlStrength;
            float  _DetailScale;
            half   _DetailStrength;
            half4  _BaseColor;
            half   _GlowIntensity;
            float  _VeinScale;
            half   _VeinWidth;
            half   _VeinSoftness;
            float  _GlowFlowSpeed;
            half   _DepthGlow;
            half   _TeamTint;
            half   _EdgeGlow;
            half   _EdgeWidth;
        CBUFFER_END
        ENDHLSL

        // ------------------------------------------------------------------ ana çizim
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MirrorVert
            #pragma fragment MirrorFrag

            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings MirrorVert(Attributes input)
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
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // Üç yöne giden dalganın toplamı + TAM eğimi. Eğim sonlu farkla değil türevle alınır,
            // yani dalgalanan normal ek örnekleme maliyeti OLMADAN gelir.
            float Swell(float2 p, float t, out float2 grad)
            {
                float2 d0 = float2( 0.98,  0.19);
                float2 d1 = float2(-0.42,  0.91);
                float2 d2 = float2( 0.62, -0.78);

                float a0 = dot(p, d0) + t * 0.90;
                float a1 = dot(p, d1) - t * 0.63;
                float a2 = dot(p, d2) + t * 1.27;

                grad = d0 * (cos(a0) * 0.50)
                     + d1 * (cos(a1) * 0.35)
                     + d2 * (cos(a2) * 0.25);

                return sin(a0) * 0.50 + sin(a1) * 0.35 + sin(a2) * 0.25;
            }

            half4 MirrorFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                float3 geoNormal = normalize(input.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(positionWS));

                // Yüzeyin kendi teğet düzlemi — desen zeminde de duvarda da aynı ölçüde okunur.
                float3 tangent = normalize(cross(geoNormal,
                                                 abs(geoNormal.y) > 0.99 ? float3(1, 0, 0) : float3(0, 1, 0)));
                float3 bitangent = cross(geoNormal, tangent);
                float2 p = float2(dot(positionWS, tangent), dot(positionWS, bitangent));

                float t = _Time.y * _FlowSpeed;

                // 1. katman: büyük, yavaş kabarma
                float2 swellGrad;
                float swell = Swell(p * _WaveScale, t, swellGrad);
                swellGrad *= _WaveScale;

                // 2. katman: büyük dalganın EĞİMİ tarafından sürüklenen (domain warp) ince
                // ayrıntı — girdap hissi buradan gelir.
                float2 detailGrad;
                float detail = Swell(p * _DetailScale + swellGrad * _SwirlStrength, t * 1.7, detailGrad);
                detailGrad *= _DetailScale;

                float height = swell + detail * _DetailStrength;
                float2 grad = swellGrad + detailGrad * _DetailStrength;

                // Dalgalanan ayna: yükseklik alanının eğimi normali eğer.
                float3 normalWS = normalize(geoNormal - (tangent * grad.x + bitangent * grad.y) * _WaveStrength);

                // Damarlar = yüzeyin EŞ YÜKSELTİ eğrileri; akışla birlikte kıvrılırlar.
                float contour = height * _VeinScale - _Time.y * _GlowFlowSpeed;
                float ridge = abs(frac(contour) - 0.5) * 2.0;
                half vein = 1.0h - smoothstep(_VeinWidth, _VeinWidth + _VeinSoftness, ridge);

                // Çukurlarda biriken parlaklık — erimiş metal plakaların arası.
                half trough = saturate(0.5h - height * 0.5h);
                trough = trough * trough * trough;

                float2 edgeUv = min(input.uv, 1.0 - input.uv);
                float edgeDist = min(edgeUv.x, edgeUv.y);
                half edge = 1.0h - smoothstep(0.0h, max(_EdgeWidth, 1e-4h), (half)edgeDist);

                half glowMask = saturate(vein + trough * _DepthGlow);
                half3 glow = _BaseColor.rgb * (glowMask * _GlowIntensity + edge * _EdgeGlow);

                // --- ayna yansıması --------------------------------------------------------
                half perceptualRoughness = 1.0h - _Smoothness;
                float3 reflectVector = reflect(-viewDir, normalWS);
                half3 environment = GlossyEnvironmentReflection(reflectVector,
                                                                positionWS,
                                                                perceptualRoughness,
                                                                1.0h,
                                                                GetNormalizedScreenSpaceUV(input.positionCS));

                half nv = saturate(dot(normalWS, viewDir));
                half fresnel = _FresnelStrength * pow(1.0h - nv, _FresnelPower);
                half reflectMask = (lerp(0.04h, 1.0h, _Metallic) + fresnel) * _ReflectionStrength;
                half3 reflectTint = lerp(half3(1, 1, 1), _BaseColor.rgb, _TeamTint) * _ReflectionColor.rgb;
                half3 reflection = environment * reflectMask * reflectTint;

                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation;
                half3 specular = LightingSpecular(lightColor, mainLight.direction, normalWS, viewDir,
                                                  half4(reflectTint, 1.0h), _Smoothness) * _SpecularStrength;
                half3 diffuse = _MirrorColor.rgb * (1.0h - _Metallic) * lightColor
                                * saturate(dot(normalWS, mainLight.direction));

                half3 color = _MirrorColor.rgb * 0.25h + diffuse + reflection + specular + glow;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ gölge
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ derinlik
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
