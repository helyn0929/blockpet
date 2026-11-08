// 魚眼（設計文件 5.27 ③；2026-11-08 使用者改：像 Sony FE 8-14mm F3.5 G 的全片幅魚眼）：
// 畫面整個填滿（四個角對到原圖的四個角，沒有黑邊、沒有圓形暗角），中間稍微放大、越往邊越壓縮，直線往外彎。
// 11-08 再改：臉不能變形——中間一個圓（_SafeRadius＝短邊一半的幾成）只等比放大 _CenterZoom 倍、形狀不變，
// 彎曲集中在外圈（_EdgePower 越大越集中在邊緣、越彎），四個角仍然對到原圖的四個角。
// 給 RawImage 用：_UVRect＝RawImage.uvRect（縮放、裁切後的範圍），_Aspect＝畫面上的寬／高（變形以對角線為準，圓的就是圓的）。
// 以 Unity 內建 UI/Default 為底（保留 Mask 的 stencil、RectMask2D 的裁切）。由 FisheyeImage 設定參數。
Shader "BlockPet/UI/Fisheye"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SafeRadius ("Undistorted Center (of short half)", Range(0, 1)) = 0.6
        _CenterZoom ("Center Zoom", Range(1, 2.5)) = 1.4
        _EdgePower ("Edge Curve Power", Range(1.5, 5)) = 3
        _UVRect ("UV Rect", Vector) = (0, 0, 1, 1)
        _Aspect ("Aspect (w/h)", Float) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _SafeRadius, _CenterZoom, _EdgePower, _Aspect;
            float4 _UVRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // RawImage 的 uv → 這張圖框裡的 0～1 → 以中心為原點，畫面上等比例，四個角的半徑＝1
                float2 rectUV = (IN.texcoord - _UVRect.xy) / max(_UVRect.zw, 1e-5);
                float2 p = rectUV * 2.0 - 1.0;
                float2 a = float2(_Aspect, 1.0) / sqrt(_Aspect * _Aspect + 1.0);
                float2 q = p * a;
                float r = length(q);

                // 中間 r0 以內：只等比放大（臉不變形）；外圈：s = r/m + c·(r−r0)^k，接得平順，r=1（角落）對到原圖角落
                float r0 = _SafeRadius * min(a.x, a.y);
                float m = _CenterZoom;
                float outer = max(r - r0, 0.0);
                float c = (1.0 - 1.0 / m) / pow(max(1.0 - r0, 1e-4), _EdgePower);
                float srcR = r / m + c * pow(outer, _EdgePower);
                float2 src = r > 1e-5 ? q * (srcR / r) : q / m;
                float2 srcUV = (src / a + 1.0) * 0.5;
                float2 uv = _UVRect.xy + saturate(srcUV) * _UVRect.zw;
                half4 color = (tex2D(_MainTex, uv) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
