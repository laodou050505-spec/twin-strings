// 观众：所有小人合成一张网格（一次绘制），顶点色上色；uv2.x = 随机相位、uv2.y = 0 身体 / 1 手臂。
// 全局 _SB_Excite（0..1）越高，观众跳得越高、举手越频繁（打中、KO 时由脚本拉高再慢慢回落）。
Shader "StringBrawl/Crowd"
{
    Properties { _Bob ("跳动高度（米）", Float) = 0.004 }
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
            CBUFFER_START(UnityPerMaterial) float _Bob; CBUFFER_END
            float _SB_Excite;
            struct A { float4 pos : POSITION; float3 n : NORMAL; half4 col : COLOR; float2 seed : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float t = _Time.y, ph = a.seed.x * 6.283;
                float ex = saturate(_SB_Excite);
                float jump = max(0, sin(t * (5 + a.seed.x * 4) + ph)) * (0.25 + ex * 1.6);
                float3 p = a.pos.xyz;
                p.y += jump * _Bob;
                p.y += a.seed.y * ex * 0.012 * max(0, sin(t * 9 + ph));   // 手臂往上举
                v.pos = TransformObjectToHClip(p);
                float3 n = TransformObjectToWorldNormal(a.n);
                half sh = 0.45 + 0.55 * saturate(dot(n, normalize(float3(0.2, 0.9, 0.3))));
                v.col = half4(a.col.rgb * sh, 1);
                return v;
            }
            half4 frag(V i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return i.col; }
            ENDHLSL
        }
    }
}
