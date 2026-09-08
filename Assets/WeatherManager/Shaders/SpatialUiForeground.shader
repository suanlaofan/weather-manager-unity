Shader "WeatherManager/Spatial UI Foreground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _PaperKey ("Remove icon atlas paper", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex; fixed4 _Color; fixed4 _TextureSampleAdd; float _PaperKey;
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex=UnityObjectToClipPos(v.vertex);
                #ifndef UNITY_COLORSPACE_GAMMA
                v.color.rgb=GammaToLinearSpace(v.color.rgb);
                #endif
                o.color=v.color*_Color;o.uv=v.uv;return o; }
            fixed4 frag(v2f i):SV_Target {
                fixed4 c=tex2D(_MainTex,i.uv)+_TextureSampleAdd;
                float3 paper=float3(.947,.923,.867);
                #ifndef UNITY_COLORSPACE_GAMMA
                paper=GammaToLinearSpace(paper);
                #endif
                c.a*=lerp(1,smoothstep(.016,.07,length(c.rgb-paper)),_PaperKey);
                return c*i.color;
            }
            ENDCG
        }
    }
}
