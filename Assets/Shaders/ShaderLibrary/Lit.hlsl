#ifndef MYPIP_LIT_HLSL
#define MYPIP_LIT_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame)
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
CBUFFER_END

struct VertexInput
{
    float3 pos : POSITION;
    float3 normal : NORMAL;
};

struct VertexOutput
{
    float4 clipPos : SV_POSITION;
};

VertexOutput LitPassVertex(VertexInput input) : SV_Target 
{
    VertexOutput output = (VertexOutput)0;
    output.clipPos = mul(unity_ObjectToWorld, float4(input.pos, 1.0));
    output.clipPos = mul(unity_MatrixVP, output.clipPos);
    return output;
}

float4 LitPassFrag(VertexOutput input)
{
    return float4(1, 0, 0, 1);
}

#endif