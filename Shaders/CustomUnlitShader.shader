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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                uint vid : SV_VertexID;
            };

            StructuredBuffer<float4> _NewVertexPosBuffer;

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            int _ModelID;

            v2f vert (appdata v)
            {
                v2f o;
                float3 newPos = float3(_NewVertexPosBuffer[v.vid + 6890*_ModelID][0], _NewVertexPosBuffer[v.vid + 6890*_ModelID][1], _NewVertexPosBuffer[v.vid + 6890*_ModelID][2]);
                o.vertex = UnityObjectToClipPos(newPos);
                return o;
            }

            fixed4 _Color;

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = _Color;
                return col;
            }
            ENDCG
        }
    }
}
