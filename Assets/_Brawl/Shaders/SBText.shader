// 文字：动态字体贴图的 alpha × 颜色，双眼都画（头显单通道双眼渲染），参与深度测试（被木偶挡住时会被挡）。
Shader "StringBrawl/Text"
{
    Properties { _MainTex ("字体贴图", 2D) = "white" {} _Color ("颜色", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; half4 _Color; CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col * _Color; return v; }
            half4 frag(V i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); half al = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a; return half4(i.col.rgb, i.col.a * al); }
            ENDHLSL
        }
    }
}
