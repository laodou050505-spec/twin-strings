// 八角笼的铁丝网：菱形网格直接在着色器里画（不用贴图），网眼镂空（透明裁剪），双面；简单的明暗。
Shader "StringBrawl/Fence"
{
    Properties { _Color ("铁丝颜色", Color) = (0.12, 0.12, 0.13, 1) _Cells ("网眼数（横）", Float) = 14 _Wire ("铁丝粗细", Range(0.02, 0.4)) = 0.12 _Aspect ("高 / 宽", Float) = 0.6 }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Color; float _Cells, _Wire, _Aspect; CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half sh : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv;
                float3 n = TransformObjectToWorldNormal(a.n);
                v.sh = 0.55 + 0.45 * abs(dot(n, normalize(float3(0.3, 0.8, 0.5))));
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 g = float2(i.uv.x, i.uv.y * _Aspect) * _Cells;
                float2 d = float2(g.x + g.y, g.x - g.y);          // 转 45°：菱形网眼
                float2 f = abs(frac(d) - 0.5);
                float wire = step(0.5 - _Wire, max(f.x, f.y));
                clip(wire - 0.5);
                return half4(_Color.rgb * i.sh, 1);
            }
            ENDHLSL
        }
    }
}
