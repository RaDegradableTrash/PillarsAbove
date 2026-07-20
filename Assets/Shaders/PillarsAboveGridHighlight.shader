Shader "PillarsAbove/GridHighlight"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _EmissionColor ("Emission", Color) = (1.15, 1.15, 1.15, 0.45)
        _HighlightCenter ("Highlight Center", Vector) = (0, 0, 0, 1)
        _HighlightRadius ("Highlight Radius", Float) = 1
        _HighlightFadeWidth ("Highlight Fade Width", Float) = 0.75
        _Alpha ("Alpha", Range(0, 1)) = 1
        _UseWorldDistance ("Use World Distance", Float) = 1
        _WaterMode ("Water Mode", Float) = 0
        _ClipBelowDynamicWater ("Clip Below Dynamic Water", Float) = 0
        _WaterLevel ("Water Level", Float) = 0
        _WaterClipSoftness ("Water Clip Softness", Float) = 0.006
        _UseClipRipple ("Use Ripple In Clip", Float) = 0
        _WaterUvMin ("Water UV World Min", Vector) = (0, 0, 0, 0)
        _WaterUvSize ("Water UV World Size", Vector) = (1, 1, 0, 0)
        _AnchorBottomToDynamicWater ("Anchor Bottom To Dynamic Water", Float) = 0
        _DynamicWaterAnchorRange ("Dynamic Water Anchor Range", Float) = 0.08
        _DynamicWaterAnchorOffset ("Dynamic Water Anchor Offset", Float) = 0.006

        _WaveAmplitude ("Wave Amplitude", Float) = 0.22
        _WaveSpeed ("Wave Speed", Float) = 0.72
        _PrimaryWaveLength ("Primary Wave Length", Float) = 7.5
        _SecondaryWaveLength ("Secondary Wave Length", Float) = 3.2
        _GerstnerSteepness ("Wave Steepness", Range(0, 1)) = 0.48
        _SimulationTex ("Interactive Ripple Texture", 2D) = "black" {}
        _Displacement ("Interactive Ripple Displacement", Float) = 0.65
        _NormalStrength ("Interactive Ripple Normal Strength", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _EmissionColor;
            float4 _HighlightCenter;
            float _HighlightRadius;
            float _HighlightFadeWidth;
            float _Alpha;
            float _UseWorldDistance;
            float _WaterMode;
            float _ClipBelowDynamicWater;
            float _WaterLevel;
            float _WaterClipSoftness;
            float _UseClipRipple;
            float4 _WaterUvMin;
            float4 _WaterUvSize;
            float _AnchorBottomToDynamicWater;
            float _DynamicWaterAnchorRange;
            float _DynamicWaterAnchorOffset;

            float _WaveAmplitude;
            float _WaveSpeed;
            float _PrimaryWaveLength;
            float _SecondaryWaveLength;
            float _GerstnerSteepness;
            sampler2D _SimulationTex;
            float4 _SimulationTex_TexelSize;
            float _Displacement;
            float _NormalStrength;

            static const float TwoPi = 6.28318530718;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 waterSampleXZ : TEXCOORD1;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            void AddVerticalWaterWave(inout float3 position, float2 direction, float length, float amplitude, float speedScale)
            {
                float2 dir = normalize(direction);
                float safeLength = max(length, 0.001);
                float waveNumber = TwoPi / safeLength;
                float phase = dot(position.xz, dir) * waveNumber + _Time.y * _WaveSpeed * speedScale;
                float sine = sin(phase);
                position.y += amplitude * sine;
            }

            float3 OceanPosition(float3 worldPosition)
            {
                float3 displaced = worldPosition;
                AddVerticalWaterWave(displaced, float2(0.91, 0.42), _PrimaryWaveLength * 1.22, _WaveAmplitude * 0.34, 0.74);
                AddVerticalWaterWave(displaced, float2(-0.72, 0.69), _PrimaryWaveLength * 0.73, _WaveAmplitude * 0.23, 0.96);
                AddVerticalWaterWave(displaced, float2(0.25, 0.97), _SecondaryWaveLength * 1.34, _WaveAmplitude * 0.13, 1.24);
                AddVerticalWaterWave(displaced, float2(-0.93, -0.36), _SecondaryWaveLength * 0.86, _WaveAmplitude * 0.07, 1.54);

                float radialDistance = length(worldPosition.xz);
                float radialEnvelope = 1.0 - smoothstep(18.0, 62.0, radialDistance);
                float radialPhaseA = radialDistance * 0.46 - _Time.y * _WaveSpeed * 1.28;
                float radialPhaseB = radialDistance * 0.25 - _Time.y * _WaveSpeed * 0.76 + 1.7;
                displaced.y += (sin(radialPhaseA) * 0.72 + sin(radialPhaseB) * 0.28)
                    * _WaveAmplitude * 0.20 * radialEnvelope;
                return displaced;
            }

            float SampleWaterVerticalOffset(float3 baseWorldPosition)
            {
                return OceanPosition(baseWorldPosition).y - baseWorldPosition.y;
            }

            float2 WaterWorldUv(float3 worldPosition)
            {
                return saturate((worldPosition.xz - _WaterUvMin.xy) / max(_WaterUvSize.xy, float2(0.001, 0.001)));
            }

            float SampleRipple(float3 worldPosition)
            {
                return tex2Dlod(_SimulationTex, float4(WaterWorldUv(worldPosition), 0.0, 0.0)).r;
            }

            float DynamicWaterY(float3 worldPosition, float includeRipple)
            {
                float waterY = _WaterLevel + SampleWaterVerticalOffset(float3(worldPosition.x, _WaterLevel, worldPosition.z));
                if (includeRipple > 0.5)
                {
                    waterY += tex2Dlod(_SimulationTex, float4(WaterWorldUv(worldPosition), 0.0, 0.0)).r * _Displacement;
                }
                return waterY;
            }

            float3 DisplaceWater(float3 worldPosition, float2 waterSampleXZ)
            {
                float verticalOffset = worldPosition.y - _WaterLevel;
                float3 sampleWorldPosition = float3(waterSampleXZ.x, _WaterLevel, waterSampleXZ.y);
                worldPosition.y = DynamicWaterY(sampleWorldPosition, 1.0) + verticalOffset;
                return worldPosition;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                if (_WaterMode > 0.5)
                {
                    worldPosition = DisplaceWater(worldPosition, v.waterSampleXZ);
                }
                else if (_AnchorBottomToDynamicWater > 0.5 &&
                         abs(worldPosition.y - _WaterLevel) <= max(_DynamicWaterAnchorRange, 0.001))
                {
                    float3 sampleWorldPosition = float3(v.waterSampleXZ.x, _WaterLevel, v.waterSampleXZ.y);
                    worldPosition.y = DynamicWaterY(sampleWorldPosition, _UseClipRipple) + _DynamicWaterAnchorOffset;
                }

                o.position = UnityWorldToClipPos(worldPosition);
                o.worldPosition = worldPosition;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float distanceToCenter = _UseWorldDistance > 0.5
                    ? distance(i.worldPosition, _HighlightCenter.xyz)
                    : distance(i.worldPosition.xz, _HighlightCenter.xz);
                float fade = 1.0 - smoothstep(
                    _HighlightRadius,
                    _HighlightRadius + max(_HighlightFadeWidth, 0.001),
                    distanceToCenter);
                fixed4 color = _Color;
                color.rgb = lerp(color.rgb, _EmissionColor.rgb, 0.35);
                float waterMask = 1.0;
                if (_ClipBelowDynamicWater > 0.5)
                {
                    float dynamicWaterY = DynamicWaterY(i.worldPosition, _UseClipRipple);
                    waterMask = smoothstep(
                        dynamicWaterY,
                        dynamicWaterY + max(_WaterClipSoftness, 0.001),
                        i.worldPosition.y);
                }
                color.a *= fade * _Alpha * waterMask;
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
