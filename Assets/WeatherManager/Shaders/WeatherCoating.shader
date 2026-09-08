Shader "WeatherManager/Weather Coating"
{
    Properties { _Snow("Snow",Range(0,1))=0 _Wet("Wet",Range(0,1))=0 _Heat("Dryness",Range(0,1))=0 _WeatherUp("Island up",Vector)=(0,1,0,0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-5" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; half upward:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
                half _Snow,_Wet,_Heat; float4 _WeatherUp;
            CBUFFER_END
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.upward=dot(TransformObjectToWorldNormal(i.normalOS),_WeatherUp.xyz);return o; }
            half4 frag(Varyings i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half snow=saturate(_Snow*1.6)*smoothstep(.3,.8,i.upward);
                half wet=_Wet*.26;half dry=_Heat*.28;
                half3 base=lerp(half3(.07,.20,.25),half3(.64,.40,.16),saturate(dry*6));
                return half4(lerp(base,half3(.9,.96,1),snow),max(snow,max(wet,dry))); }
            ENDHLSL
        }
    }
}
