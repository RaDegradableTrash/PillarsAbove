Shader "PillarsAbove/ShallowWaterSimulation"
{
    Properties
    {
        _MainTex ("Current and Previous Height", 2D) = "black" {}
        _Damping ("Damping", Range(0.9, 1.0)) = 0.995
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Damping;

            float4 frag(v2f_img i) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy;
                float2 state = tex2D(_MainTex, i.uv).rg;
                float left = tex2D(_MainTex, i.uv - float2(texel.x, 0.0)).r;
                float right = tex2D(_MainTex, i.uv + float2(texel.x, 0.0)).r;
                float down = tex2D(_MainTex, i.uv - float2(0.0, texel.y)).r;
                float up = tex2D(_MainTex, i.uv + float2(0.0, texel.y)).r;

                float averageNeighbors = (left + right + down + up) * 0.25;
                float newHeight = (averageNeighbors * 2.0 - state.g) * _Damping;

                // R becomes the new height; G preserves the just-simulated height.
                return float4(newHeight, state.r, 0.0, 1.0);
            }
            ENDCG
        }
    }
}
