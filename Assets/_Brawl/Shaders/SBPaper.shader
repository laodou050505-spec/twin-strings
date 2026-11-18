// 纸片：贴图 alpha 裁剪（抠掉背景，纸的外形就是图的外形），两面都画；受主光漫反射 + 环境光，背面略暗一点（纸透一点光）。双眼渲染。
Shader "StringBrawl/Paper"
{
    Properties { _MainTex ("纸面", 2D) = "white" {} _Color ("颜色", Color) = (1,1,1,1) }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; half4 _Color; CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.n = TransformObjectToWorldNormal(a.n); return v; }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                clip(c.a - 0.5);
                float3 n = normalize(front ? i.n : -i.n);
                Light L = GetMainLight();
                half diff = saturate(dot(n, L.direction)) * 0.75 + 0.35;
                half3 amb = SampleSH(n) * 0.6;
                return half4(c.rgb * (L.color * diff * (front ? 1 : 0.85) + amb), 1);
            }
            ENDHLSL
        }
    }
}
