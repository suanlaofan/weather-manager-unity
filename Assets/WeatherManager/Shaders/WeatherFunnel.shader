Shader "WeatherManager/Continuous Funnel"
{
    Properties { _BaseColor("Cloud color",Color)=(.48,.63,.67,.6) _FxTime("Simulation time",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; float _FxTime;
            CBUFFER_END
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p=i.positionOS.xyz;
                p.x+=sin(p.y*4.1+_FxTime*1.4)*.025*p.y;
                p.z+=cos(p.y*3.5+_FxTime*1.1)*.018*p.y;
                o.positionCS=TransformObjectToHClip(p); o.uv=i.uv; return o; }
            half4 frag(Varyings i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float spiral=sin(i.uv.x*31.4159+i.uv.y*21-_FxTime*8);
                float wisps=sin(i.uv.x*62.8318-i.uv.y*13+_FxTime*3);
                float a=(.34+.38*smoothstep(-.4,.9,spiral)+.12*wisps)*smoothstep(0,.1,i.uv.y)*(1-smoothstep(.78,1,i.uv.y));
                return half4(_BaseColor.rgb*(.72+.28*saturate(spiral)),_BaseColor.a*a); }
            ENDHLSL
        }
    }
}
