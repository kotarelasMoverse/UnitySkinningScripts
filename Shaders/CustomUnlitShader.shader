Shader "Unlit/CustomUnlitShader"
{
    Properties
    {
        _ModelID ("_ModelID", Integer) = 0
        _Color ("Main Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Tags {"LightMode"="ForwardBase"}

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc" // for _LightColor0

            struct appdata
            {
                float4 vertex : POSITION;
                uint vid : SV_VertexID;
            };

            StructuredBuffer<float4> _NewVertexPosBuffer;
            StructuredBuffer<float4> _NewNormalBuffer;

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 diff : COLOR0; // diffuse lighting color
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            int _ModelID;

            v2f vert (appdata v)
            {
                v2f o;
                float3 newPos = float3(_NewVertexPosBuffer[v.vid + 6890*_ModelID][0], _NewVertexPosBuffer[v.vid + 6890*_ModelID][1], _NewVertexPosBuffer[v.vid + 6890*_ModelID][2]);
                o.vertex = UnityObjectToClipPos(newPos);
                float3 newNormal = float3(_NewNormalBuffer[v.vid + 6890*_ModelID][0], _NewNormalBuffer[v.vid + 6890*_ModelID][1], _NewNormalBuffer[v.vid + 6890*_ModelID][2]);
                half3 worldNormal = UnityObjectToWorldNormal(newNormal);
                half nl = max(0, dot(worldNormal, _WorldSpaceLightPos0.xyz));
                o.diff = nl * _LightColor0;
                return o;
            }

            fixed4 _Color;

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = _Color;
                col *= i.diff;
                return col;
            }
            ENDCG
        }
    }
}
