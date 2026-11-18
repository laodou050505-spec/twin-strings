// 木偶脚下的软影子：正片叠底（把台面压暗），中心深、边缘渐隐；_Strength 控制深浅（跳起来变淡）。双眼渲染。
Shader "StringBrawl/Shadow"
{
    Properties { _Strength ("深浅", Range(0, 1)) = 0.6 }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off Cull Off Blend DstColor Zero, Zero One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half _Strength; CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; return v; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half d = length(i.uv - 0.5) * 2;
                half s = saturate(1 - d); s = s * s * (3 - 2 * s);
                return half4((1 - s * _Strength).xxx, 1);
            }
            ENDHLSL
        }
    }
}
