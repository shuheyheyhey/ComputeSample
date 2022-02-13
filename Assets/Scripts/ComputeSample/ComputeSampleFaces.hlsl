#ifndef COMPUTE_SAMPLE_FACES_HLSL
#define COMPUTE_SAMPLE_FACES_HLSL

// Include helper functions from URP
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// This describes a vertex on the generated mesh, it should match that in the compute shader!
struct DrawVertex {
    float3 positionWS; // position in world space
    float2 uv; // UV
};
// A triangle
struct DrawTriangle {
    float3 normalWS; // normal in world space. All points share this normal
    DrawVertex vertices[3];
};
// The buffer to draw from
StructuredBuffer<DrawTriangle> _DrawTriangles;

// This structure is generated by the vertex function and passed to the geometry function
struct VertexOutput {
    float3 positionWS   : TEXCOORD0; // Position in world space
    float3 normalWS     : TEXCOORD1; // Normal vector in world space
    float2 uv           : TEXCOORD2; // UVs
    float4 positionCS   : SV_POSITION; // Position in clip space
};

// The _MainTex property. The sampler and scale/offset vector is also created
TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float4 _MainTex_ST;

// Returns the view direction in world space
float3 GetViewDirectionFromPosition(float3 positionWS) {
    return normalize(GetCameraPositionWS() - positionWS);
}

// If this is the shadow caster pass, we also need this variable, which URP sets
#ifdef SHADOW_CASTER_PASS
float3 _LightDirection;
#endif

// Calculates the position in clip space, taking into account various strategies
// to improve shadow quality in the shadow caster pass
float4 CalculatePositionCSWithShadowCasterLogic(float3 positionWS, float3 normalWS) {
    float4 positionCS;

    #ifdef SHADOW_CASTER_PASS
    // From URP's ShadowCasterPass.hlsl
    // If this is the shadow caster pass, we need to adjust the clip space position to account
    // for shadow bias and offset (this helps reduce shadow artifacts)
    positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
    #if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #else
    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #endif
    #else
    // This built in function transforms from world space to clip space
    positionCS = TransformWorldToHClip(positionWS);
    #endif

    return positionCS;
}

// Calculates the shadow texture coordinate for lighting calculations
float4 CalculateShadowCoord(float3 positionWS, float4 positionCS) {
    // Calculate the shadow coordinate depending on the type of shadows currently in use
    #if SHADOWS_SCREEN
    return ComputeScreenPos(positionCS);
    #else
    return TransformWorldToShadowCoord(positionWS);
    #endif
}

// Vertex functions

// The SV_VertexID semantic is an index we can use to get a vertex to work on
// The max value of this is the first argument in the indirect args buffer
// The system will create triangles out of each three consecutive vertices
VertexOutput Vertex(uint vertexID: SV_VertexID) {
    // Initialize the output struct
    VertexOutput output = (VertexOutput)0;

    // Get the vertex from the buffer
    // Since the buffer is structured in triangles, we need to divide the vertexID by three
    // to get the triangle, and then modulo by 3 to get the vertex on the triangle
    DrawTriangle tri = _DrawTriangles[vertexID / 3];
    DrawVertex input = tri.vertices[vertexID % 3];

    output.positionWS = input.positionWS;
    output.normalWS = tri.normalWS;
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    // Apply shadow caster logic to the CS position
    output.positionCS = CalculatePositionCSWithShadowCasterLogic(input.positionWS, tri.normalWS);

    return output;
}

// Fragment functions

// The SV_Target semantic tells the compiler that this function outputs the pixel color
float4 Fragment(VertexOutput input) : SV_Target {

#ifdef SHADOW_CASTER_PASS
    // If in the shadow caster pass, we can just return now
    // It's enough to signal that should will cast a shadow
    return 0;
#else
    // Initialize some information for the lighting function
    InputData lightingInput = (InputData)0;
    lightingInput.positionWS = input.positionWS;
    lightingInput.normalWS = input.normalWS; // No need to renormalize, since triangles all share normals
    lightingInput.viewDirectionWS = GetViewDirectionFromPosition(input.positionWS);
    lightingInput.shadowCoord = CalculateShadowCoord(input.positionWS, input.positionCS);

    // Read the main texture
    float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;

    // Call URP's simple lighting function
    // The arguments are lightingInput, albedo color, specular color, smoothness, emission color, and alpha
    SurfaceData surfaceData;
    
    surfaceData.albedo = albedo;
    surfaceData.alpha = 0.5;
    surfaceData.emission = 0;
    surfaceData.metallic = 1;
    surfaceData.occlusion = 1;
    surfaceData.smoothness = 1;
    surfaceData.specular = 0;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 1;
    surfaceData.normalTS = 1;
    return UniversalFragmentBlinnPhong(lightingInput, surfaceData);
#endif
}

#endif