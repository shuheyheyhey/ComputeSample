#ifndef COMPUTE_SHADER_HELPER
#define COMPUTE_SHADER_HELPER

float3 GetNormalFromTriangle(float3 a, float3 b, float3 c)
{
    return normalize(cross(b - a, c - a));
}

float3 GetTriangleCenter(float3 a, float3 b, float3 c)
{
    return (a + b + c) / 3.0;
}

float2 GetTriangleCenter(float2 a, float2 b, float2 c)
{
    return (a + b + c) / 3.0;
}

#endif