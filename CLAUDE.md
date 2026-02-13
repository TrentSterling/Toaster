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

- `Assets/Toaster/Runtime/Toaster.cs` — Namespace root (`namespace Toaster`), static `Appliance` class, version info, `BrowningLevel` enum, utility logging
- `Assets/Toaster/Runtime/VoxelBaker.cs` — C# orchestrator: sets up voxel grid (3D RenderTexture), iterates renderers/submeshes, executes Meta Pass via CommandBuffer, dispatches compute. `[ContextMenu("Bake Voxels")]`
- `Assets/Toaster/Runtime/Shaders/Voxelizer.compute` — HLSL compute shader: `VoxelizeMesh` kernel (triangle-box intersection, barycentric UV interpolation, voxel grid writes) + `ClearGrid` kernel
- `Assets/Toaster/Runtime/Shaders/ToasterVolume.shader` — URP volumetric fog raymarcher. Ray-box intersection → front-to-back compositing. Blend One OneMinusSrcAlpha, Cull Front, ZWrite Off
- `Assets/Toaster/Runtime/Shaders/ToasterDebugSlice.shader` — Debug shader: renders a flat 2D Z-slice of the 3D voxel texture on a quad
- `Assets/Toaster/Runtime/Shaders/ToasterDebugHeatmap.shader` — False-color heatmap with channel isolation and grid overlay
- `Assets/Toaster/Runtime/Shaders/ToasterDebugIsosurface.shader` — Raymarched solid surfaces with gradient-estimated normals and lighting
- `Assets/Toaster/Runtime/Shaders/ToasterDebugMultiSlice.shader` — Hologram-style multi-slice Y-plane visualizer with animation
- `Assets/Toaster/Runtime/Shaders/ToasterDebugPointCloud.shader` — Billboard per occupied voxel, driven by VoxelPointCloudRenderer.cs
- `Assets/Toaster/Runtime/VoxelPointCloudRenderer.cs` — Component to configure point cloud shader properties from baker results
- `Assets/Toaster/Editor/ToasterDemoSetup.cs` — Editor menu items: "Toaster > Create Demo Scene" and "Toaster > Create Demo Scene & Bake". Auto-creates camera, lights, test geometry, baker, all visualizers
- `Assets/Toaster/TODO.md` — Project roadmap and task tracking
- `Assets/Toaster/FUTURE.md` — Advanced techniques research (brickmaps, radiance cascades, VXGI, SVO, etc.)

## Tech Stack

- Unity (URP/HDRP compatible — requires materials with Meta Pass)
- Compute Shaders (HLSL)
- CommandBuffer API for Meta Pass rendering
- RenderTexture (Tex3D) for voxel grid storage

## Conventions

- Namespace: `Toaster`
- Version: `0.1 (Crumb)`
- Browning levels: Raw (low res), Light (med res), Burnt (high res)
- Log prefix: `[TOASTER]`

## Notes

- Meshes need UVs (preferably UV2/lightmap UVs for non-overlapping coverage)
- Materials need a Meta Pass (Standard, URP Lit, HDRP Lit have this built-in; custom Shader Graphs need lightmap static enabled)
- The AABB intersection test is simplified (no full SAT); works well with small voxels
- Race conditions on voxel writes (multiple triangles hitting same voxel) are accepted — last write wins
- UV interpolation in compute shader uses barycentric projection of voxel center onto triangle plane
- Buffer management is per-object (recreated each iteration). Production optimization: one giant buffer or pooling
- Voxel grid visualization requires a custom raymarcher shader — Gizmos can only show the bounding box
- The "Submesh Dispatch" method: CPU finds Meta pass index, CommandBuffer renders to temp texture, Compute dispatches per-triangle and samples that texture
- Rejected approaches: Gemini suggested slicing the scene (produced "8 slices of sourdough bread"); Jobs + Physics.CheckSphere considered but too slow and hard to get UV/color data back to CPU

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
