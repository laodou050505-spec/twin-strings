// LED 环屏：自发光（不受场景灯光影响），字幕沿 u 方向循环滚动；全局 _SB_Excite 越高越亮、滚得越快。双眼渲染。
Shader "StringBrawl/Led"
{
    Properties { _MainTex ("LED 字幕", 2D) = "black" {} _Speed ("滚动速度", Float) = 0.04 _Bright ("亮度", Float) = 1.3 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; float _Speed; float _Bright; CBUFFER_END
            float _SB_Excite;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; return v; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float ex = saturate(_SB_Excite);
                float2 uv = float2(i.uv.x * _MainTex_ST.x - frac(_Time.y * _Speed), i.uv.y);   // 恒定速度、无限循环（frac 防止时间大了精度抖动）
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                return half4(c * _Bright * (0.85 + ex * 0.5), 1);
            }
            ENDHLSL
        }
    }
}
