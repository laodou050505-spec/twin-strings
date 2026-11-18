// 加色发光（聚光灯光柱、闪光灯、灯泡）：颜色 × 顶点色 × 沿 uv.y 渐隐，不写深度。
Shader "StringBrawl/Glow"
{
    Properties { _Color ("颜色", Color) = (1, 0.95, 0.8, 1) _Fade ("沿 uv.y 渐隐", Range(0, 1)) = 1 }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off Cull Off Blend One One, One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Color; half _Fade; CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col; return v; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half f = lerp(1, i.uv.y, _Fade);
                half3 c = _Color.rgb * i.col.rgb * f * _Color.a;
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
