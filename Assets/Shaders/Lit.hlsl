#ifndef MYPIP_LIT_HLSL
#define MYPIP_LIT_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame)
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
CBUFFER_END

sampler2D 	_MainTex;
float4 _MainTex_TexelSize;

#define UNITY_MATRIX_M unity_ObjectToWorld

//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

/*
UNITY_INSTANCING_BUFFER_START(PerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)
*/

/*
#define MAX_VISIBLE_LIGHTS 16
CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END
*/

struct Light
{
    float3 pos;
    float3 direction;
    float4 color;
    float spotAngle;
    float range;
    uint type;
};

#define LIGHT_TYPE_SPOT             0
#define LIGHT_TYPE_DIRECTIONAL      1
#define LIGHT_TYPE_POINT            2


StructuredBuffer<Light> g_lights;
StructuredBuffer<uint> g_lightIndexList;
StructuredBuffer<uint2> g_lightGrid;


uint3 cb_clusterCount;
uint3 cb_clusterSize;
float4 cb_screenSize;

real4 unity_LightIndices[2];

struct VertexInput
{
    float3 pos : POSITION;
    float3 normal : NORMAL;
    float2 uv     : TEXCOORD0;
    //UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput
{
    float4 clipPos : SV_POSITION;
    float2 uv      : TEXCOORD0;
    float3 normal  : TEXCOORD1;
    float3 worldPos: TEXCOORD2;
    //UNITY_VERTEX_INPUT_INSTANCE_ID
};

uint3 computeClusterIndex(uint3 clusterIndex3D)
{
    return clusterIndex3D.x + (cb_clusterCount.x * (clusterIndex3D.y + cb_clusterCount.y * clusterIndex3D.z));
}

/*
float3 diffuseLighting(int lightIndex, float3 normal)
{
    float3 lightColor = _VisibleLightColors[lightIndex].rgb;
    float3 lightDir = _VisibleLightDirections[lightIndex].xyz;
    float nDotL = max(0, dot(normal, lightDir));
    return nDotL * lightColor;
}
*/


float3 diffuseLighting(float3 lightColor, float3 lightDir, float3 normal, float factor)
{
    float nDotL = max(0, dot(normal, lightDir));
    return nDotL * lightColor * factor;
} 

VertexOutput LitPassVertex(VertexInput input)
{
    VertexOutput output;
    //UNITY_SETUP_INSTANCE_ID(input);
    //UNITY_TRANSFER_INSTANCE_ID(input, output);
    output.worldPos = mul(UNITY_MATRIX_M, float4(input.pos, 1.0)).xyz;
    output.clipPos = mul(unity_MatrixVP, float4(output.worldPos, 1.0));
    output.normal = mul(UNITY_MATRIX_M, float4(input.normal, 0.0)).xyz;
    output.uv = input.uv;
    return output;
}

float4 LitPassFrag(VertexOutput input) : SV_Target
{
    //UNITY_SETUP_INSTANCE_ID(input);
    
    float3 tex = tex2D(_MainTex, input.uv).rgb;
    float3 normal = normalize(input.normal);
    uint3 screenPos = input.clipPos.xyw;
    screenPos = screenPos / cb_clusterSize;
    
    uint index = computeClusterIndex(screenPos);
    
    uint2 lightGrid = g_lightGrid[index];
    float4 color = float4(0, 0, 0, 1);
    
    for (uint i = 0; i < lightGrid.y; ++i)
    {
        uint lightIndex = g_lightIndexList[lightGrid.x + i];
        Light light = g_lights[lightIndex];
        switch (light.type)
        {
        case LIGHT_TYPE_DIRECTIONAL:
            {
                float3 lightDir = -light.direction;
                color.xyz += diffuseLighting(light.color.xyz, lightDir, normal, 1);
            } break;
        case LIGHT_TYPE_SPOT:
            {
                
                float3 lightPos = light.pos;
                float radius = light.range;
                float3 tolight = normalize(lightPos - input.worldPos);
                float3 lightDir = normalize(light.direction);
                float disToLight = distance(lightPos, input.worldPos);
                float cosAngle = dot(-tolight, lightDir);
                
                float outer = 0.5 * light.spotAngle;
                float inner = 0.5 * light.spotAngle * 0.4;
                float cosOuter = cos(radians(outer));
                float cosInner = cos(radians(inner));
                
                float factor = (1 - saturate(disToLight/ radius)) * saturate((cosAngle - cosOuter) / cosInner);
                color.xyz += diffuseLighting(light.color.xyz, tolight, normal, factor);
            } break; 
        case LIGHT_TYPE_POINT:
            {
                float3 lightPos = light.pos;
                float radius = light.range ;
                float3 lightDir = normalize(lightPos - input.worldPos);
                
                float disToLight = distance(lightPos, input.worldPos);
                float factor = 1 - saturate(disToLight/ radius);
        
                color.xyz += diffuseLighting(light.color.xyz, lightDir, normal, factor);
            } break;
            
        }
    }
    color.xyz *= tex;
    return color ;
    
    
    /*
    float3 tex = tex2D(_MainTex, input.uv).rgb;
    float3 normal = normalize(input.normal);
    float3 diffuseLight = 0;
    for (int i = 0; i < MAX_VISIBLE_LIGHTS; ++i)
    {
        diffuseLight += diffuseLighting(i, normal);
    }
    float3 color = tex * diffuseLight * UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
    return float4(color, 1);
    */
}

#endif