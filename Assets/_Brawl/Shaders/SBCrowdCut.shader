// 剪纸观众：所有观众合成一张网格（一次绘制）。贴图 alpha = 剪纸形状（镂空花纹是真透空，alpha 裁剪），顶点色 = 纸的颜色。
// uv2.x = 随机相位；全局 _SB_Excite（0..1）越高跳得越高越频繁（打中、KO 时脚本拉高再慢慢回落）；轻微左右摇摆（以脚底为轴）。
Shader "StringBrawl/CrowdCut"
{
    Properties { _MainTex ("剪纸图集", 2D) = "white" {} _Bob ("跳动高度（米）", Float) = 0.005 }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; float _Bob; CBUFFER_END
            float _SB_Excite;
            struct A { float4 pos : POSITION; float3 n : NORMAL; half4 col : COLOR; float2 uv : TEXCOORD0; float2 seed : TEXCOORD1; float2 foot : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float t = _Time.y, ph = a.seed.x * 6.283;
                float ex = saturate(_SB_Excite);
                float jump = max(0, sin(t * (4 + a.seed.x * 4) + ph)) * (0.2 + ex * 1.8);
                float3 p = a.pos.xyz;
                // 以脚底为轴左右摇摆：越往上摆得越多（foot.x = 离脚底的高度）
                float sway = sin(t * (1.6 + a.seed.x) + ph) * (0.04 + ex * 0.08);
                p.xz += a.foot.y * 0 + float2(cos(a.seed.y), sin(a.seed.y)) * a.foot.x * sway;
                p.y += jump * _Bob;
                v.pos = TransformObjectToHClip(p);
                v.uv = a.uv; v.col = a.col;
                return v;
            }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half al = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                clip(al - 0.5);
                // 背面：剪纸的反面是素纸，压暗成剪影（不显示镜像的花纹）
                return half4(front ? i.col.rgb : i.col.rgb * 0.35, 1);
            }
            ENDHLSL
        }
    }
}
