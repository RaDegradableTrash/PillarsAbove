Shader "PillarsAbove/ShallowWaterSurface"
{
    Properties
    {
        _SimulationTex ("Simulation Texture", 2D) = "black" {}
        _ShallowColor ("Shallow Color", Color) = (0.08, 0.55, 0.62, 0.72)
        _DeepColor ("Deep Color", Color) = (0.01, 0.12, 0.25, 0.88)
        _Displacement ("Displacement", Range(0, 5)) = 1
        _NormalStrength ("Normal Strength", Range(0, 8)) = 1
        _Smoothness ("Smoothness", Range(8, 256)) = 96
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 0.65
        _Alpha ("Alpha", Range(0, 1)) = 0.82
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
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

            sampler2D _SimulationTex;
            float4 _SimulationTex_TexelSize;
            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            float _Displacement;
            float _NormalStrength;
            float _Smoothness;
            float _FresnelStrength;
            float _Alpha;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float height : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                float2 texel = _SimulationTex_TexelSize.xy;
                float height = tex2Dlod(_SimulationTex, float4(v.uv, 0.0, 0.0)).r;
                float left = tex2Dlod(_SimulationTex, float4(v.uv - float2(texel.x, 0.0), 0.0, 0.0)).r;
                float right = tex2Dlod(_SimulationTex, float4(v.uv + float2(texel.x, 0.0), 0.0, 0.0)).r;
                float down = tex2Dlod(_SimulationTex, float4(v.uv - float2(0.0, texel.y), 0.0, 0.0)).r;
                float up = tex2Dlod(_SimulationTex, float4(v.uv + float2(0.0, texel.y), 0.0, 0.0)).r;

                float4 displaced = v.vertex;
                displaced.y += height * _Displacement;

                float2 gradient = float2(right - left, up - down) * _NormalStrength;
                float3 localNormal = normalize(float3(-gradient.x, 1.0, -gradient.y));
                float3 worldPosition = mul(unity_ObjectToWorld, displaced).xyz;

                o.position = UnityWorldToClipPos(worldPosition);
                o.worldPosition = worldPosition;
                o.worldNormal = UnityObjectToWorldNormal(localNormal);
                o.height = height;
                UNITY_TRANSFER_FOG(o, o.position);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.worldPosition);
                float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDirection = normalize(lightDirection + viewDirection);

                float diffuse = saturate(dot(normal, lightDirection));
                float specular = pow(saturate(dot(normal, halfDirection)), _Smoothness);
                float fresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 5.0) * _FresnelStrength;
                float heightBlend = saturate(i.height * 0.5 + 0.5);

                fixed3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, heightBlend);
                fixed3 ambient = ShadeSH9(float4(normal, 1.0));
                fixed3 lighting = ambient + _LightColor0.rgb * diffuse;
                fixed3 color = waterColor * lighting;
                color += _LightColor0.rgb * specular;
                color = lerp(color, fixed3(0.65, 0.85, 0.92), saturate(fresnel));

                fixed4 output = fixed4(color, _Alpha);
                UNITY_APPLY_FOG(i.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }

    Fallback Off
}
