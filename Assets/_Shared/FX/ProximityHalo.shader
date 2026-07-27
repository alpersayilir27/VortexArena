// Yakınlık uyarısı halkası: DUVAR ARKASINDAN da görünmesi gerektiği için ZTest Always.
// Free-roam'da tehlike sanal engelin arkasındaki GERÇEK bedendir; oklüzyon burada
// güvenliği bozar, o yüzden bilinçli olarak derinlik testi kapalıdır.
Shader "VortexArena/ProximityHalo"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.45, 0.12, 1)
        _Softness  ("Ring Softness", Range(0.05, 1)) = 0.45
        _Radius    ("Ring Radius", Range(0.1, 1)) = 0.72
    }

    SubShader
    {
        Tags
        {
            "RenderType"       = "Transparent"
            "Queue"            = "Overlay"
            "RenderPipeline"   = "UniversalPipeline"
            "IgnoreProjector"  = "True"
        }

        Pass
        {
            Name "ProximityHalo"
            Blend One One          // eklemeli: karanlık buz arenasında okunur kalır
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Softness;
                float  _Radius;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Merkezden dışa doğru 0..1 yarıçap, ortası boş bir halka çiz.
                float2 delta = IN.uv - 0.5;
                float  r     = length(delta) * 2.0;
                float  ring  = saturate(1.0 - abs(r - _Radius) / max(_Softness, 1e-4));
                float  a     = ring * ring * _BaseColor.a;
                return half4(_BaseColor.rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
