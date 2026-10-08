// Animated skybox: cubemap / 360 panorama / gradient base + slow rotation + wind-driven clouds.
// ⚠️ CGPROGRAM + UnityCG like the built-in skyboxes: URP draws RenderSettings.skybox via the legacy path.
// ⚠️ Speed 0 + cloud opacity 0 must match built-in Skybox/Cubemap|Panoramic exactly (math copied).
Shader "VortexArena/AnimatedSkybox"
{
    Properties
    {
        [KeywordEnum(Cubemap, Panoramic, Gradient)] _SkySource ("Gökyüzü kaynağı", Float) = 0

        // Built-in skybox property names: swapping an existing sky material's shader keeps its values.
        _Tint ("Renk tonu", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Pozlama", Range(0, 8)) = 1.0
        _Rotation ("Dönüş (derece)", Range(0, 360)) = 0
        [NoScaleOffset] _Tex ("Cubemap (HDR)", Cube) = "grey" {}
        [NoScaleOffset] _MainTex ("Panorama 360 (HDR)", 2D) = "grey" {}

        _RotationSpeed ("Dönüş hızı (derece/dk)", Range(0, 10)) = 1

        [Header(Gradyan)]
        _SkyTopColor ("Tepe rengi", Color) = (0.35, 0.52, 0.80, 1)
        _SkyHorizonColor ("Ufuk rengi", Color) = (0.78, 0.85, 0.92, 1)
        _SkyGroundColor ("Zemin rengi", Color) = (0.36, 0.34, 0.32, 1)
        _HorizonBlend ("Ufuk geçişi", Range(0.05, 4)) = 0.6

        [Header(Bulutlar)]
        [NoScaleOffset] _CloudTex ("Bulut gürültüsü (R)", 2D) = "black" {}
        _CloudColor ("Bulut rengi", Color) = (1, 1, 1, 1)
        _CloudOpacity ("Bulut opaklığı (0 = kapalı)", Range(0, 1)) = 0.6
        _CloudCoverage ("Bulut eşiği (yüksek = az bulut)", Range(0, 1)) = 0.45
        _CloudSoftness ("Bulut yumuşaklığı", Range(0.01, 1)) = 0.3
        _CloudScale ("Bulut ölçeği", Float) = 0.6
        _CloudDetailScale ("Detay ölçeği", Float) = 2.7
        _CloudCurvature ("Kubbe eğriliği", Range(0.05, 1)) = 0.25
        _CloudWind ("Rüzgâr (XY yön, Z hız uv/dk, W detay hız çarpanı)", Vector) = (1, 0.35, 0.03, 1.6)
        _CloudHorizonFade ("Ufukta sönme", Range(0.01, 1)) = 0.25
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0 // ddx + tex2Dgrad
            // Single keyword set on purpose: variants multiply on Quest, so clouds have no on/off keyword.
            #pragma shader_feature_local _SKYSOURCE_CUBEMAP _SKYSOURCE_PANORAMIC _SKYSOURCE_GRADIENT

            #include "UnityCG.cginc"

            samplerCUBE _Tex;
            half4 _Tex_HDR;

            sampler2D _MainTex;
            half4 _MainTex_HDR;

            half4 _Tint;
            half _Exposure;
            float _Rotation;
            float _RotationSpeed;

            half4 _SkyTopColor;
            half4 _SkyHorizonColor;
            half4 _SkyGroundColor;
            half _HorizonBlend;

            sampler2D _CloudTex;
            half4 _CloudColor;
            half _CloudOpacity;
            half _CloudCoverage;
            half _CloudSoftness;
            float _CloudScale;
            float _CloudDetailScale;
            float _CloudCurvature;
            float4 _CloudWind;
            half _CloudHorizonFade;

            // Built-in skybox rotation, copied verbatim so a material keeps its look after the swap.
            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            // Latitude-longitude 360 mapping (mirror-off layout, the only one we author).
            inline float2 ToRadialCoords(float3 coords)
            {
                float3 n = normalize(coords);
                float latitude = acos(n.y);
                float longitude = atan2(n.z, n.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 skyDir : TEXCOORD0;   // unrotated, like built-in
                float3 worldDir : TEXCOORD1; // rotated = actual view direction, for clouds
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                // ⚠️ Mandatory on Quest (single-pass instanced): without these macros one eye draws
                // with the other eye's matrices.
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // fmod keeps the animated angle bounded; _Time.y grows over the whole session.
                float angle = _Rotation + fmod(_Time.y * _RotationSpeed / 60.0, 360.0);
                float3 rotated = RotateAroundYInDegrees(v.vertex.xyz, angle);

                o.vertex = UnityObjectToClipPos(rotated);
                // Built-in passes the UNROTATED position as lookup direction: rotating the vertex and
                // sampling the original direction is what turns the sky.
                o.skyDir = v.vertex.xyz;
                // Clouds must NOT turn with the sky (they move by wind), so they use the rotated
                // position, which equals the real view direction.
                o.worldDir = rotated;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = i.skyDir;
                half3 c;

            #if defined(_SKYSOURCE_GRADIENT)
                float y = normalize(dir).y;
                c = (y >= 0.0)
                    ? lerp(_SkyHorizonColor.rgb, _SkyTopColor.rgb, pow(saturate(y), _HorizonBlend))
                    : lerp(_SkyHorizonColor.rgb, _SkyGroundColor.rgb, pow(saturate(-y), _HorizonBlend));
                // Authored colors are final: no tint, only exposure.
                c *= _Exposure;
            #elif defined(_SKYSOURCE_PANORAMIC)
                float2 uv = ToRadialCoords(dir);
                // Seam fix: uv.x jumps 1 -> 0 at the atan2 wrap, so the automatic derivative explodes
                // there and the mip selection draws a visible line. Take the derivative of a
                // half-shifted copy too and use whichever pair is shorter.
                float2 uvShift = float2(frac(uv.x + 0.5) - 0.5, uv.y);
                float2 dx = ddx(uv), dy = ddy(uv);
                float2 dxS = ddx(uvShift), dyS = ddy(uvShift);
                if (dot(dxS, dxS) + dot(dyS, dyS) < dot(dx, dx) + dot(dy, dy))
                {
                    dx = dxS;
                    dy = dyS;
                }
                half4 tex = tex2Dgrad(_MainTex, uv, dx, dy);
                c = DecodeHDR(tex, _MainTex_HDR);
                c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb;
                c *= _Exposure;
            #else
                half4 tex = texCUBE(_Tex, dir);
                c = DecodeHDR(tex, _Tex_HDR);
                c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb;
                c *= _Exposure;
            #endif

                // Cloud sheet: a flat plane read through a dome projection, so it thickens towards the
                // horizon like real cloud cover.
                float3 w = normalize(i.worldDir);
                float2 p = w.xz / (max(w.y, 0) + _CloudCurvature);
                float t = _Time.y / 60.0; // minutes
                float2 wind = normalize(_CloudWind.xy + 1e-5) * _CloudWind.z;
                // frac keeps UV precision over long sessions; the noise texture tiles at 1.
                float2 off1 = frac(wind * t);
                // Detail drifts ~30 deg off the main direction so the sheet evolves instead of
                // sliding rigidly.
                float2 windDetail = float2(wind.x * 0.866 - wind.y * 0.5, wind.x * 0.5 + wind.y * 0.866);
                float2 off2 = frac(windDetail * _CloudWind.w * t);
                half n = tex2D(_CloudTex, p * _CloudScale + off1).r * 0.7
                       + tex2D(_CloudTex, p * _CloudScale * _CloudDetailScale + off2).r * 0.3;
                // ⚠️ Horizon fade is a VR COMFORT requirement, not a look choice: a large moving field
                // low in the view causes motion sickness. Clouds exist only above the horizon.
                half density = smoothstep(_CloudCoverage, _CloudCoverage + _CloudSoftness, n)
                             * _CloudOpacity * smoothstep(0, _CloudHorizonFade, w.y);
                c = lerp(c, _CloudColor.rgb * _Exposure, density);

                return half4(c, 1);
            }
            ENDCG
        }
    }

    // No fallback: a pink sky is better than silently falling back to a different sky model.
    Fallback Off
}
