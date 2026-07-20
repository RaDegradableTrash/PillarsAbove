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
        _DynamicWaterClip ("Dynamic Water Clip", Float) = 0
        _WaterLevel ("Water Level", Float) = 0
        _WaterEdgeSink ("Water Edge Sink", Float) = 0.035
        _WaterUvMin ("Water UV World Min", Vector) = (0, 0, 0, 0)
        _WaterUvSize ("Water UV World Size", Vector) = (1, 1, 0, 0)
        _WaveAmplitude ("Wave Amplitude", Float) = 0.22
        _WaveSpeed ("Wave Speed", Float) = 0.72
        _PrimaryWaveLength ("Primary Wave Length", Float) = 7.5
        _SecondaryWaveLength ("Secondary Wave Length", Float) = 3.2
        _SimulationTex ("Interactive Ripple Texture", 2D) = "black" {}
        _Displacement ("Interactive Ripple Displacement", Float) = 0.65
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
        float _DynamicWaterClip;
        float _WaterLevel;
        float _WaterEdgeSink;
        float4 _WaterUvMin;
        float4 _WaterUvSize;
        float _WaveAmplitude;
        float _WaveSpeed;
        float _PrimaryWaveLength;
        float _SecondaryWaveLength;
        sampler2D _SimulationTex;
        float _Displacement;
        static const float TwoPi = 6.28318530718;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void AddVerticalWaterWave(inout float waterY, float2 sampleXZ, float2 direction, float length, float amplitude, float speedScale)
        {
            float2 dir = normalize(direction);
            float safeLength = max(length, 0.001);
            float waveNumber = TwoPi / safeLength;
            float phase = dot(sampleXZ, dir) * waveNumber + _Time.y * _WaveSpeed * speedScale;
            waterY += amplitude * sin(phase);
        }

        float2 WaterWorldUv(float3 worldPosition)
        {
            return saturate((worldPosition.xz - _WaterUvMin.xy) / max(_WaterUvSize.xy, float2(0.001, 0.001)));
        }

        float DynamicWaterY(float3 worldPosition)
        {
            float waterY = _WaterLevel;
            AddVerticalWaterWave(waterY, worldPosition.xz, float2(0.91, 0.42), _PrimaryWaveLength * 1.22, _WaveAmplitude * 0.34, 0.74);
            AddVerticalWaterWave(waterY, worldPosition.xz, float2(-0.72, 0.69), _PrimaryWaveLength * 0.73, _WaveAmplitude * 0.23, 0.96);
            AddVerticalWaterWave(waterY, worldPosition.xz, float2(0.25, 0.97), _SecondaryWaveLength * 1.34, _WaveAmplitude * 0.13, 1.24);
            AddVerticalWaterWave(waterY, worldPosition.xz, float2(-0.93, -0.36), _SecondaryWaveLength * 0.86, _WaveAmplitude * 0.07, 1.54);

            float radialDistance = length(worldPosition.xz);
            float radialEnvelope = 1.0 - smoothstep(18.0, 62.0, radialDistance);
            float radialPhaseA = radialDistance * 0.46 - _Time.y * _WaveSpeed * 1.28;
            float radialPhaseB = radialDistance * 0.25 - _Time.y * _WaveSpeed * 0.76 + 1.7;
            waterY += (sin(radialPhaseA) * 0.72 + sin(radialPhaseB) * 0.28)
                * _WaveAmplitude * 0.20 * radialEnvelope;
            waterY += tex2D(_SimulationTex, WaterWorldUv(worldPosition)).r * _Displacement;
            return waterY;
        }

        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            float dynamicWaterY = DynamicWaterY(IN.worldPos);
            if (_DynamicWaterClip > 0.5)
            {
                clip(IN.worldPos.y - dynamicWaterY + _WaterEdgeSink);
            }

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
                dynamicWaterY,
                dynamicWaterY + max(1.0, _WetFadeDistance),
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
