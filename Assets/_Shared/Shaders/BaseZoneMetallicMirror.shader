// Taban bölgesi şeridi için koyu, ayna gibi yansıtan yüzey + takım rengiyle kayan enerji deseni.
//
// Tasarım notları (değiştirmeden önce oku):
//  * Desen UV'den DEĞİL dünya XZ'sinden türer: şerit built-in Plane mesh'i ve ölçeği
//    (0.1, 1, 0.83) düzgün olmadığı için UV tabanlı bir desen bir eksende ezilirdi. Dünya uzayı
//    aynı zamanda kırmızı ve mavi şeridin desenini birbiriyle hizalı tutar.
//  * _BaseColor = TAKIM RENGİ. Adı bilinçli: BaseZoneVisibility, x-ray hayaletinin rengini
//    şeridin 0. slot materyalinden `_BaseColor` (yoksa `_Color`) alanını okuyarak kopyalar
//    (Assets/_Shared/Core/Arena/BaseZoneVisibility.cs, CopyTeamColor). Yüzeyin koyu rengi bu
//    yüzden ayrı bir alanda (_MirrorColor) durur — aksi hâlde ölen oyuncunun duvar arkasından
//    gördüğü şerit simsiyah çizilirdi.
//  * Yansıma URP'nin kendi yolundan alınır (GlossyEnvironmentReflection): reflection probe
//    blending, box projection ve Forward+ probe atlası bedavaya gelir.
Shader "VortexArena/BaseZoneMetallicMirror"
{
    Properties
    {
        [Header(Ayna Yuzeyi)]
        _MirrorColor        ("Yuzey Rengi (koyu zemin)", Color) = (0.02, 0.02, 0.025, 1)
        _ReflectionColor    ("Yansima Tonu", Color) = (1, 1, 1, 1)
        _Metallic           ("Metallic", Range(0, 1)) = 1
        _Smoothness         ("Smoothness", Range(0, 1)) = 0.96
        _ReflectionStrength ("Yansima Gucu", Range(0, 4)) = 1.2
        _FresnelPower       ("Fresnel Power", Range(1, 8)) = 4
        _FresnelStrength    ("Fresnel Gucu", Range(0, 2)) = 0.6
        _SpecularStrength   ("Isik Parlamasi", Range(0, 4)) = 1

        [Header(Takim Rengi)]
        _BaseColor          ("Takim Rengi (x-ray da bunu okur)", Color) = (0.85, 0.15, 0.15, 1)
        _GlowIntensity      ("Glow Siddeti", Range(0, 20)) = 4
        _TeamTint           ("Yansimaya Renk Sizmasi", Range(0, 1)) = 0.12

        [Header(Kayan Desen)]
        _ScrollDir          ("Kayma Yonu (dunya X ve Z)", Vector) = (0, 1, 0, 0)
        _ScrollSpeed        ("Kayma Hizi (m/s)", Float) = 0.55
        _BandSpacing        ("Bant Araligi (m)", Float) = 1.6
        _BandWidth          ("Bant Kalinligi (0-1)", Range(0.01, 0.9)) = 0.16
        _BandSoftness       ("Bant Yumusakligi", Range(0.005, 0.5)) = 0.1
        _FlowStrength       ("Akis Bozulmasi (m)", Range(0, 2)) = 0.55
        _FlowScale          ("Akis Sikligi", Float) = 0.35
        _FlowSpeed          ("Akis Hizi", Float) = 0.18
        _FlowGlow           ("Akis Parlamasi", Range(0, 1)) = 0.35
        _RippleStrength     ("Yansima Dalgalanmasi", Range(0, 0.4)) = 0.05

        [Header(Kenar)]
        _EdgeGlow           ("Kenar Parlamasi", Range(0, 5)) = 0.8
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
            half4  _BaseColor;
            half   _GlowIntensity;
            half   _TeamTint;
            float4 _ScrollDir;
            float  _ScrollSpeed;
            float  _BandSpacing;
            half   _BandWidth;
            half   _BandSoftness;
            float  _FlowStrength;
            float  _FlowScale;
            float  _FlowSpeed;
            half   _FlowGlow;
            half   _RippleStrength;
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

            // Yansımanın doğru gelmesi için gereken anahtarlar (URP Lit ile aynı küme).
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

            // Dokusuz, iki sinüs katmanlı akış alanı. Quest'te doku okumaktan ucuz ve deseni
            // tekrarsız gösterecek kadar düzensiz.
            float2 FlowField(float2 worldXZ)
            {
                float2 p = worldXZ * _FlowScale;
                float t = _Time.y * _FlowSpeed;
                float2 flow;
                flow.x = sin(p.y * 1.7 + t * 1.3) + sin(p.x * 1.1 - t * 0.9);
                flow.y = cos(p.x * 1.3 - t * 1.1) + sin(p.y * 0.9 + t * 0.7);
                return flow * 0.5;
            }

            half4 MirrorFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                float3 geoNormal = normalize(input.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(positionWS));

                float2 flow = FlowField(positionWS.xz);

                // --- yansımayı dalgalandıran normal sapması (sıvı metal hissi) -------------
                float3 tangent = normalize(cross(geoNormal,
                                                 abs(geoNormal.y) > 0.99 ? float3(1, 0, 0) : float3(0, 1, 0)));
                float3 bitangent = cross(geoNormal, tangent);
                float3 normalWS = normalize(geoNormal + (tangent * flow.x + bitangent * flow.y) * _RippleStrength);

                // --- kayan bantlar (dünya XZ, metre) ---------------------------------------
                float2 dir = normalize(_ScrollDir.xy + float2(1e-5, 1e-5));
                float spacing = max(_BandSpacing, 0.01);
                float axis = dot(positionWS.xz, dir) + flow.x * _FlowStrength - _Time.y * _ScrollSpeed;
                float bandDist = abs(frac(axis / spacing) - 0.5) * 2.0;
                half band = 1.0h - smoothstep(_BandWidth, _BandWidth + _BandSoftness, bandDist);

                // Yavaş, geniş parlama lekeleri — bantlar tek başına fazla mekanik duruyor.
                half blob = saturate(flow.y * 0.5 + 0.5);
                blob = blob * blob * blob * blob;

                // --- şeridin kenar çizgisi (Plane mesh'inin UV'si 0..1) ---------------------
                float2 edgeUv = min(input.uv, 1.0 - input.uv);
                float edgeDist = min(edgeUv.x, edgeUv.y);
                half edge = 1.0h - smoothstep(0.0h, max(_EdgeWidth, 1e-4h), (half)edgeDist);

                half glowMask = saturate(band + blob * _FlowGlow);
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
                // Metallic = "her açıdan ayna", fresnel = "sıyırma açısında ayna". Dielektrik
                // taban 0.04 olduğu için metallic 0'da yüzey yalnız kenarlarda parlar.
                half reflectMask = (lerp(0.04h, 1.0h, _Metallic) + fresnel) * _ReflectionStrength;
                half3 reflectTint = lerp(half3(1, 1, 1), _BaseColor.rgb, _TeamTint) * _ReflectionColor.rgb;
                half3 reflection = environment * reflectMask * reflectTint;

                // --- ana ışık: keskin parlama + (metalik olmayan payda) sönük difüz ---------
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
