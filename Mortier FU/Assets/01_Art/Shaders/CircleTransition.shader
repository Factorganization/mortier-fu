Shader "Custom/CircleTransition"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _Progress ("Progress", Range(0, 1)) = 0
        _Center ("Center", Vector) = (0.5, 0.5, 0, 0)
    }

    SubShader
    {
        Tags 
        { 
            "Queue"="Overlay" 
            "RenderType"="Transparent" 
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float4 _Color;
            float _Progress;
            float4 _Center;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 center = _Center.xy;

                float aspect = _ScreenParams.x / _ScreenParams.y;

                float2 correctedUv = i.uv - center;
                correctedUv.x *= aspect;

                float dist = length(correctedUv);

                // Distance corrigée aux 4 coins de l'écran, pour trouver le point le plus éloigné du centre
                float2 c0 = float2(0, 0) - center; c0.x *= aspect;
                float2 c1 = float2(1, 0) - center; c1.x *= aspect;
                float2 c2 = float2(0, 1) - center; c2.x *= aspect;
                float2 c3 = float2(1, 1) - center; c3.x *= aspect;

                float maxRadius = max(max(length(c0), length(c1)), max(length(c2), length(c3)));
                float radius = _Progress * maxRadius * 1.15;

                float alpha = smoothstep(radius, radius, dist);

                return float4(_Color.rgb, alpha * _Color.a);
            }

            ENDCG
        }
    }
}