Shader "Cementery/Skybox/MinecraftDayNight"
{
    Properties
    {
        _SkyTint ("Sky Tint", Color) = (0.53, 0.72, 1, 1)
        _GroundColor ("Ground Color", Color) = (0.42, 0.45, 0.5, 1)
        _SunColor ("Sun Color", Color) = (1, 0.956, 0.84, 1)
        _Exposure ("Exposure", Range(0, 8)) = 1
        _AtmosphereThickness ("Atmosphere Thickness", Range(0, 5)) = 1
        _SunSize ("Sun Size", Range(0.001, 0.2)) = 0.008
        _SunSoftness ("Sun Softness", Range(0.0005, 0.03)) = 0.0018
        _GodrayStrength ("Godray Strength", Range(0, 1)) = 0
        _GodrayPower ("Godray Power", Range(0.5, 8)) = 2.6
        _GodrayTint ("Godray Tint", Color) = (1, 0.82, 0.62, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _SkyTint;
            fixed4 _GroundColor;
            fixed4 _SunColor;
            float _Exposure;
            float _AtmosphereThickness;
            float _SunSize;
            float _SunSoftness;
            float _GodrayStrength;
            float _GodrayPower;
            fixed4 _GodrayTint;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(i.dir);
                float upMask = saturate(viewDir.y * 0.5 + 0.5);
                float horizon = 1.0 - abs(viewDir.y);
                horizon = pow(saturate(horizon), lerp(3.0, 1.0, saturate(_AtmosphereThickness / 5.0)));

                float3 gradient = lerp(_GroundColor.rgb, _SkyTint.rgb, upMask);
                gradient += _SkyTint.rgb * horizon * 0.2;

                float3 sunDir = normalize(_WorldSpaceLightPos0.xyz);
                float sunDot = dot(viewDir, sunDir);
                float sunEdge = max(0.0005, _SunSoftness);
                float sunDisk = smoothstep(1.0 - _SunSize - sunEdge, 1.0 - _SunSize, sunDot);
                sunDisk = pow(sunDisk, 1.4);

                float sunForward = saturate(dot(viewDir, sunDir));
                float sunCone = pow(sunForward, lerp(96.0, 14.0, saturate(_GodrayStrength)));
                float rayPatternA = sin((viewDir.x * 51.0 + viewDir.z * 37.0) + _Time.y * 0.32);
                float rayPatternB = sin((viewDir.x * -29.0 + viewDir.y * 43.0) - _Time.y * 0.21);
                float rayPattern = saturate(rayPatternA * rayPatternB * 0.5 + 0.5);
                rayPattern = pow(rayPattern, max(0.5, _GodrayPower));
                float horizonMask = saturate(1.0 - abs(viewDir.y) * 0.7);
                float godray = sunCone * rayPattern * horizonMask * saturate(_GodrayStrength);

                float3 col = gradient + _SunColor.rgb * sunDisk;
                col += _GodrayTint.rgb * godray;
                col *= _Exposure;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
