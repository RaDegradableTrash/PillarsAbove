Shader "PillarsAbove/StratifiedPillar"
{
    Properties
    {
        _BottomColor ("Bottom Stone", Color) = (0.25, 0.26, 0.27, 1)
        _MiddleColor ("Middle Stone", Color) = (0.46, 0.40, 0.33, 1)
        _TopColor ("Top Stone", Color) = (0.64, 0.54, 0.40, 1)
        _GradientBottom ("Gradient Bottom Height", Float) = -24
        _GradientMiddle ("Gradient Middle Height", Float) = 46
        _GradientTop ("Gradient Top Height", Float) = 136
        _DrySmoothness ("Dry Stone Smoothness", Range(0, 1)) = 0.11
        _WetSmoothness ("Wet Stone Smoothness", Range(0, 1)) = 0.46
        _WetLineHeight ("Wet Line Height", Float) = 5
        _WetFadeDistance ("Wet Fade Distance", Range(1, 40)) = 18
        _StoneSpecular ("Stone Reflection Color", Color) = (0.08, 0.09, 0.10, 1)
        _WetSpecular ("Wet Reflection Color", Color) = (0.30, 0.36, 0.40, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _BottomColor;
        fixed4 _MiddleColor;
        fixed4 _TopColor;
        float _GradientBottom;
        float _GradientMiddle;
        float _GradientTop;
        float _DrySmoothness;
        float _WetSmoothness;
        float _WetLineHeight;
        float _WetFadeDistance;
        fixed4 _StoneSpecular;
        fixed4 _WetSpecular;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            float lowerGradient = smoothstep(
                _GradientBottom,
                max(_GradientBottom + 0.001, _GradientMiddle),
                IN.worldPos.y);
            float upperGradient = smoothstep(
                _GradientMiddle,
                max(_GradientMiddle + 0.001, _GradientTop),
                IN.worldPos.y);

            fixed3 stoneColor = lerp(_BottomColor.rgb, _MiddleColor.rgb, lowerGradient);
            stoneColor = lerp(stoneColor, _TopColor.rgb, upperGradient);

            float wetness = 1.0 - smoothstep(
                _WetLineHeight,
                _WetLineHeight + max(1.0, _WetFadeDistance),
                IN.worldPos.y);
            stoneColor *= lerp(1.0, 0.82, wetness);

            o.Albedo = stoneColor;
            o.Emission = 0.0;
            o.Specular = lerp(_StoneSpecular.rgb, _WetSpecular.rgb, wetness);
            o.Smoothness = lerp(_DrySmoothness, _WetSmoothness, wetness);
            o.Occlusion = 1.0;
            o.Alpha = 1.0;
        }
        ENDCG
    }

    Fallback "Diffuse"
}
