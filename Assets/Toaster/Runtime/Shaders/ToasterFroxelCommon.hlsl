#ifndef TOASTER_FROXEL_COMMON_INCLUDED
#define TOASTER_FROXEL_COMMON_INCLUDED

// ============================================================
// Toaster Froxel Common — shared depth math & coordinate transforms
// Used by ToasterFroxel.compute and ToasterFroxelApply.shader
// ============================================================

// Froxel grid dimensions
int _FroxelResX;
int _FroxelResY;
int _FroxelResZ;
#define FroxelResolution int3(_FroxelResX, _FroxelResY, _FroxelResZ)

// Depth distribution params
float _FroxelNear;
float _FroxelFar;
float _DepthUniformity; // 0 = logarithmic, 1 = linear, 0.5 = blend

// Camera matrices
float4x4 _InvViewProj;
float4x4 _PrevViewProj;
float4 _ScreenParams_Froxel; // (width, height, 1/width, 1/height)
int _FrameIndex;

// Blue noise
Texture2D<float> _BlueNoise;
SamplerState sampler_BlueNoise;

// ============================================================
// Depth distribution (Frostbite SIGGRAPH 2015)
// ============================================================

// Slice [0..numSlices] → linear eye depth
float SliceToDepth(float slice)
{
    float t = slice / (float)_FroxelResZ;
    float linear = _FroxelNear + t * (_FroxelFar - _FroxelNear);
    float log_depth = _FroxelNear * pow(_FroxelFar / _FroxelNear, t);
    return lerp(log_depth, linear, _DepthUniformity);
}

// Linear eye depth → fractional slice [0..numSlices]
float DepthToSlice(float depth)
{
    float t_linear = (depth - _FroxelNear) / (_FroxelFar - _FroxelNear);
    float t_log = log(depth / _FroxelNear) / log(_FroxelFar / _FroxelNear);
    float t = lerp(t_log, t_linear, _DepthUniformity);
    return saturate(t) * (float)_FroxelResZ;
}

// ============================================================
// Coordinate transforms
// ============================================================

// Froxel integer coord → world position (center of froxel cell)
float3 FroxelToWorld(int3 coord, float jitter)
{
    // Screen UV from froxel XY
    float2 uv = ((float2)coord.xy + 0.5) / float2(_FroxelResX, _FroxelResY);

    // Depth from slice (with optional jitter along Z)
    float depth = SliceToDepth((float)coord.z + 0.5 + jitter);

    // Reconstruct clip space position
    // UV [0,1] → NDC [-1,1], flip Y for DX convention
    float2 ndc = uv * 2.0 - 1.0;
    #if UNITY_UV_STARTS_AT_TOP
    ndc.y = -ndc.y;
    #endif

    // Create clip-space point at the given depth
    // In Unity reversed-Z: near=1, far=0
    // We want a point at 'depth' linear eye distance
    // Use a far-plane point and scale by depth
    float4 clipFar = float4(ndc.x, ndc.y, 0.0, 1.0); // reversed-Z: far = 0
    float4 worldFar = mul(_InvViewProj, clipFar);
    worldFar.xyz /= worldFar.w;

    float4 clipNear = float4(ndc.x, ndc.y, 1.0, 1.0); // reversed-Z: near = 1
    float4 worldNear = mul(_InvViewProj, clipNear);
    worldNear.xyz /= worldNear.w;

    // Ray from camera through this pixel
    float3 camPos = worldNear.xyz; // at near plane ≈ camera
    float3 rayDir = normalize(worldFar.xyz - worldNear.xyz);

    return camPos + rayDir * depth;
}

// ============================================================
// Blue noise jitter
// ============================================================

float GetBlueNoiseJitter(int2 screenPos, int frame)
{
    // Animated blue noise: offset UV by golden ratio per frame
    float2 uv = ((float2)(screenPos % 128) + 0.5) / 128.0;
    uv += float2(0.7548776662, 0.5698402909) * (float)frame; // golden ratio offsets
    uv = frac(uv);
    return _BlueNoise.SampleLevel(sampler_BlueNoise, uv, 0).r;
}

#endif // TOASTER_FROXEL_COMMON_INCLUDED
