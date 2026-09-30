Shader "VortexArena/BurgerThermometer"
{
    // Patty doneness thermometer drawn on a single quad: glass tube + bulb, a liquid column rising
    // from raw pink to cooked brown, a green "take it off" mark and a flame while it sits on the grill.
    //
    // ⚠️ BurgerPattyThermometer drives _Fill/_CookMark/_Heat/_Warn/_Burnt/_Visibility through a
    // MaterialPropertyBlock, so the values in here are EDITOR defaults only.
    //
    // ⚠️ No texture and no branches worth the name: every shape is an SDF anti-aliased with fwidth.
    // One of these rides every patty on a Quest tile GPU, and a sampled dial would also be a second
    // asset to keep in sync with the server's thresholds.
    Properties
    {
        [Header(Kod surer)]
        _Fill ("Doluluk", Range(0, 1)) = 0.3
        _CookMark ("Pişti işareti", Range(0, 1)) = 0.5
        _Heat ("Ateş", Range(0, 1)) = 0
        _Warn ("Yanma uyarısı", Range(0, 1)) = 0
        _Burnt ("Yandı", Range(0, 1)) = 0
        _Visibility ("Görünürlük", Range(0, 1)) = 1

        [Header(Bicim)]
        _Aspect ("Quad en/boy oranı", Range(0.1, 2)) = 0.5

        [Header(Renkler)]
        _GlassColor ("Cam", Color) = (1, 1, 1, 0.35)
        _OutlineColor ("Kontur", Color) = (0.08, 0.07, 0.07, 1)
        _RawColor ("Çiğ", Color) = (0.85, 0.45, 0.45, 1)
        _CookedColor ("Pişmiş", Color) = (0.55, 0.30, 0.12, 1)
        _BurntColor ("Yanmış", Color) = (0.12, 0.09, 0.08, 1)
        _ZoneColor ("Pişti bölgesi", Color) = (0.20, 0.85, 0.30, 1)
        _WarnColor ("Uyarı", Color) = (0.95, 0.15, 0.10, 1)
        _FlameColor ("Alev", Color) = (1.0, 0.55, 0.12, 1)
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

        Pass
        {
            Name "BurgerThermometer"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher contract: every material property lives in this CBUFFER.
            CBUFFER_START(UnityPerMaterial)
                float _Fill;
                float _CookMark;
                float _Heat;
                float _Warn;
                float _Burnt;
                float _Visibility;
                float _Aspect;
                float4 _GlassColor;
                float4 _OutlineColor;
                float4 _RawColor;
                float4 _CookedColor;
                float4 _BurntColor;
                float4 _ZoneColor;
                float4 _WarnColor;
                float4 _FlameColor;
            CBUFFER_END

            // Dial layout in quad UV (y = 0 bottom): bulb, then the tube it feeds.
            #define BULB_Y 0.13
            #define BULB_R 0.10
            #define TUBE_BOTTOM 0.26
            #define TUBE_TOP 0.92
            #define TUBE_R 0.055
            #define GLASS_THICK 0.030
            #define LIQUID_BOTTOM 0.24

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float SdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Inside mask of an SDF, one pixel wide edge.
            float Inside(float d, float e)
            {
                return 1.0 - smoothstep(-e, e, d);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Square the coordinates so the tube keeps its width on a non-square quad.
                float2 p = float2((input.uv.x - 0.5) * _Aspect, input.uv.y);

                float ex = max(fwidth(p.x), 1e-5);
                float ey = max(fwidth(p.y), 1e-5);

                float bulb = length(p - float2(0.0, BULB_Y)) - BULB_R;
                float tube = SdSegment(p, float2(0.0, TUBE_BOTTOM), float2(0.0, TUBE_TOP)) - TUBE_R;
                float body = min(bulb, tube);

                float e = max(fwidth(body), 1e-5);
                float glass = Inside(body, e);
                float outline = glass * smoothstep(-0.020 - e, -0.020 + e, body);

                // Liquid: the bulb is always full, the tube fills up to _Fill.
                float cavity = Inside(body + GLASS_THICK, e);
                float bulbCavity = Inside(bulb + GLASS_THICK, e);
                float top = lerp(LIQUID_BOTTOM, TUBE_TOP - 0.01, saturate(_Fill));
                float column = 1.0 - smoothstep(top - ey, top + ey, p.y);
                float liquid = cavity * saturate(max(column, bulbCavity));

                // Raw drifts toward cooked up to the mark, then stays cooked; burnt overrides.
                float cookT = _CookMark > 0.001 ? saturate(_Fill / _CookMark) : 1.0;
                half3 liquidColor = lerp(_RawColor.rgb, _CookedColor.rgb, cookT);
                liquidColor = lerp(liquidColor, _BurntColor.rgb, saturate(_Burnt));

                // ~4 Hz warning pulse: the cooked window is closing.
                float pulse = saturate(_Warn) * (0.5 + 0.5 * sin(_Time.y * 25.13));
                liquidColor = lerp(liquidColor, _WarnColor.rgb, pulse * 0.7);
                half3 outlineColor = lerp(_OutlineColor.rgb, _WarnColor.rgb, pulse);

                // Green mark at the cooked threshold, overhanging the tube on both sides.
                float markY = lerp(LIQUID_BOTTOM, TUBE_TOP - 0.01, saturate(_CookMark));
                float mark = (1.0 - smoothstep(0.010, 0.010 + 2.0 * ey, abs(p.y - markY))) *
                             (1.0 - smoothstep(0.095, 0.095 + 2.0 * ex, abs(p.x)));

                // Glass between mark and top reads as the "ready" band.
                float zone = smoothstep(markY - ey, markY + ey, p.y) *
                             (1.0 - smoothstep(TUBE_TOP - ey, TUBE_TOP + ey, p.y));

                // Flame beside the tube top while the patty is on the grill.
                float flicker = 1.0 + 0.12 * sin(_Time.y * 11.0);
                float2 fq = (p - float2(0.105, 0.855)) / (0.055 * flicker);
                fq.x *= 1.0 + 0.8 * saturate(fq.y);
                float flame = Inside(length(fq) - 1.0, 0.25) * saturate(_Heat);

                half3 color = _GlassColor.rgb;
                float alpha = glass * _GlassColor.a;

                color = lerp(color, _ZoneColor.rgb, zone * 0.25);
                color = lerp(color, liquidColor, liquid);
                alpha = max(alpha, liquid * 0.95);
                color = lerp(color, _ZoneColor.rgb, mark);
                alpha = max(alpha, mark);
                color = lerp(color, outlineColor, outline);
                alpha = max(alpha, outline);
                color = lerp(color, _FlameColor.rgb, flame);
                alpha = max(alpha, flame);

                return half4(color, saturate(alpha * saturate(_Visibility)));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
