# Toaster

**GPU voxelizer + path tracer for Unity 6 URP.**
Bakery takes hours. A Toaster takes minutes.

Toaster is a lightweight volumetric lighting pipeline that voxelizes your scene on the GPU, traces light bounces through the voxel grid, and renders the result as raymarched fog — all from a single compute shader dispatch. No baked lightmaps, no SH decoding, no waiting.

## Pipeline

```
Meta Pass ──> Voxelize ──> Trace ──> Render
(material)   (compute)   (path)   (fog vol)
```

1. **Bake Materials** — CommandBuffer renders each object's Meta Pass into a temp RT to extract albedo + emission in UV space
2. **Voxelize** — Compute shader dispatches one thread per triangle, finds intersecting voxels via full SAT test (13 axes), writes to a `RWTexture3D` through an atomic accumulation buffer
3. **Trace** — Path tracer shoots N rays per occupied voxel using Halton sequences. DDA grid marching, shadow rays, multi-bounce color bleeding. Writes to a separate `LightingGrid`
4. **Render** — Volume fog shader raymarches the baked 3D texture. Front-to-back compositing, optional lighting grid blending

## Features

- **One-click demo** — `Toaster > Create Demo Scene && Bake` sets up geometry, lights, baker, tracer, and 6 visualizers
- **BrowningLevel presets** — Raw (1.0), Light (0.5), Burnt (0.25) voxel sizes
- **Auto-wire** — After bake, all Toaster visualizers get the grid automatically
- **Auto-fit bounds** — Compute scene AABB from static renderers
- **Serialization** — Baked grid saved as Texture3D asset, survives domain reloads
- **Incremental bake** — Only re-voxelize objects that moved
- **LOD mipmaps** — Distance-based quality falloff
- **Buffer pooling** — Reuses ComputeBuffers across objects
- **Shader Graph** — HLSL include with 3 custom function nodes

## Debug Visualizers

| Visualizer | What It Shows |
|---|---|
| **Slice** | Single Z-slice of the 3D texture |
| **Heatmap** | False-color density map with channel isolation |
| **Isosurface** | Raymarched solid surfaces with gradient normals |
| **Multi-Slice** | Hologram-style Y-plane slices with animation |
| **Point Cloud** | Procedural billboard per occupied voxel |
| **Volume Fog** | Production raymarched volumetric fog |

## Quick Start

1. Open project in **Unity 6** (URP 17)
2. Menu: **Toaster > Create Demo Scene && Bake**
3. Done. Scene populates with test geometry, bakes voxels, traces lighting, wires all visualizers

For manual setup:
1. Add a `VoxelBaker` component, assign `Voxelizer.compute`
2. Mark geometry as **Contribute GI** static
3. Right-click component > **Bake Voxels**
4. Add a `ToasterTracer`, assign `ToasterTracer.compute`, link the baker
5. Right-click > **Trace Lighting**

## Editor Window

**Toaster > Baker Window** — one-click bake, trace, auto-fit bounds, wire visualizers, grid stats (resolution, memory, serialization status), texture preview.

## Shader Graph Integration

Drop `ToasterSample.hlsl` into a Custom Function node:

- `ToasterSampleVolume` — Sample albedo grid at world position
- `ToasterSampleLighting` — Sample traced lighting at world position
- `ToasterSampleFog` — Pre-multiplied fog color+alpha for direct blending

## Architecture

```
Assets/Toaster/
  Runtime/
    Toaster.cs                  # Namespace root, version, logging
    VoxelBaker.cs               # Orchestrator — Meta Pass + compute dispatch
    ToasterTracer.cs            # Path tracer orchestrator
    VoxelPointCloudRenderer.cs  # Procedural point cloud driver
    Shaders/
      Voxelizer.compute         # VoxelizeMesh, ClearGrid, ClearAccum, FinalizeGrid
      ToasterTracer.compute     # TraceLight, ClearLighting
      ToasterVolume.shader      # Production fog raymarcher
      ToasterDebug*.shader      # 5 debug visualizers
      ToasterSample.hlsl        # Shader Graph include
  Editor/
    ToasterDemoSetup.cs         # Menu items for demo scene creation
    ToasterEditorWindow.cs      # Baker window UI
```

## Tech

- Unity 6 URP 17.0.4
- Compute shaders (HLSL)
- CommandBuffer API for Meta Pass
- `RWTexture3D` (ARGBHalf) for voxel storage
- Atomic `InterlockedAdd` on fixed-point accumulation buffer
- Full SAT triangle-box intersection (Akenine-Moller 2001)
- Halton sequence sampling + Wang hash seeding
- DDA voxel grid ray marching

## Status

**v0.7 (Crumb)** — Full pipeline working + audit hardening. See [TODO.md](Assets/Toaster/TODO.md) for remaining work and [FUTURE.md](Assets/Toaster/FUTURE.md) for advanced techniques research (brickmaps, radiance cascades, froxels, etc).

## License

MIT

---

Built by [Trent Sterling](https://tront.xyz)
