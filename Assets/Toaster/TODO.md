# Toaster TODO

## Current Status: v1.1 (Crumb)
Working: Full pipeline — Material bake (Meta Pass + emission) + GPU voxelization + path tracer + volume raymarcher + 6 debug visualizers + editor window + Shader Graph integration + froxel volumetric fog with direct scene lighting + multi-volume blending + froxel debug views + remapped density model + light-localized fog halos + height fog.

---

## Completed (v0.1 → v0.8)

### Bug Fixes
- [x] Meta Pass UV1 support — injects UV1 = UV0 for readable meshes without lightmap UVs, _BaseColor fill fallback
- [x] Object filtering — skips Toaster/* shader materials and objects without ContributeGI static flag
- [x] Race conditions on voxel writes — atomic accumulation buffer (InterlockedAdd on fixed-point RGBA), FinalizeGrid kernel averages
- [x] Voxel grid serialized as Texture3D asset, survives domain reloads
- [x] SetInts GridResolution CBUFFER packing — replaced with per-component SetInt + #define alias to avoid 16-byte padding ambiguity
- [x] mesh.isReadable guard — skip non-readable meshes with warning instead of crashing
- [x] sharedMaterials empty array guard — skip renderers with no materials
- [x] Null material slot guard — skip null entries in sharedMaterials array
- [x] Incremental bake fix — no longer clears the grid when reusing existing data, preserves unchanged objects' voxels
- [x] HLSL `sample` variable renamed to `texSample` — avoids shadowing reserved keyword

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
- [x] Froxel volumetric fog pipeline — screen-space frustum-aligned 3D grid with Beer-Lambert integration
- [x] Multi-volume froxel injection — up to 8 volumes composited via StructuredBuffer + named texture slots
- [x] Temporal reprojection on froxel scattering — EMA blend with history buffer, previous ViewProj reprojection
- [x] Static volume registry — ToasterVolume.ActiveVolumes for froxel pipeline auto-discovery
- [x] Demo scene upgrade — corridor with pillars, arches, neon strips, dramatic colored lighting
- [x] Direct scene light evaluation in froxel compute — point, spot, directional with distance/cone attenuation
- [x] Henyey-Greenstein phase function — anisotropic scattering for god rays (g parameter -1..1)
- [x] HDR Meta Pass — ARGBHalf render target captures emission + albedo in one pass
- [x] Emissive material bleeding — unity_MetaFragmentControl (1,1,0,0) captures both albedo and emission

---

## Completed (v1.0 → v1.1)

### Critical Fixes
- [x] Fog density model remap — fogDensity slider (0-1) now means "optical depth at max distance" instead of raw extinction. Converts via `extinction = fogDensity * 3.0 / maxDistance`. Fixes 100% opaque fog at default settings.
- [x] Light-localized density boost — fog thickens near point/spot lights for glow halos. Uses URP distance attenuation + spot cone mask, weighted by intensity.
- [x] Height fog — Y-based density falloff with sqrt curve (thicker at ground, thinner above).
- [x] Edge falloff range widened — 0-1 (was 0-0.5), default 0.2 (was 0.1).
- [x] Diagnostic logging — re-logs when volume count changes instead of once.
- [x] Editor window grouped controls — Density / Lighting / Height / Temporal / Debug sections.

---

## v1.0 Sprint — Polish + Quality

### Critical (must work before calling it done)
- [x] Temporal keyword toggle — enable/disable TEMPORAL_REPROJECTION multi_compile at runtime

### Froxel Quality
- [x] Height fog falloff — remap world Y between base/max height with sqrt density curve
- [x] Froxel debug visualizer — 5 modes (off/scattering/extinction/transmittance/depth slices) in apply shader
- [ ] Exposure/tonemapping on fog — HDR fog values can blow out, need soft clamp
- [ ] Shadow rays for scene lights in fog — occlude light contribution behind solid geometry
- [ ] Light cookie support — project light cookies into froxel injection

### Bake Quality
- [ ] Barycentric UV interpolation improvement — project voxel center onto triangle plane for exact UV
- [ ] Conservative rasterization — ensure thin geometry doesn't miss voxels
- [ ] Anti-aliased voxelization — partial coverage for voxels on geometry edges

### Editor / Tools
- [x] Froxel settings in editor window — inline quick settings for density, intensity, ambient, scatter, temporal
- [ ] One-click froxel setup — button in editor window to add feature to active renderer
- [ ] Progress bar for bake (via `EditorUtility.DisplayProgressBar`)
- [ ] Undo support for bake operations
- [ ] Scene view overlay showing voxel grid stats + froxel stats
- [ ] Froxel gizmo — draw froxel grid frustum wireframe in scene view

### Performance
- [ ] Mesh batching — combine all geometry into one mega-buffer, single dispatch
- [ ] Async GPU readback for diagnostics instead of blocking `ReadPixels`
- [ ] Froxel resolution auto-scaling — reduce resolution when GPU budget exceeded
- [ ] Early-out in InjectMedia — skip froxels with no active volumes nearby (spatial hash)

### Runtime Polish
- [ ] Density field support — separate density channel or derive from voxel occupancy
- [ ] Material property override for per-object voxel color tint
- [ ] Wind-driven noise — animated 3D noise offset for fog movement
- [ ] Particle fog injection — let particle systems contribute density to froxels

### Demo Scene
- [x] Add second overlapping volume — courtyard area beyond doorway, 3m overlap zone with edge falloff
- [ ] Add moving light — test temporal reprojection stability
- [ ] Camera fly-through path — scripted camera for consistent testing
- [ ] Before/after toggle — switch between raymarcher and froxel fog for comparison

### Future (see FUTURE.md)
- [ ] Sparse brickmap storage
- [ ] Clipmap compositing (near/far cascades)
- [ ] DXR hardware ray tracing for shadow rays
- [ ] Mip Sky Fog (Uncharted 4 technique) — sample sky cubemap at distance for atmospheric perspective
- [ ] VR/Stereo — double-width integration buffer with IPD parallax

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
| Froxel Fog | `Toaster/FroxelApply` | Screen-space Beer-Lambert composited fog (v0.8+) |
