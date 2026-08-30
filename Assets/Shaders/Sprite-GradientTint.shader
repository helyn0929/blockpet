Shader "Sprites/Gradient Tint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GradientTex ("Gradient Ramp (灰階0~1對應的顏色)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 由 GradientTintSprite.cs 依 Additive/Screen 切換，不要手動改
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _GradientTex;
            fixed4 _Color;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);

                // 灰階當漸層對照表的取樣座標：貼圖越黑對應 Gradient 左端，越白對應右端
                half gray = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                fixed4 ramp = tex2D(_GradientTex, float2(gray, 0.5));

                fixed4 col;
                col.rgb = ramp.rgb * ramp.a * tex.a * i.color.rgb * i.color.a;
                col.a = tex.a * i.color.a;
                return col;
            }
            ENDCG
        }
    }
}
