Shader "WeatherManager/Quiet Water"
{
    Properties { _BaseColor ("Lagoon", Color) = (.17,.48,.50,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 p:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 p:SV_POSITION; float2 uv:TEXCOORD0; half3 normal:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes i) { Varyings o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.p.xz;o.normal=TransformObjectToWorldNormal(float3(0,1,0));return o; }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p=i.uv/float2(6,4)+float2(_Time.y*.025,0);
                float2 cell=floor(p);float noise=frac(sin(dot(cell,float2(127.1,311.7)))*43758.5453);
                float2 f=frac(p);
                float ripple=(1-smoothstep(.025,.05,abs(f.y-(.28+noise*.35))))*smoothstep(.12,.22,f.x)*(1-smoothstep(.48+noise*.22,.56+noise*.22,f.x));
                half pulse=.98h+.025h*sin(i.uv.x*.6+i.uv.y*.3+_Time.y*.45);
                Light sun=GetMainLight();half3 lit=SampleSH(i.normal)+sun.color*(.32h+.50h*saturate(dot(i.normal,sun.direction)));
                half3 c=_BaseColor.rgb*(pulse+ripple*.20h);
                return half4(c*lit,1);
            }
            ENDHLSL
        }
    }
}
