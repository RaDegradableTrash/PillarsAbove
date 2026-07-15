Shader "PillarsAbove/ShallowWaterInput"
{
    Properties
    {
        _MainTex ("Height State", 2D) = "black" {}
        _PulseUV ("Pulse UV", Vector) = (0.5, 0.5, 0, 0)
        _PulseRadiusUV ("Pulse Radius UV", Vector) = (0.05, 0.05, 0, 0)
        _PulseHeight ("Pulse Height", Float) = 0.5
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
            float2 _PulseUV;
            float2 _PulseRadiusUV;
            float _PulseHeight;

            float4 frag(v2f_img i) : SV_Target
            {
                float2 state = tex2D(_MainTex, i.uv).rg;
                float2 normalizedOffset = (i.uv - _PulseUV) / max(_PulseRadiusUV, 0.000001);
                float distanceFromCenter = length(normalizedOffset);

                // Smooth circular cap: full in the center and zero at the edge.
                float pulse = (1.0 - smoothstep(0.0, 1.0, distanceFromCenter)) * _PulseHeight;

                // Offset both time slices to create a displaced surface with zero
                // initial velocity; subsequent simulation steps propagate the ring.
                return float4(state.r + pulse, state.g + pulse, 0.0, 1.0);
            }
            ENDCG
        }
    }
}
