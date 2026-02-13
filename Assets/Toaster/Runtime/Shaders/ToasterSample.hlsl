// ToasterSample.hlsl
// HLSL include for Shader Graph Custom Function nodes.
//
// Usage in Shader Graph:
// 1. Add a Custom Function node
// 2. Set Source to "File" and point to this file
// 3. Set Function Name to one of the functions below
// 4. Connect inputs/outputs as described
//
// Functions:
//   ToasterSampleVolume    — Sample the baked voxel grid at a world position
//   ToasterSampleLighting  — Sample the traced lighting grid at a world position
//   ToasterSampleFog       — Full fog integration (density + color) for custom shaders

#ifndef TOASTER_SAMPLE_INCLUDED
#define TOASTER_SAMPLE_INCLUDED

// --- ToasterSampleVolume ---
// Inputs:
//   WorldPos (Vector3) — world-space position to sample
//   BoundsMin (Vector3) — minimum corner of the voxel grid
//   BoundsMax (Vector3) — maximum corner of the voxel grid
//   VolumeTex (Texture3D) — the baked voxel grid texture
//   VolumeSampler (SamplerState) — sampler for the volume texture
//   LOD (Float) — mipmap level (0 = full res)
// Outputs:
//   Color (Vector3) — sampled RGB color
//   Density (Float) — sampled alpha/density
void ToasterSampleVolume_float(
    float3 WorldPos,
    float3 BoundsMin,
    float3 BoundsMax,
    Texture3D VolumeTex,
    SamplerState VolumeSampler,
    float LOD,
    out float3 Color,
    out float Density)
{
    float3 uvw = (WorldPos - BoundsMin) / (BoundsMax - BoundsMin);

    // Clamp UVW to [0,1] to prevent sampling outside the grid
    uvw = saturate(uvw);

    float4 texSample = SAMPLE_TEXTURE3D_LOD(VolumeTex, VolumeSampler, uvw, LOD);
    Color = texSample.rgb;
    Density = texSample.a;
}

// Half-precision version (for mobile / performance)
void ToasterSampleVolume_half(
    half3 WorldPos,
    half3 BoundsMin,
    half3 BoundsMax,
    Texture3D VolumeTex,
    SamplerState VolumeSampler,
    half LOD,
    out half3 Color,
    out half Density)
{
    half3 uvw = (WorldPos - BoundsMin) / (BoundsMax - BoundsMin);
    uvw = saturate(uvw);
    half4 texSample = SAMPLE_TEXTURE3D_LOD(VolumeTex, VolumeSampler, uvw, LOD);
    Color = texSample.rgb;
    Density = texSample.a;
}

// --- ToasterSampleLighting ---
// Same as above but specifically named for the lighting grid
void ToasterSampleLighting_float(
    float3 WorldPos,
    float3 BoundsMin,
    float3 BoundsMax,
    Texture3D LightingTex,
    SamplerState LightingSampler,
    float LOD,
    out float3 LightColor,
    out float Density)
{
    float3 uvw = (WorldPos - BoundsMin) / (BoundsMax - BoundsMin);
    uvw = saturate(uvw);
    float4 texSample = SAMPLE_TEXTURE3D_LOD(LightingTex, LightingSampler, uvw, LOD);
    LightColor = texSample.rgb;
    Density = texSample.a;
}

void ToasterSampleLighting_half(
    half3 WorldPos,
    half3 BoundsMin,
    half3 BoundsMax,
    Texture3D LightingTex,
    SamplerState LightingSampler,
    half LOD,
    out half3 LightColor,
    out half Density)
{
    half3 uvw = (WorldPos - BoundsMin) / (BoundsMax - BoundsMin);
    uvw = saturate(uvw);
    half4 texSample = SAMPLE_TEXTURE3D_LOD(LightingTex, LightingSampler, uvw, LOD);
    LightColor = texSample.rgb;
    Density = texSample.a;
}

// --- ToasterSampleFog ---
// Convenience: returns a pre-multiplied fog color+alpha for direct blending
// Can be used in Shader Graph with a Blend node
void ToasterSampleFog_float(
    float3 WorldPos,
    float3 BoundsMin,
    float3 BoundsMax,
    Texture3D VolumeTex,
    SamplerState VolumeSampler,
    float Intensity,
    float DensityMultiplier,
    out float4 FogColorAlpha)
{
    float3 uvw = (WorldPos - BoundsMin) / (BoundsMax - BoundsMin);

    // Outside the volume? No fog
    if (any(uvw < 0) || any(uvw > 1))
    {
        FogColorAlpha = float4(0, 0, 0, 0);
        return;
    }

    float4 texSample = SAMPLE_TEXTURE3D_LOD(VolumeTex, VolumeSampler, uvw, 0);
    float3 color = texSample.rgb * Intensity;
    float density = saturate(texSample.a * DensityMultiplier);

    FogColorAlpha = float4(color * density, density);
}

void ToasterSampleFog_half(
    half3 WorldPos,
    half3 BoundsMin,
    half3 BoundsMax,
    Texture3D VolumeTex,
    SamplerState VolumeSampler,
    half Intensity,
    half DensityMultiplier,
    out half4 FogColorAlpha)
{
    half3 uvw = (WorldPos - BoundsMin) / (BoundsMax - BoundsMin);

    if (any(uvw < 0) || any(uvw > 1))
    {
        FogColorAlpha = half4(0, 0, 0, 0);
        return;
    }

    half4 texSample = SAMPLE_TEXTURE3D_LOD(VolumeTex, VolumeSampler, uvw, 0);
    half3 color = texSample.rgb * Intensity;
    half density = saturate(texSample.a * DensityMultiplier);

    FogColorAlpha = half4(color * density, density);
}

#endif // TOASTER_SAMPLE_INCLUDED
