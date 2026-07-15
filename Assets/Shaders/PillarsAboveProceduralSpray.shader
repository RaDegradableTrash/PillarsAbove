Shader "PillarsAbove/ProceduralSpray"
{
    Properties
    {
        _DeepColor ("Deep Spray Color", Color) = (0.22, 0.74, 0.82, 0.35)
        _FoamColor ("Foam Color", Color) = (0.96, 1.00, 0.92, 0.92)
        _Alpha ("Alpha", Range(0, 1)) = 0.86
        _WaveAmplitude ("Swell Amplitude", Range(0, 2)) = 0.42
        _WaveFrequency ("Swell Frequency", Range(0.1, 8)) = 2.4
        _WaveSpeed ("Swell Speed", Range(0, 8)) = 2.2
        _ExpandDistance ("Normal Expansion", Range(0, 4)) = 1.2
        _NoiseStrength ("Noise Displacement", Range(0, 2)) = 0.56
        _NoiseScale ("Noise Scale", Range(0.1, 12)) = 3.7
        _VoronoiScale ("Voronoi Scale", Range(0.5, 24)) = 8.5
        _ErosionWidth ("Erosion Width", Range(0.01, 0.8)) = 0.18
        _ErosionSpeed ("Erosion Speed", Range(0, 4)) = 1.15
        _Center ("Island Center", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+80" "RenderType" = "Transparent" }
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
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _DeepColor;
            fixed4 _FoamColor;
            float _Alpha;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _WaveSpeed;
            float _ExpandDistance;
            float _NoiseStrength;
            float _NoiseScale;
            float _VoronoiScale;
            float _ErosionWidth;
            float _ErosionSpeed;
            float4 _Center;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float height01 : TEXCOORD1;
                float life01 : TEXCOORD2;
                float intensity : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash22(float2 p)
            {
                float n = Hash21(p);
                return frac(float2(n, Hash21(p + n + 19.19)));
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 uv = frac(p);
                uv = uv * uv * (3.0 - 2.0 * uv);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, uv.x), lerp(c, d, uv.x), uv.y);
            }

            float VoronoiPointDistance(float2 baseCell, float2 cellUv, float2 offset)
            {
                float2 feature = Hash22(baseCell + offset);
                feature = 0.5 + 0.5 * sin(6.28318 * feature);
                float2 delta = offset + feature - cellUv;
                return dot(delta, delta);
            }

            float Voronoi(float2 uv)
            {
                float2 baseCell = floor(uv);
                float2 cellUv = frac(uv);
                float minDist = VoronoiPointDistance(baseCell, cellUv, float2(-1.0, -1.0));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(0.0, -1.0)));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(1.0, -1.0)));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(-1.0, 0.0)));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(0.0, 0.0)));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(1.0, 0.0)));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(-1.0, 1.0)));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(0.0, 1.0)));
                minDist = min(minDist, VoronoiPointDistance(baseCell, cellUv, float2(1.0, 1.0)));
                return saturate(sqrt(minDist));
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float2 radial = worldPos.xz - _Center.xz;
                float2 radialDir = length(radial) > 0.001 ? normalize(radial) : float2(1.0, 0.0);
                float2 normalDir = length(worldNormal.xz) > 0.001 ? normalize(worldNormal.xz) : radialDir;

                float phase = dot(worldPos.xz, float2(0.74, 0.67)) * _WaveFrequency + _Time.y * _WaveSpeed + v.color.r * 6.28318;
                float swell = sin(phase) * _WaveAmplitude * lerp(0.25, 1.0, v.color.a);
                float life = v.color.g;
                float expansion = _ExpandDistance * (0.35 + 0.32 * sin(_Time.y * 0.65 + v.color.r * 6.28318));

                float noiseA = ValueNoise(worldPos.xz * _NoiseScale + v.color.rg * 3.1);
                float noiseB = ValueNoise(worldPos.zy * (_NoiseScale * 1.37) + v.color.gr * 2.7);
                float jagged = (noiseA + noiseB - 1.0) * _NoiseStrength * lerp(0.15, 0.65, v.color.a);

                worldPos.xz += normalDir * (expansion + jagged);
                worldPos.y += swell + abs(jagged) * 0.08 + life * 0.035;

                o.worldPos = worldPos;
                o.height01 = saturate(v.color.b);
                o.life01 = life;
                o.intensity = saturate(v.color.a);
                o.pos = UnityWorldToClipPos(worldPos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 erosionUv = i.worldPos.xz * _VoronoiScale;
                float cells = Voronoi(erosionUv);
                float threshold = lerp(0.04, 0.18, i.life01);
                float eroded = smoothstep(threshold - _ErosionWidth, threshold + _ErosionWidth, 1.0 - cells);
                float heightFade = lerp(0.92, 1.0, smoothstep(0.04, 0.68, i.height01));
                float edgeSoftness = lerp(0.94, 1.0, eroded);
                float alpha = saturate(max(0.72, heightFade * edgeSoftness) * _Alpha * i.intensity * lerp(0.98, 1.24, i.intensity));

                float foamWhiteness = saturate(i.height01 * 0.18 + i.intensity * 0.96 + eroded * 0.03);
                fixed3 color = lerp(_DeepColor.rgb, _FoamColor.rgb, foamWhiteness);
                color += fixed3(0.025, 0.055, 0.050) * i.intensity * smoothstep(0.20, 0.82, i.height01);
                UNITY_APPLY_FOG(i.fogCoord, color);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
