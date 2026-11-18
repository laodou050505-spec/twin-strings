// 描边（选人时标出选中的木偶）。只描最外圈轮廓，身体内部（部件和部件之间）不出线。
// 一个着色器当两种材质用：
//   ① 占位：照原样画一遍木偶，但不出颜色，只在模板缓冲里记下“这些像素是木偶”；
//   ② 描边：把模型沿法线撑大一圈再画，只在“不是木偶”的像素上出颜色——露出来的就只有最外面那一圈。
Shader "StringBrawl/Outline"
{
    Properties
    {
        _Color ("颜色", Color) = (1, 1, 1, 1)
        _Width ("宽度（米）", Float) = 0.0016
        [HideInInspector] _SComp ("模板比较", Float) = 8
        [HideInInspector] _SPass ("模板通过后", Float) = 0
        [HideInInspector] _CMask ("颜色写入", Float) = 15
        [HideInInspector] _CullM ("剔除", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+5" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull [_CullM] ZWrite Off ZTest LEqual ColorMask [_CMask]
            Stencil { Ref 1 Comp [_SComp] Pass [_SPass] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Color; float _Width; float _SComp; float _SPass; float _CMask; float _CullM; CBUFFER_END
            struct A { float4 pos : POSITION; float3 nrm : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float3 ws = TransformObjectToWorld(a.pos.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(a.nrm));
                v.pos = TransformWorldToHClip(ws + n * _Width);
                return v;
            }
            half4 frag(V i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return half4(_Color.rgb, 1); }
            ENDHLSL
        }
    }
}
