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
float3 _CameraPos;
float3 _CamForward;
int _FrameIndex;

// Blue noise
Texture2D _BlueNoise;
SamplerState sampler_BlueNoise;

// ============================================================
// Depth distribution (Frostbite SIGGRAPH 2015)
// ============================================================

// Slice [0..numSlices] → linear eye depth
float SliceToDepth(float slice)
{
    float t = slice / (float)_FroxelResZ;
    float linDepth = _FroxelNear + t * (_FroxelFar - _FroxelNear);
    float logDepth = _FroxelNear * pow(abs(_FroxelFar / _FroxelNear), t);
    return lerp(logDepth, linDepth, _DepthUniformity);
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

    // Linear eye depth from slice (with optional jitter along Z)
    float eyeDepth = SliceToDepth((float)coord.z + 0.5 + jitter);

    // UV [0,1] → NDC [-1,1], flip Y for DX convention
    float2 ndc = uv * 2.0 - 1.0;
    #if UNITY_UV_STARTS_AT_TOP
    ndc.y = -ndc.y;
    #endif

    // Unproject far-plane point to world space (reversed-Z: far = 0)
    float4 clipFar = float4(ndc.x, ndc.y, 0.0, 1.0);
    float4 worldFar = mul(_InvViewProj, clipFar);
    worldFar.xyz /= worldFar.w;

    // Ray from camera to this pixel's far-plane point (NOT normalized!)
    // The length encodes the perspective correction for this pixel.
    float3 ray = worldFar.xyz - _CameraPos;

    // Project ray onto camera forward to get the eye-depth at the far plane
    // Then scale ray so that the eye-depth component equals our desired depth
    float viewDepthAtFar = dot(ray, _CamForward);
    float t = eyeDepth / viewDepthAtFar;

    return _CameraPos + ray * t;
}

// ============================================================
// Blue noise jitter
// ============================================================

float GetBlueNoiseJitter(int2 screenPos, int frame)
{
    // Animated blue noise: offset UV by golden ratio per frame
    float2 uv = ((float2)((uint2)screenPos % 128u) + 0.5) / 128.0;
    uv += float2(0.7548776662, 0.5698402909) * (float)frame; // golden ratio offsets
    uv = frac(uv);
    return _BlueNoise.SampleLevel(sampler_BlueNoise, uv, 0).r;
}

#endif // TOASTER_FROXEL_COMMON_INCLUDED
