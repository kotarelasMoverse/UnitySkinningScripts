Shader "Unlit/CustomUnlitShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ModelID ("_ModelID", Integer) = 0
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
                float2 uv : TEXCOORD0;
                uint vid : SV_VertexID;
            };

            StructuredBuffer<float4> _NewVertexPosBuffer;

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
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
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
