# Toaster TODO

## Current Status: v0.1 (Crumb)
Working: Material bake (Meta Pass) + GPU voxelization + volume raymarcher + debug visualizers.
Not yet: Path tracer (Stage 3), runtime fog integration, asset serialization.

---

## Immediate (This Week)

### Bug Fixes
- [ ] Meta Pass UV1 support — current workaround fills meta RT with `_BaseColor` fallback. Need to generate UV1 from UV0 for meshes without lightmap UVs, or write a custom meta-like pass that uses UV0
- [ ] Fog volume + debug viz objects get picked up by `FindObjectsByType<MeshRenderer>` during bake — add a `ToasterIgnore` tag or layer mask filter to VoxelBaker
- [ ] Race conditions on voxel writes — multiple triangles hitting the same voxel use last-write-wins. Consider `InterlockedMax` or accumulation buffer for proper blending
- [ ] Voxel grid is not serialized — lost on domain reload. Save as `.asset` via `AssetDatabase.CreateAsset`

### Polish
- [ ] Point cloud visualizer needs a `Mesh` with enough vertices/instances to cover the grid — currently relies on `DrawMeshInstancedIndirect` pattern; add compute buffer for visible voxel compaction
- [ ] Debug slice should auto-wire to baker's voxel grid via `VoxelBaker` reference field
- [ ] Add `[Range]` attributes and tooltips to VoxelBaker inspector fields
- [ ] BrowningLevel presets should auto-set voxelSize (Raw=1.0, Light=0.5, Burnt=0.25)

---

## Short Term (This Month)

### Stage 3: Path Tracer
- [ ] `ToasterTracer.compute` — for each occupied voxel, shoot N rays in random directions
- [ ] If ray hits a light source, accumulate light color weighted by distance falloff
- [ ] If ray hits another occupied voxel, sample that voxel's albedo and bounce
- [ ] Write results to `LightingGrid` (separate `RWTexture3D` from `AlbedoGrid`)
- [ ] Multi-bounce support (2-3 bounces for color bleeding)
- [ ] Blue noise or Halton sequence for ray directions to reduce banding

### Runtime Integration
- [ ] Volume fog shader samples `LightingGrid` instead of raw albedo grid
- [ ] Additive fog blending mode option (for emissive glow effects)
- [ ] Density field support — separate density channel or derive from voxel occupancy
- [ ] Multiple volume support — blend overlapping Toaster volumes

### Bake Quality
- [ ] Proper SAT (Separating Axis Theorem) for triangle-box intersection instead of AABB-only
- [ ] Barycentric UV interpolation improvement — project voxel center onto triangle plane for exact UV (current: uses voxel center directly, which can be off-plane)
- [ ] Conservative rasterization — ensure thin geometry doesn't miss voxels
- [ ] Anti-aliased voxelization — partial coverage for voxels on geometry edges

### Tools
- [ ] Editor window (not just context menu) with preview, settings, bake button
- [ ] Progress bar for bake (via `EditorUtility.DisplayProgressBar`)
- [ ] Undo support for bake operations
- [ ] Scene view overlay showing voxel grid stats (resolution, memory, voxel count)

---

## Medium Term (Next Quarter)

### Performance
- [ ] Buffer pooling — reuse ComputeBuffers across objects instead of recreate per-object
- [ ] Mesh batching — combine all geometry into one mega-buffer, single dispatch
- [ ] Async GPU readback for diagnostics instead of blocking `ReadPixels`
- [ ] LOD support — different voxel sizes for different distance ranges
- [ ] Incremental bake — only re-voxelize objects that moved/changed

### Shader Graph Integration
- [ ] Custom Shader Graph node for sampling Toaster volumes
- [ ] Shader Graph sub-graph for volumetric fog effect
- [ ] Material property override for per-object voxel color tint

### Quality of Life
- [ ] Presets system (Low/Med/High maps to resolution + ray count + bounce count)
- [ ] Auto-bounds — compute scene AABB from all static renderers
- [ ] Multi-scene support — bake volumes per-scene, composite at runtime
- [ ] Gizmo visualization improvements — show occupied voxel count, memory usage

---

## Debug Visualizers (Shipped)

| Visualizer | Shader | What It Shows |
|---|---|---|
| Raw Slice | `Toaster/DebugSlice` | Single Z-slice, raw color values |
| Heatmap | `Toaster/DebugHeatmap` | False-color density map with channel isolation |
| Isosurface | `Toaster/DebugIsosurface` | Raymarched solid surfaces with gradient normals |
| Multi-Slice | `Toaster/DebugMultiSlice` | Hologram-style Y-slices, optional animation |
| Point Cloud | `Toaster/DebugPointCloud` | Billboard per occupied voxel (needs driver script) |
| Volume Fog | `Toaster/Volume` | Production raymarched volumetric fog |
