Shader "PillarsAbove/OceanFoam"
{
    Properties
    {
        _ShallowColor ("Water Color", Color) = (0.035, 0.17, 0.25, 1)
        _MidColor ("Water Color Mid", Color) = (0.035, 0.17, 0.25, 1)
        _DeepColor ("Water Color Deep", Color) = (0.035, 0.17, 0.25, 1)
        _HorizonColor ("Soft Reflection", Color) = (0.30, 0.38, 0.42, 1)
        _FarWaterColor ("Far Water Haze", Color) = (0.50, 0.51, 0.51, 1)
        _DiffuseTint ("Diffuse Tint", Color) = (0.17, 0.22, 0.25, 1)
        _ShoreRippleColor ("Shore Ripple Color", Color) = (0.36, 0.45, 0.47, 1)
        _Alpha ("Base Alpha", Range(0, 1)) = 1

        _WaveAmplitude ("Wave Amplitude", Range(0.05, 5.0)) = 2.25
        _WaveSpeed ("Wave Speed", Range(0.05, 4.0)) = 0.64
        _PrimaryWaveLength ("Primary Wave Length", Range(8.0, 180.0)) = 52
        _SecondaryWaveLength ("Secondary Wave Length", Range(3.0, 80.0)) = 21
        _GerstnerSteepness ("Wave Steepness", Range(0, 1)) = 0.52
        _MicroRippleStrength ("Micro Ripple Strength", Range(0, 2)) = 0.22
        _SimulationTex ("Interactive Ripple Texture", 2D) = "black" {}
        _Displacement ("Interactive Ripple Displacement", Range(0, 5)) = 0.65
        _NormalStrength ("Interactive Ripple Normal Strength", Range(0, 8)) = 1
        _WaterUvMin ("Water UV World Min", Vector) = (0, 0, 0, 0)
        _WaterUvSize ("Water UV World Size", Vector) = (1, 1, 0, 0)

        _FoamColor ("Foam Color", Color) = (0.64, 0.67, 0.66, 1)
        _ShoreFoamDistance ("Shore Ripple Distance", Range(0.1, 16)) = 11.5
        _ShoreFoamStrength ("Shore Ripple Strength", Range(0, 2)) = 1.05
        _CrestFoamThreshold ("Crest Foam Threshold", Range(0, 1.5)) = 0.62
        _CrestFoamWidth ("Crest Foam Width", Range(0.02, 0.8)) = 0.26
        _CrestFoamStrength ("Crest Foam Strength", Range(0, 2)) = 0.78
        _FoamScale ("Foam Detail Scale", Range(0.03, 1)) = 0.18
        _FoamDrift ("Foam Drift", Range(0, 3)) = 0.46

        _Smoothness ("Highlight Sharpness", Range(16, 512)) = 72
        _SunGlitterStrength ("Sun Glitter", Range(0, 4)) = 0.42
        _ReflectionStrength ("Soft Reflection", Range(0, 2)) = 0.38
        _DiffuseStrength ("Wrapped Diffuse", Range(0, 2)) = 0.32
        _ShadowStrength ("Realtime Shadow Strength", Range(0, 1)) = 0.78
        _FresnelPower ("Fresnel Power", Range(1, 8)) = 3.2
        _DepthColorRange ("Depth Color Range", Range(2, 80)) = 28
        _FarFogStart ("Far Water Fog Start", Range(20, 600)) = 170
        _FarFogEnd ("Far Water Fog End", Range(80, 3000)) = 600
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+20" "RenderType" = "Opaque" }
        LOD 250
        ZWrite On
        Cull Back

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _ShallowColor;
            fixed4 _MidColor;
            fixed4 _DeepColor;
            fixed4 _HorizonColor;
            fixed4 _FarWaterColor;
            fixed4 _DiffuseTint;
            fixed4 _ShoreRippleColor;
            fixed4 _FoamColor;
            float _Alpha;

            float _WaveAmplitude;
            float _WaveSpeed;
            float _PrimaryWaveLength;
            float _SecondaryWaveLength;
            float _GerstnerSteepness;
            float _MicroRippleStrength;
            sampler2D _SimulationTex;
            float4 _SimulationTex_TexelSize;
            float _Displacement;
            float _NormalStrength;
            float4 _WaterUvMin;
            float4 _WaterUvSize;

            float _ShoreFoamDistance;
            float _ShoreFoamStrength;
            float _CrestFoamThreshold;
            float _CrestFoamWidth;
            float _CrestFoamStrength;
            float _FoamScale;
            float _FoamDrift;

            float _Smoothness;
            float _SunGlitterStrength;
            float _ReflectionStrength;
            float _DiffuseStrength;
            float _ShadowStrength;
            float _FresnelPower;
            float _DepthColorRange;
            float _FarFogStart;
            float _FarFogEnd;

            sampler2D _CameraDepthTexture;
            static const float TwoPi = 6.28318530718;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 screenPosition : TEXCOORD2;
                float eyeDepth : TEXCOORD3;
                float height01 : TEXCOORD4;
                float crestEnergy : TEXCOORD5;
                UNITY_FOG_COORDS(6)
                SHADOW_COORDS(7)
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1, 0));
                float c = Hash21(cell + float2(0, 1));
                float d = Hash21(cell + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                value += ValueNoise(p) * 0.52;
                p = mul(float2x2(0.80, -0.60, 0.60, 0.80), p) * 2.03 + 17.7;
                value += ValueNoise(p) * 0.29;
                p = mul(float2x2(0.80, -0.60, 0.60, 0.80), p) * 2.11 + 9.2;
                value += ValueNoise(p) * 0.19;
                return value;
            }

            void AddGerstnerWave(
                inout float3 position,
                inout float3 tangent,
                inout float3 binormal,
                float2 direction,
                float length,
                float amplitude,
                float speedScale)
            {
                float2 dir = normalize(direction);
                float safeLength = max(length, 0.001);
                float waveNumber = TwoPi / safeLength;
                float phase = dot(position.xz, dir) * waveNumber + _Time.y * _WaveSpeed * speedScale;
                float sine = sin(phase);
                float cosine = cos(phase);
                position.y += amplitude * sine;

                float steepness = _GerstnerSteepness / max(waveNumber * amplitude * 5.0, 0.001);
                float wa = waveNumber * amplitude;
                tangent += float3(
                    -dir.x * dir.x * steepness * wa * sine,
                    dir.x * wa * cosine,
                    -dir.x * dir.y * steepness * wa * sine);
                binormal += float3(
                    -dir.x * dir.y * steepness * wa * sine,
                    dir.y * wa * cosine,
                    -dir.y * dir.y * steepness * wa * sine);
            }

            float3 OceanPosition(float3 worldPosition, out float3 normal, out float crestEnergy)
            {
                float3 displaced = worldPosition;
                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);

                // Broad directional swells establish readable wave rhythm. Shorter,
                // weaker crossing waves break repetition without producing lumps.
                AddGerstnerWave(displaced, tangent, binormal, float2(0.91, 0.42), _PrimaryWaveLength * 1.22, _WaveAmplitude * 0.34, 0.74);
                AddGerstnerWave(displaced, tangent, binormal, float2(-0.72, 0.69), _PrimaryWaveLength * 0.73, _WaveAmplitude * 0.23, 0.96);
                AddGerstnerWave(displaced, tangent, binormal, float2(0.25, 0.97), _SecondaryWaveLength * 1.34, _WaveAmplitude * 0.13, 1.24);
                AddGerstnerWave(displaced, tangent, binormal, float2(-0.93, -0.36), _SecondaryWaveLength * 0.86, _WaveAmplitude * 0.07, 1.54);

                normal = normalize(cross(binormal, tangent));
                if (normal.y < 0.0)
                {
                    normal = -normal;
                }

                // A pair of broad radial swells lets the sea visibly breathe around
                // the monolith instead of only sliding past it in one direction.
                float radialDistance = length(worldPosition.xz);
                float2 radialDirection = worldPosition.xz / max(radialDistance, 0.001);
                float radialEnvelope = 1.0 - smoothstep(18.0, 62.0, radialDistance);
                float radialPhaseA = radialDistance * 0.46 - _Time.y * _WaveSpeed * 1.28;
                float radialPhaseB = radialDistance * 0.25 - _Time.y * _WaveSpeed * 0.76 + 1.7;
                float radialHeight = (sin(radialPhaseA) * 0.72 + sin(radialPhaseB) * 0.28)
                    * _WaveAmplitude * 0.20 * radialEnvelope;
                displaced.y += radialHeight;
                float radialSlope = (cos(radialPhaseA) * 0.46 * 0.72 + cos(radialPhaseB) * 0.25 * 0.28)
                    * _WaveAmplitude * 0.20 * radialEnvelope;
                normal = normalize(normal + float3(-radialDirection.x * radialSlope, 0.0, -radialDirection.y * radialSlope));

                float normalizedHeight = saturate((displaced.y - worldPosition.y) / max(_WaveAmplitude, 0.001) * 0.5 + 0.5);
                float slope = saturate((1.0 - normal.y) * 3.2);
                crestEnergy = normalizedHeight * 0.72 + slope * 0.58 + saturate(radialHeight) * 0.22;
                return displaced;
            }

            float2 WaterWorldUv(float3 worldPosition)
            {
                return saturate((worldPosition.xz - _WaterUvMin.xy) / max(_WaterUvSize.xy, float2(0.001, 0.001)));
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 baseWorldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal;
                float crestEnergy;
                float3 worldPosition = OceanPosition(baseWorldPosition, worldNormal, crestEnergy);
                float2 texel = _SimulationTex_TexelSize.xy;
                float2 rippleUv = WaterWorldUv(baseWorldPosition);
                float ripple = tex2Dlod(_SimulationTex, float4(rippleUv, 0.0, 0.0)).r;
                float rippleLeft = tex2Dlod(_SimulationTex, float4(rippleUv - float2(texel.x, 0.0), 0.0, 0.0)).r;
                float rippleRight = tex2Dlod(_SimulationTex, float4(rippleUv + float2(texel.x, 0.0), 0.0, 0.0)).r;
                float rippleDown = tex2Dlod(_SimulationTex, float4(rippleUv - float2(0.0, texel.y), 0.0, 0.0)).r;
                float rippleUp = tex2Dlod(_SimulationTex, float4(rippleUv + float2(0.0, texel.y), 0.0, 0.0)).r;
                float2 rippleGradient = float2(rippleRight - rippleLeft, rippleUp - rippleDown) * _NormalStrength;
                worldPosition.y += ripple * _Displacement;
                worldNormal = normalize(worldNormal + float3(-rippleGradient.x, 0.0, -rippleGradient.y));

                o.pos = UnityWorldToClipPos(worldPosition);
                o.worldPosition = worldPosition;
                o.worldNormal = worldNormal;
                o.screenPosition = ComputeScreenPos(o.pos);
                o.eyeDepth = -mul(UNITY_MATRIX_V, float4(worldPosition, 1.0)).z;
                o.height01 = saturate((worldPosition.y - baseWorldPosition.y) / max(_WaveAmplitude, 0.001) * 0.5 + 0.5);
                o.crestEnergy = crestEnergy;
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW_WPOS(o, worldPosition);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 geometricNormal = normalize(i.worldNormal);

                // Screen-independent micro ripples keep highlights alive up close
                // without adding noisy displacement to the silhouette.
                float2 rippleUvA = i.worldPosition.xz * 0.085 + float2(_Time.y * 0.022, -_Time.y * 0.016);
                float2 rippleUvB = i.worldPosition.xz * 0.17 + float2(-_Time.y * 0.032, _Time.y * 0.025);
                float rippleA = Fbm(rippleUvA);
                float rippleB = Fbm(rippleUvB);
                float2 rippleGradient = float2(
                    ddx(rippleA + rippleB * 0.45),
                    ddy(rippleA + rippleB * 0.45)) * _MicroRippleStrength;
                float3 normal = normalize(geometricNormal + float3(-rippleGradient.x, 0.0, -rippleGradient.y));

                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.worldPosition);
                float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDirection = normalize(lightDirection + viewDirection);
                UNITY_LIGHT_ATTENUATION(attenuation, i, i.worldPosition);
                float ndotl = saturate(dot(normal, lightDirection));
                float wrappedDiffuse = saturate((dot(normal, lightDirection) + 0.38) / 1.38);
                float ndotv = saturate(dot(normal, viewDirection));
                float fresnel = pow(1.0 - ndotv, _FresnelPower);

                float2 screenUv = i.screenPosition.xy / max(i.screenPosition.w, 0.0001);
                float rawSceneDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUv);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth);
                float waterDepth = max(0.0, sceneEyeDepth - i.eyeDepth);
                float depth01 = saturate(waterDepth / max(_DepthColorRange, 0.001));

                fixed3 waterColor = _MidColor.rgb;
                waterColor *= lerp(0.88, 1.08, wrappedDiffuse * attenuation);
                waterColor = lerp(waterColor, waterColor + _DiffuseTint.rgb * 0.10,
                    wrappedDiffuse * _DiffuseStrength * attenuation);
                float sunlitCrest = smoothstep(0.58, 0.94, i.height01) * (1.0 - fresnel) * 0.12;
                waterColor = lerp(waterColor, _MidColor.rgb * 1.10, sunlitCrest);

                float3 reflectionDirection = reflect(-viewDirection, normal);
                half4 reflectionSample = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflectionDirection);
                fixed3 environmentReflection = DecodeHDR(reflectionSample, unity_SpecCube0_HDR);
                fixed3 skyReflection = lerp(_HorizonColor.rgb, environmentReflection, 0.28);
                waterColor = lerp(waterColor, skyReflection, saturate(fresnel * _ReflectionStrength));

                float broadHighlight = pow(saturate(dot(normal, halfDirection)), max(8.0, _Smoothness * 0.24));
                float sharpHighlight = pow(saturate(dot(normal, halfDirection)), _Smoothness);
                float glitterNoise = smoothstep(0.54, 0.76, rippleA * 0.62 + rippleB * 0.38);
                float sunGlitter = (broadHighlight * 0.42 + sharpHighlight * (0.40 + glitterNoise * 0.65))
                    * _SunGlitterStrength * attenuation;
                waterColor += _LightColor0.rgb * sunGlitter * 0.72;

                // Realtime shadow map contribution. This is the real pillar/placed
                // geometry shadow on the water surface, separate from stylized foam
                // and reflection terms.
                float realtimeShadow = lerp(1.0 - _ShadowStrength, 1.0, attenuation);
                waterColor *= saturate(realtimeShadow);

                // Foam is evaluated directly on the displaced water surface. Depth
                // supplies shoreline contact; wave height and slope supply crest foam.
                float2 foamFlow = float2(_Time.y * _FoamDrift * 0.16, -_Time.y * _FoamDrift * 0.11);
                float foamNoise = Fbm(i.worldPosition.xz * _FoamScale + foamFlow);
                float foamDetail = Fbm(i.worldPosition.xz * (_FoamScale * 2.7) - foamFlow * 1.8);
                float brokenPattern = smoothstep(0.39, 0.72, foamNoise * 0.72 + foamDetail * 0.38);

                float crestMask = smoothstep(
                    _CrestFoamThreshold,
                    _CrestFoamThreshold + max(_CrestFoamWidth, 0.001),
                    i.crestEnergy);
                float crestRibbons = smoothstep(0.38, 0.72,
                    sin(dot(i.worldPosition.xz, normalize(float2(-0.42, 0.91))) * 0.54
                        - _Time.y * _WaveSpeed * 1.3 + foamNoise * 3.4) * 0.5 + 0.5);
                float crestFoam = crestMask * lerp(0.28, 1.0, brokenPattern) * lerp(0.62, 1.0, crestRibbons) * _CrestFoamStrength;

                float shoreProximity = 1.0 - smoothstep(0.05, _ShoreFoamDistance, waterDepth);
                float shoreVariation = sin(dot(i.worldPosition.xz, normalize(float2(0.83, -0.56))) * 1.7
                    + _Time.y * 0.34 + foamDetail * 5.2) * 0.5 + 0.5;
                float shoreBreakup = smoothstep(0.42, 0.68,
                    foamNoise * 0.58 + foamDetail * 0.36 + shoreVariation * 0.18);
                float shoreFoam = shoreProximity
                    * smoothstep(0.30, 0.72, shoreBreakup + crestMask * 0.24)
                    * _ShoreFoamStrength;

                float foam = saturate(max(crestFoam, shoreFoam));
                foam *= lerp(0.18, 1.0, smoothstep(0.18, 0.52, foamNoise + foamDetail * 0.28));
                float shoreRipple = shoreProximity * (0.18 + shoreBreakup * 0.42);
                waterColor = lerp(waterColor, _ShoreRippleColor.rgb, shoreRipple * 0.32);
                waterColor = lerp(waterColor, _FoamColor.rgb, foam * 0.36);

                // Keep the ocean base color consistent. Only camera-distance haze
                // lifts the far water toward a soft neutral grey-white horizon.
                float cameraDistance = distance(_WorldSpaceCameraPos.xyz, i.worldPosition);
                float farFog = smoothstep(_FarFogStart, max(_FarFogStart + 1.0, _FarFogEnd), cameraDistance);
                waterColor = lerp(waterColor, _FarWaterColor.rgb, farFog * 0.62);

                float alpha = saturate(_Alpha + fresnel * 0.015 + foam * 0.02);
                fixed4 outputColor = fixed4(waterColor, alpha);
                UNITY_APPLY_FOG(i.fogCoord, outputColor);
                return outputColor;
            }
            ENDCG
        }
    }

    Fallback Off
}
