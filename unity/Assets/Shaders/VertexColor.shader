// 頂点カラー表示シェーダー (Built-in RP)。
// 点群由来メッシュ(頂点RGB)やカメラブリップの頂点カラーを描画する。
// Standardシェーダーは頂点カラーを無視するため、簡易ライティング付きで自前描画。
Shader "DT/VertexColor"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
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
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float3 worldNormal : TEXCOORD0;
            };

            float4 _Tint;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Tint;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 簡易ランバート + アンビエント
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndl = saturate(dot(normalize(i.worldNormal), lightDir));
                float lighting = 0.4 + 0.6 * ndl;
                return fixed4(i.color.rgb * lighting, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
