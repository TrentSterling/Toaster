# Toaster

A lightweight Unity voxelizer for detached volumetric lighting, replacing Bakery.

## Project Overview

Toaster is a custom lightmapping pipeline that separates material sampling from geometry voxelization. The name comes from the idea that Bakery takes hours; a Toaster takes minutes.

## Architecture

The pipeline has three stages:

1. **Bake Materials (Pre-Pass):** Render each object's Meta Pass into a temporary 2D render texture to extract albedo + emission laid out in UV space. This runs once per object, not per voxel.
2. **Voxelize:** A compute shader iterates per-triangle (not per-voxel), finds intersecting voxels via AABB tests, interpolates UVs with barycentric coordinates, samples the pre-pass texture, and writes to a `RWTexture3D` voxel grid.
3. **Trace (Path Tracer):** For each voxel, shoot rays out. If a ray hits a light, add light color. If it hits a solid voxel, sample that voxel's albedo from the AlbedoGrid and bounce again. Accumulate the result into a `LightingGrid` (`RWTexture3D`).
4. **Runtime (Fog Shader):** A raymarcher samples the baked `LightingGrid` 3D texture. No SH decoding — just raw `tex3D` color lookup.

Key design decisions:
- **Triangle-based voxelization** (dispatch per triangle, find voxels) instead of voxel-based (per voxel, check sphere). Much faster and avoids Physics API overhead.
- **Meta Pass for material data** — Unity shaders expose albedo/emission via the Meta pass. A `CommandBuffer` renders objects through this pass into a temp RT, decoupling material evaluation from voxelization.
- **Compute shader** for GPU voxelization instead of CPU `Physics.CheckSphere`.
- **L0 Ambient packing** instead of Bakery's L1 Spherical Harmonics. Bakery uses 3 textures to encode SH L1 (directional lighting). Toaster uses 1 `RGBAHalf` 3D texture with isotropic (ambient) lighting — deletes 75% of data and 90% of decode math. Trade-off: light is "flat" (no directional response), which is fine for ambient fog/glow.

## Lighting Data Format

Single `Texture3D` (`RGBAHalf`):
- R: Light Red
- G: Light Green
- B: Light Blue
- A: Density (or Occlusion)

Written as: `LightingGrid[id] = float4(AccumulatedLight.rgb, VoxelDensity);`

## Key Files

### Core Pipeline
- `Assets/Toaster/Runtime/Toaster.cs` — Namespace root, static `Appliance` class, version `1.2 (Crumb)`, `BrowningLevel` enum, utility logging
- `Assets/Toaster/Runtime/VoxelBaker.cs` — C# orchestrator: voxel grid setup, Meta Pass via CommandBuffer, compute dispatch, auto-wire visualizers, auto-fit bounds, serialization, incremental bake, buffer pooling
- `Assets/Toaster/Runtime/Shaders/Voxelizer.compute` — Kernels: `VoxelizeMesh` (SAT triangle-box, atomic accum), `ClearGrid`, `ClearAccum`, `FinalizeGrid`
- `Assets/Toaster/Runtime/ToasterTracer.cs` — Path tracer orchestrator: gathers scene lights, uploads GPU buffers, dispatches trace
- `Assets/Toaster/Runtime/Shaders/ToasterTracer.compute` — Kernels: `TraceLight` (Halton sampling, DDA march, multi-bounce, shadow rays), `ClearLighting`

### Froxel Fog Pipeline (v0.8+)
- `Assets/Toaster/Runtime/ToasterFroxelFeature.cs` — ScriptableRendererFeature: settings, references, pass lifecycle
- `Assets/Toaster/Runtime/ToasterFroxelPass.cs` — RenderGraph pass: RTHandle 3D textures, compute dispatch, volume data upload, fullscreen apply
- `Assets/Toaster/Runtime/Shaders/ToasterFroxelCommon.hlsl` — Shared HLSL: Frostbite depth distribution, FroxelToWorld, blue noise jitter
- `Assets/Toaster/Runtime/Shaders/ToasterFroxel.compute` — Kernels: `ClearFroxels`, `InjectMedia` (multi-volume + temporal), `IntegrateFroxels` (Beer-Lambert)
- `Assets/Toaster/Runtime/Shaders/ToasterFroxelApply.shader` — Fullscreen triangle composite (Blend One SrcAlpha, depth-aware)

### Shaders
- `Assets/Toaster/Runtime/Shaders/ToasterVolume.shader` — URP volumetric fog raymarcher with optional _LightingTex toggle and additive blend (legacy fallback)
- `Assets/Toaster/Runtime/Shaders/ToasterDebugSlice.shader` — Z-slice debug quad
- `Assets/Toaster/Runtime/Shaders/ToasterDebugHeatmap.shader` — False-color heatmap with channel isolation
- `Assets/Toaster/Runtime/Shaders/ToasterDebugIsosurface.shader` — Raymarched surfaces with gradient normals
- `Assets/Toaster/Runtime/Shaders/ToasterDebugMultiSlice.shader` — Hologram-style Y-plane slices
- `Assets/Toaster/Runtime/Shaders/ToasterDebugPointCloud.shader` — Procedural billboard point cloud
- `Assets/Toaster/Runtime/Shaders/ToasterSample.hlsl` — Shader Graph Custom Function include (ToasterSampleVolume, ToasterSampleLighting, ToasterSampleFog)

### Components & Editor
- `Assets/Toaster/Runtime/ToasterVolume.cs` — Volume component: static `ActiveVolumes` registry, `LightingGrid` property, per-volume froxel density/intensity/edgeFalloff
- `Assets/Toaster/Runtime/VoxelPointCloudRenderer.cs` — Graphics.DrawProcedural point cloud renderer
- `Assets/Toaster/Editor/ToasterDemoSetup.cs` — "Toaster > Create Demo Scene" — corridor with pillars, neon emissives, dramatic lighting
- `Assets/Toaster/Editor/ToasterEditorWindow.cs` — "Toaster > Baker Window" — bake/trace buttons, grid stats, auto-fit, froxel setup, preview
- `Assets/Toaster/Editor/BlueNoiseGenerator.cs` — "Toaster > Generate Blue Noise Texture" — 128x128 R8 Mitchell's best candidate
- `Assets/Toaster/TODO.md` — Project roadmap and task tracking
- `Assets/Toaster/FUTURE.md` — Advanced techniques research

## Tech Stack

- Unity (URP/HDRP compatible — requires materials with Meta Pass)
- Compute Shaders (HLSL)
- CommandBuffer API for Meta Pass rendering
- RenderTexture (Tex3D) for voxel grid storage

## Conventions

- Namespace: `Toaster`
- Version: `1.2 (Crumb)`
- Browning levels: Raw (low res), Light (med res), Burnt (high res)
- Log prefix: `[TOASTER]`

## Notes

- Meshes need UVs (preferably UV2/lightmap UVs for non-overlapping coverage)
- Materials need a Meta Pass (Standard, URP Lit, HDRP Lit have this built-in; custom Shader Graphs need lightmap static enabled)
- Full SAT (Separating Axis Theorem) triangle-box intersection test (13 axes) since v0.4
- Atomic accumulation buffer (InterlockedAdd) eliminates race conditions on voxel writes since v0.3
- UV interpolation in compute shader uses barycentric projection of voxel center onto triangle plane
- Buffer pooling (EnsureBuffer pattern) reuses ComputeBuffers across objects since v0.6
- Voxel grid visualization requires a custom raymarcher shader — Gizmos can only show the bounding box
- The "Submesh Dispatch" method: CPU finds Meta pass index, CommandBuffer renders to temp texture, Compute dispatches per-triangle and samples that texture
- Rejected approaches: Gemini suggested slicing the scene (produced "8 slices of sourdough bread"); Jobs + Physics.CheckSphere considered but too slow and hard to get UV/color data back to CPU

## Froxel Fog Pipeline (v0.8+, refined v1.1)

Screen-space frustum-aligned 3D grid with physically-based Beer-Lambert integration.

### Architecture: 4-Stage Pipeline
1. **ClearFroxels** — Zero out the scattering grid
2. **InjectMedia** — For each froxel: reconstruct world position, check which volumes contain it, sample baked lighting grids, evaluate scene lights (point/spot/directional with Henyey-Greenstein phase), compute extinction and in-scattered light. Includes temporal reprojection (EMA blend with previous frame).
3. **IntegrateFroxels** — Front-to-back per-column (x,y): walk Z slices, accumulate Beer-Lambert transmittance and energy-conserving in-scattering `(1-exp(-σd))/σ`.
4. **Apply** — Fullscreen triangle composite (Blend One SrcAlpha, depth-aware). Supports 5 debug modes.

### Density Model (v1.1)
- `fogDensity` slider (0–0.2) = direct extinction coefficient (absorption per meter)
- Sent raw to GPU — no conversion. Per-volume `densityMultiplier` scales it.
- At fogDensity=0.03, densityMultiplier=3: effective extinction=0.09 → `exp(-0.09*24)=0.12` → 88% opacity through 24m corridor
- At fogDensity=0.03, densityMultiplier=2: effective extinction=0.06 → `exp(-0.06*16)=0.38` → 62% opacity through 16m courtyard
- Default 0.03 is the known-working baseline; per-volume multipliers handle variation

### Light-Localized Density (v1.1)
- `lightDensityBoost` adds extra extinction near point/spot lights
- Uses URP-style smooth distance attenuation + spot cone mask
- Weighted by light intensity — brighter lights create denser fog halos
- Additive to base density: `extinction += lightBoost * _LightDensityBoost * _FogDensity`

### Height Fog (v1.1)
- `enableHeightFog` + `heightFogBase`/`heightFogTop` controls
- Density multiplied by `1 - sqrt(t)` where `t = saturate((y - base) / (top - base))`
- sqrt falloff gives denser fog at ground level, tapering above

### Per-Volume Controls
- `densityMultiplier` — per-volume density scale (in VolumeGPUData.settings.x)
- `intensityMultiplier` — per-volume light color scale (in VolumeGPUData.settings.y)
- `edgeFalloff` (0-1) — fades density near volume boundaries for smooth overlap

### Debug Modes
0=Off, 1=Scattering (raw in-scattered light), 2=Extinction (density field), 3=Transmittance (blue→green→red gradient), 4=DepthSlices (log depth visualization)

## Reference: SLZ Custom-URP Volumetrics

Repo: https://github.com/StressLevelZero/Custom-URP (Unity 2021.2, URP 8148.0.7-5)

Techniques worth stealing for Toaster's evolution:
- **Froxel grid** with logarithmic depth slicing (Frostbite SIGGRAPH 2015 method)
- **Clipmap compositing** — two cascades (near 80m / far 160m) merge baked volumes into camera-centered 3D textures, rebuilt when camera moves >1m
- **Temporal reprojection** on froxel scattering — exponential moving average with depth-dependent weight (near = responsive, far = stable)
- **Mip Sky Fog** (Uncharted 4 technique) — sample sky cubemap at increasing mip levels with distance for atmospheric perspective
- **Height fog density** — remap world Y between base/max height, sqrt falloff
- **Front-to-back integration** (StepAdd) — `transmittance = exp(-extinction * travelDist)`, accumulate in-scattered light weighted by remaining transmittance
- **DXR baking path** — hardware ray tracing for shadow rays during offline bake (with software Moller-Trumbore fallback)
- **Data format**: `R16G16B16A16_SFloat` everywhere, froxel grid default 128x128x64
- **VR/Stereo**: double-width integration buffer, IPD-based parallax

## Reference: Brickmap Demo

User has a brickmap demo project somewhere on disk (will locate later). It's a GPU sparse brick map architecture — two-level indirection texture mapping logical chunks to physical atlas locations. Relevant for efficient sparse voxel storage if Toaster needs to scale to large worlds.
