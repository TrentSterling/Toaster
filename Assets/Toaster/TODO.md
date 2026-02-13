# Toaster TODO

## Current Status: v0.6 (Crumb)
Working: Full pipeline — Material bake (Meta Pass) + GPU voxelization + path tracer + volume raymarcher + 6 debug visualizers + editor window + Shader Graph integration.

---

## Completed (v0.1 → v0.6)

### Bug Fixes
- [x] Meta Pass UV1 support — injects UV1 = UV0 for readable meshes without lightmap UVs, _BaseColor fill fallback
- [x] Object filtering — skips Toaster/* shader materials and objects without ContributeGI static flag
- [x] Race conditions on voxel writes — atomic accumulation buffer (InterlockedAdd on fixed-point RGBA), FinalizeGrid kernel averages
- [x] Voxel grid serialized as Texture3D asset, survives domain reloads

### Polish
- [x] Point cloud visualizer — Graphics.DrawProcedural, no mesh needed
- [x] Auto-wire all visualizers after bake (finds Toaster/* shaders + point clouds)
- [x] Range attributes, tooltips, HideInInspector on VoxelBaker fields
- [x] BrowningLevel presets auto-set voxelSize (Raw=1.0, Light=0.5, Burnt=0.25)

### Features
- [x] Stage 3 Path Tracer (ToasterTracer.compute) — Halton sequence, DDA marching, multi-bounce, shadow rays
- [x] Runtime fog integration — _LightingTex toggle, additive blend mode
- [x] Full SAT triangle-box intersection (13 axes)
- [x] Editor window (Toaster > Baker Window) — inline inspector, bake/trace buttons, grid stats, preview
- [x] Buffer pooling (EnsureBuffer reuse pattern)
- [x] LOD mipmaps + incremental bake (dirty tracking)
- [x] Shader Graph integration (ToasterSample.hlsl — 3 custom function nodes)
- [x] Auto-fit bounds from scene geometry

---

## Remaining / Future

### Bake Quality
- [ ] Barycentric UV interpolation improvement — project voxel center onto triangle plane for exact UV
- [ ] Conservative rasterization — ensure thin geometry doesn't miss voxels
- [ ] Anti-aliased voxelization — partial coverage for voxels on geometry edges

### Tools
- [ ] Progress bar for bake (via `EditorUtility.DisplayProgressBar`)
- [ ] Undo support for bake operations
- [ ] Scene view overlay showing voxel grid stats

### Performance
- [ ] Mesh batching — combine all geometry into one mega-buffer, single dispatch
- [ ] Async GPU readback for diagnostics instead of blocking `ReadPixels`

### Runtime
- [ ] Multiple volume support — blend overlapping Toaster volumes
- [ ] Density field support — separate density channel or derive from voxel occupancy
- [ ] Material property override for per-object voxel color tint

### Future (see FUTURE.md)
- [ ] Sparse brickmap storage
- [ ] Temporal reprojection on froxel scattering
- [ ] Clipmap compositing
- [ ] DXR hardware ray tracing for shadow rays

---

## Debug Visualizers (Shipped)

| Visualizer | Shader | What It Shows |
|---|---|---|
| Raw Slice | `Toaster/DebugSlice` | Single Z-slice, raw color values |
| Heatmap | `Toaster/DebugHeatmap` | False-color density map with channel isolation |
| Isosurface | `Toaster/DebugIsosurface` | Raymarched solid surfaces with gradient normals |
| Multi-Slice | `Toaster/DebugMultiSlice` | Hologram-style Y-slices, optional animation |
| Point Cloud | `Toaster/DebugPointCloud` | Billboard per occupied voxel (procedural draw) |
| Volume Fog | `Toaster/Volume` | Production raymarched volumetric fog |
