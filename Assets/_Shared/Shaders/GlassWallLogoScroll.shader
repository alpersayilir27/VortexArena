// Transparent glass / energy wall with a brand logo printed on top that SCROLLS HORIZONTALLY.
// Scrolling variant of `VortexArena/GlassWallLogo`: the only difference is that the logo walks
// left at a constant speed, the whole glass/rim/cut-out behaviour is identical. The two shaders
// stay SEPARATE because part of the walls want a static logo; adding a speed field to the existing
// shader would carry a silent animation risk into every material.
//
// ⚠️ WRITTEN AGAINST QUEST'S REAL CONSTRAINTS (Mobile_RPAsset), and those constraints dictate the
// shape of this file: **depth texture OFF** and **opaque texture OFF** → soft intersection with the
// geometry behind, frosted-glass blur and refraction CANNOT be written; all of them need Scene
// Depth / Scene Color. **HDR OFF** → brightness clips at 1.0, there is no bloom, so the "the glass
// glows" feel is produced by rim (fresnel) contrast, NOT by a bigger emission. Both are enabled in
// the editor (PC_RPAsset), so a setting that looks good here can come out silently dull in the
// headset — the verdict is always given on the APK (Docs/Sistem-Ozeti.md §7).
//
// ⚠️ Unlit, and it STAYS unlit: a glass wall is a coating, not a surface for scene light to darken.
//
// ⚠️ Cull Off: the quad is a SINGLE-sided mesh — culled, a player looking from behind would not see
// the wall at all and the wall would disappear from one side.
//
// ⚠️ The logo source may be a JPG (no alpha channel, white background). DEFAULT behaviour is cutting
// the white background; when a PNG that has alpha is bound, "logo has its own alpha" is checked,
// otherwise the mask is applied twice and the logo thins out.
// ⚠️ The keyword default is deliberately INVERTED (white is cut while the keyword is absent): in
// Unity a [Toggle] property's default value of 1 still does NOT enable the shader keyword — the
// keyword is only set when clicked in the Inspector. Had the default behaviour hung on the keyword,
// it would stay silently off in every material built from code or from a tool.
//
// ⚠️ Scrolling is periodic ON THE WALL ITSELF: in center mode too, the logo leaving the left edge
// re-enters from the right (in tiled mode it is already continuous via frac). This has a visible
// side effect — if a logo is placed near an edge and scaled up enough to overflow, the overflowing
// part shows on the opposite edge; this holds even when the scroll speed is 0.
Shader "VortexArena/GlassWallLogoScroll"
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
        // Center mode: how many equally spaced copies fit on the wall, the logo KEEPS its _LogoSize
        // (1 = today's single logo). Tiled mode: tiling density multiplier, so the logo shrinks by
        // the same factor. Below 1 is clamped to 1; Z/W unused.
        _LogoRepeat ("Logo tekrar sayısı (X, Y)", Vector) = (1, 1, 0, 0)

        [Header(Kaydirma)]
        // Unit: LOGO WIDTH per second. 0.5 = the logo walks its own width to the left in two seconds.
        // The measure is in logo units, so the scroll feel is not broken when _LogoSize changes.
        // Positive = to the left, negative = to the right, 0 = static (same image as the static shader).
        _ScrollSpeed ("Logo kayma hızı (saniyede logo boyu, + = sola)", Float) = 0.5

        [Header(Logo zemini)]
        // Unchecked = white background is cut (JPG without alpha). Checked = the texture's own alpha is used.
        [Toggle(_LOGO_ALPHA_CHANNEL)] _LogoHasAlpha ("Logonun kendi alfası var (PNG)", Float) = 0
        _LogoCut ("Beyaz kesme eşiği", Range(0, 1)) = 0.06

        [Header(Ileri)]
        // 0 = automatic (from the object's world scale). ⚠️ If the quad is marked "Static" the mesh is
        // baked into world space, unity_ObjectToWorld falls back to identity and the automatic ratio
        // comes out 1 — the logo is squashed. In that case type width ÷ height here by hand.
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
            Name "GlassWallScroll"
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

            // Softening band of the cut mask. Fixed: wide enough to soften the antialiased pixels on
            // the logo edge, narrow enough not to eat thin letters. Not exposed as a field — a second
            // threshold field would mean the two thresholds silently drifting apart.
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

            // For SRP Batcher compatibility ALL numeric properties live in this block and all of them
            // are float (same reason as CharacterShieldV2/AvatarGhost: a mixed half/float layout drops
            // the batcher). The float counterparts of the toggles must be here too — a field that is in
            // the Properties block but not in the CBUFFER breaks batcher compatibility.
            CBUFFER_START(UnityPerMaterial)
                float4 _GlassColor;
                float  _RimPower;
                float  _RimStrength;
                float  _LogoStrength;
                float  _LogoSize;
                float4 _LogoCenter;
                float  _LogoTiled;
                float4 _LogoRepeat;
                float  _ScrollSpeed;
                float  _LogoHasAlpha;
                float  _LogoCut;
                float  _LogoAspect;
            CBUFFER_END

            // The wall's aspect ratio is read from the object's WORLD scale: the logo is square, the
            // panel is not. Without the ratio a square logo would be squashed by the panel's ratio.
            float ObjectAspect()
            {
                float3 axisX = float3(unity_ObjectToWorld[0][0], unity_ObjectToWorld[1][0], unity_ObjectToWorld[2][0]);
                float3 axisY = float3(unity_ObjectToWorld[0][1], unity_ObjectToWorld[1][1], unity_ObjectToWorld[2][1]);
                return length(axisX) / max(length(axisY), 1e-4);
            }

            // Folds a distance into a single repeat slot: copies land every `period`. An EVEN count is
            // offset by half a slot, otherwise one of the copies would sit exactly on the wall's wrap
            // seam and be drawn as two halves on the opposite edges. count = 1 → offset 0, so the
            // single-logo case folds over the whole wall exactly as before.
            float FoldRepeat(float d, float period, float count)
            {
                d += (fmod(count, 2.0) < 0.5) ? period * 0.5 : 0.0;
                return (frac(d / period + 0.5) - 0.5) * period;
            }

            // The logo UV is DERIVED from the wall UV, not from the texture's own Tiling/Offset
            // ([NoScaleOffset]): size and center are described by two metrically meaningful fields, so
            // the same placement cannot be written from two different places.
            float2 LogoUV(float2 uv, float aspect, out float box)
            {
                float size = max(_LogoSize, 1e-4);
                float2 sizeUV = float2(size / max(aspect, 1e-4), size);
                float2 count = max(_LogoRepeat.xy, 1.0);
                float2 luv = (uv - _LogoCenter.xy) / sizeUV + 0.5;

                // Scroll: as the SAMPLED x grows the image moves LEFT — hence a positive speed flows
                // left. The offset is added in logo units (in the space divided by sizeUV), so the speed
                // is independent of _LogoSize. ⚠️ _Time.y grows over the whole session; frac loses
                // precision in very long sessions (the scroll may jitter) — the cure is not a bigger
                // speed but rebuilding the material if needed.
                luv.x += _Time.y * _ScrollSpeed;

            #if defined(_LOGO_TILED)
                box = 1.0;
                // count multiplies the tiling frequency, so the logo shrinks by the same factor. The
                // scroll offset is scaled along with it, so the speed on the wall stays unchanged.
                // ⚠️ Different X and Y counts stretch the logo here (the tiles are contiguous).
                return frac(luv * count);
            #else
                // In center mode THE WALL ITSELF is periodic: so that a logo leaving the left edge
                // re-enters from the right, x is folded into an interval as wide as the wall's width in
                // logo units (1 / sizeUV.x). Dividing that interval by count places `count` copies side
                // by side at equal spacing while the logo keeps its size; the gap between them is cut by
                // the box mask below. The interval is picked symmetrically around the center; with count
                // 1, speed 0 and the logo centered the fold does nothing and the image is identical to
                // the static-logo shader.
                float2 period = 1.0 / max(sizeUV * count, 1e-4);
                luv.x = FoldRepeat(luv.x - 0.5, period.x, count.x) + 0.5;

                // Vertical stacking only when asked: a single copy must keep the current behaviour of
                // NOT wrapping over the top/bottom edge.
                if (count.y > 1.0)
                {
                    luv.y = FoldRepeat(luv.y - 0.5, period.y, count.y) + 0.5;
                }

                // In center mode outside the logo is NEVER sampled: if the texture's Wrap Mode is left
                // at Repeat, the outside would fill up with copies of the logo.
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

                // The glass base opens up towards the edge (fresnel). ⚠️ abs(): in double-sided drawing
                // the back faces' normal points AWAY from the camera, without abs the back of the wall
                // would go completely dark (the same trap as in AvatarGhost/CharacterShieldV2).
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
                // The white background is cut with a mask derived from the DARKEST channel. min(r,g,b)
                // is picked because the logo's turquoise and orange are saturated, so they come out near
                // 1 while white gives 0. Cutting by luminance would eat the light orange too.
                float ink = 1.0 - min(min(logo.r, logo.g), logo.b);
                float logoAlpha = smoothstep(_LogoCut, _LogoCut + VA_LOGO_EDGE, ink);
            #endif

                logoAlpha *= _LogoStrength * box;

                // The logo is printed ON TOP of the glass: it suppresses both the color and the
                // transparency as much as its strength. At _LogoStrength = 1 the logo is opaque, at 0
                // the wall is clean glass.
                rgb = lerp(rgb, logo.rgb, logoAlpha);
                alpha = lerp(alpha, 1.0, logoAlpha);

                rgb = MixFog(rgb, input.fogFactor);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    // NO fallback (same reason as AvatarGhost/CharacterShieldV2): drawing pink is better than silently
    // drawing an opaque wall and hiding half the arena.
    Fallback Off
}
