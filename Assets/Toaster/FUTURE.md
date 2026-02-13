# Toaster FUTURE — Advanced Techniques & Research

This document catalogs techniques, papers, and ideas for evolving Toaster from a simple voxelizer into a production-quality volumetric lighting system. Organized roughly by relevance and implementation complexity.

---

## Tier 1: High-Impact, Achievable Soon

### Radiance Cascades
*Alexander Sannikov, 2023*

Multi-scale radiance propagation that's simpler than path tracing and converges faster than LPV. The key insight: use a hierarchy of probe grids at different angular resolutions — coarse probes capture far-field lighting, fine probes capture near-field. Merge cascades to get both sharp local shadows and smooth distant illumination.

**Why it matters for Toaster:** Could replace the planned path tracer (Stage 3) with something that converges in fewer samples. Instead of shooting random rays per voxel, use structured cascades.

**Key details:**
- Each cascade has probes spaced 2^n apart, with 4^n angular bins
- Coarse cascades propagate far with few directions; fine cascades resolve nearby detail
- Merge step combines cascades — O(N) total work, not O(N log N)
- Naturally parallelizable on GPU (one thread per probe per cascade)
- Paper: "Radiance Cascades: A Novel Approach to Calculating Global Illumination" (2023)

### Voxel Cone Tracing (VXGI Style)
*Crassin et al., NVIDIA, 2011*

Instead of shooting thin rays, trace fat cones through a mip-mapped 3D texture. Wider cones = softer bounces. The 3D texture mip chain acts as a pre-filtered radiance field — mip 0 is sharp detail, higher mips are blurred ambient.

**Why it matters for Toaster:** We already have the voxel grid. Adding mip generation and cone tracing gives us soft indirect light without expensive path tracing.

**Key details:**
- Generate mip chain for the 3D texture (anisotropic filtering preferred)
- Sample higher mips as cone widens with distance: `mipLevel = log2(coneWidth / voxelSize)`
- Diffuse: ~6 wide cones in hemisphere. Specular: 1 narrow cone in reflection direction
- AO falls out naturally (cone occlusion)
- Trade-off: light leaking through thin walls at high mip levels
- Reference: "Interactive Indirect Illumination Using Voxel Cone Tracing" (SIGGRAPH 2011)

### Blue Noise Dithered Ray Marching
Volumetric ray marching suffers from visible stepping artifacts (banding). Blue noise offsets the ray start per-pixel, converting banding into imperceptible noise.

**Why it matters for Toaster:** The volume fog shader currently steps at fixed intervals — blue noise + temporal accumulation would dramatically improve visual quality.

**Key details:**
- Sample a tiling blue noise texture (128x128, `_BlueNoiseTex`)
- Offset ray start: `t += blueNoise(screenUV) * stepSize`
- With TAA or temporal reprojection, the noise averages out over frames → smooth result
- Combine with interleaved sampling (2x2 pixel grid, different offsets) for 4x effective samples
- Reference: "Blue-noise Dithered Sampling" (Georgiev & Fajardo, 2016)

---

## Tier 2: Medium Complexity, High Reward

### Sparse Brick Maps
Two-level indirection for efficient sparse voxel storage. An "indirection texture" maps logical grid chunks (bricks) to physical atlas locations. Only allocate memory for occupied regions.

**Why it matters for Toaster:** Current flat `RWTexture3D` wastes VRAM on empty space. A 128^3 grid is 128MB at `RGBAHalf`. Brickmaps could reduce this to only occupied regions (~5-20% typical).

**Key details:**
- Level 1: Coarse indirection texture (e.g., 16^3) — each cell either points to a brick or is marked empty
- Level 2: Brick atlas — physical 3D texture containing actual voxel data in 8^3 or 16^3 bricks
- Allocation: Compute shader scans occupancy, compacts non-empty bricks, assigns atlas slots
- Lookup: `atlasUVW = indirection[coarseCoord].xyz + fractional offset within brick`
- Similar to virtual texturing but in 3D
- User has a reference brickmap demo project (location TBD)

### Clipmap Cascades (SLZ-style)
*Stress Level Zero, Custom-URP*

Camera-centered cascading volumes at different resolutions. Near cascade = high detail (e.g., 80m range, fine voxels). Far cascade = low detail (160m range, coarse voxels). Rebuild when camera moves more than 1 voxel.

**Why it matters for Toaster:** Solves the "whole scene in one grid" problem. Most voxel detail is wasted on distant areas the player can barely see.

**Key details:**
- 2+ cascades, each a 3D texture centered on camera
- Near cascade: 128^3 covering 80m^3 → 0.625m voxels
- Far cascade: 128^3 covering 160m^3 → 1.25m voxels
- Scroll/rebuild when camera moves >1 voxel (toroidal addressing for efficiency)
- Compositing: blend cascades in the fog shader based on distance
- Reference: https://github.com/StressLevelZero/Custom-URP

### Froxel Grid (Frostbite-style)
*Hillaire, EA/Frostbite, SIGGRAPH 2015*

Frustum-aligned voxel grid for participating media scattering. Unlike world-space voxels, froxels align to the camera frustum with logarithmic depth slicing — high resolution near the camera, low resolution far away.

**Why it matters for Toaster:** Better suited for runtime fog rendering than world-space grids. Could sample Toaster's baked lighting data into a froxel grid for final compositing.

**Key details:**
- Grid typically 160x90x64 (width x height x depth slices)
- Depth slicing: `z_slice = log(depth / nearPlane) / log(farPlane / nearPlane) * numSlices`
- Per-froxel: accumulate in-scattered light + extinction from all light sources
- Integration pass: front-to-back accumulate scattered light and transmittance
- Final composite: sample froxel grid during deferred/forward shading
- Reference: "Physically Based and Unified Volumetric Rendering in Frostbite" (SIGGRAPH 2015 course)

### Temporal Reprojection for Volumetrics
Reproject the previous frame's volumetric result into the current frame to amortize expensive ray marching over multiple frames.

**Key details:**
- Store previous frame's fog result + depth
- Reproject using inverse view-projection matrix
- Blend: `result = lerp(currentFrame, reprojectedPrev, 0.9)` (exponential moving average)
- Depth-dependent weight: near voxels = lower blend (responsive), far = higher blend (stable)
- Reject reprojected samples when disocclusion detected (depth difference > threshold)
- SLZ uses this on their froxel scattering — reduces noise 10x for 1-frame latency cost

---

## Tier 3: Advanced / Research-Level

### Signed Distance Fields (Global SDF)
Encode scene geometry as a distance field — each voxel stores the distance to the nearest surface. Enables ultra-fast ray marching (sphere tracing) and cheap ambient occlusion.

**Why it matters for Toaster:** SDF could replace or augment the voxel grid for ray tracing in Stage 3. Sphere tracing is ~10x faster than fixed-step marching for finding surface intersections.

**Key details:**
- Build SDF from triangle meshes using jump flooding algorithm (JFA) on GPU
- Store as separate `RWTexture3D<float>` alongside the albedo grid
- Ray march: `t += sdf.SampleLevel(pos)` — steps proportional to distance, not fixed
- AO: cast short cone/rays, accumulate occlusion from SDF — 4 samples enough for smooth AO
- Combine: SDF for fast intersection, voxel grid for color data
- Reference: UE5 Lumen uses global SDF for software ray tracing

### Spatial Hashing
Hash-based sparse voxel storage — map 3D coordinates to a flat hash table. Only stores occupied voxels with O(1) lookup.

**Why it matters for Toaster:** Alternative to brickmaps for sparse storage. Better for dynamic scenes where occupancy changes frequently.

**Key details:**
- Hash function: `hash(x,y,z) = (x * 73856093 ^ y * 19349663 ^ z * 83492791) % tableSize`
- Store key-value pairs: key = packed (x,y,z), value = voxel color/density
- GPU-friendly with atomic operations for insertion
- Collision handling: linear probing or cuckoo hashing
- Drawback: cache-unfriendly access patterns, harder to mip-map
- Reference: "Parallel Spatial Hashing" (Teschner et al.), NVIDIA instant-ngp uses similar approach

### Sparse Voxel Octrees (SVO)
Hierarchical tree where each node has 8 children. Only subdivide where geometry exists. Natural LOD — deeper = finer detail.

**Why it matters for Toaster:** Maximum compression of sparse scenes. A room with a few objects might use 1% of the voxels a flat grid would.

**Key details:**
- Build: top-down subdivision, stop when node is empty or at max depth
- Store as linearized array with child pointers (GPU-friendly)
- Traversal: DDA-style ray march through the octree
- Mip-chain: each octree level IS a mip level — free filtered lookups
- Trade-off: complex implementation, pointer chasing is cache-unfriendly on GPU
- Reference: "Efficient Sparse Voxel Octrees" (Laine & Karras, NVIDIA, 2010)

### Light Propagation Volumes (LPV)
*Kaplanyan & Dachsbacher, 2010 (CryEngine 3)*

Inject light into a low-res 3D grid, then iteratively propagate using spherical harmonics. Each propagation step spreads light one cell in each direction.

**Why it matters for Toaster:** Alternative to path tracing for Stage 3. Simple iterative algorithm that's easy to implement on GPU.

**Key details:**
- Grid typically 32^3 (very coarse), using SH L1 (4 coefficients per cell)
- Injection: for each light, shoot a few hundred VPLs (virtual point lights), inject into grid cells
- Propagation: 4-6 iterations, each cell accumulates flux from its 6 face neighbors
- Occlusion: separate "geometry volume" blocks light propagation
- Result: low-frequency indirect light, no sharp shadows
- Trade-off: light leaks through thin walls, very low resolution
- Reference: "Cascaded Light Propagation Volumes for Real-Time Indirect Illumination" (2010)

### Spherical Harmonics L1 Upgrade
Toaster currently uses L0 (ambient/isotropic). L1 SH adds directional response — the fog can react to where light is coming from.

**Key details:**
- L0: 1 coefficient per channel = 3 floats (RGB) = current Toaster format
- L1: 4 coefficients per channel = 12 floats (RGB × 4) = 3 textures or 1 texture with 4x depth
- Encode: `SH = {L0, L1x, L1y, L1z}` per channel
- Decode: `color = L0 + L1x*dir.x + L1y*dir.y + L1z*dir.z`
- Trade-off: 4x memory, more complex fog shader, but directional fog response
- Bakery uses L1 SH (3 additional textures). Toaster could optionally support it as "Browning Level: Charred"

### Hardware Ray Tracing (DXR) for Bake
Use DXR `TraceRay` for Stage 3 instead of compute shader ray marching through the voxel grid.

**Key details:**
- Build BLAS/TLAS from scene meshes
- For each voxel, trace rays using hardware BVH — orders of magnitude faster than software
- Shadow rays to light sources for direct illumination
- Bounce rays for indirect (sample hit surface albedo, re-trace)
- Fallback: software Moller-Trumbore intersection (SLZ does this in Custom-URP)
- Requires DX12 / Vulkan, RTX or RDNA2+ GPU
- Unity API: `RayTracingAccelerationStructure`, `RayTracingShader`

### Screen-Space Volumetric Upsampling
Render volumetrics at quarter resolution, then bilateral upsample to full res. Massive performance win.

**Key details:**
- Render fog at 1/4 res (half width, half height)
- Bilateral upsample: weight samples by depth similarity to preserve edges
- Checkerboard rendering: alternate which quarter-pixels are computed each frame
- Combine with temporal reprojection for 16x effective samples over 4 frames
- Reference: "Bilateral Upsampling for Volumetric Effects" (Guerrilla Games, 2017)

---

## Tier 4: Moonshot / Far Future

### Neural Radiance Fields (NeRF-style) for Baked Lighting
Train a tiny MLP to represent the lighting field instead of storing a 3D texture. Could compress lighting data 100x.

### Surfel-Based GI (UE5 Lumen-style)
Replace voxels with surfels (surface elements) placed on geometry. More memory-efficient for surface-only lighting.

### Probe-Based Irradiance (DDGI)
Dynamic Diffuse Global Illumination — grid of spherical probes that store irradiance as octahedral maps. Real-time update via ray tracing.

### Photon Mapping
Bidirectional approach — trace photons from lights, deposit in voxel grid, then gather during camera ray march. Handles caustics naturally.

---

## Reference Repos & Papers

| Resource | URL / Citation |
|---|---|
| SLZ Custom-URP | https://github.com/StressLevelZero/Custom-URP |
| Frostbite Volumetrics | "Physically Based and Unified Volumetric Rendering" (Hillaire, SIGGRAPH 2015) |
| VXGI (Crassin) | "Interactive Indirect Illumination Using Voxel Cone Tracing" (SIGGRAPH 2011) |
| Radiance Cascades | Alexander Sannikov (2023) |
| SVO (NVIDIA) | "Efficient Sparse Voxel Octrees" (Laine & Karras, 2010) |
| LPV (CryEngine) | "Cascaded Light Propagation Volumes" (Kaplanyan, 2010) |
| Blue Noise | "Blue-noise Dithered Sampling" (Georgiev & Fajardo, 2016) |
| DDGI | "Dynamic Diffuse Global Illumination" (Majercik et al., 2019) |
| Spatial Hashing | "Optimized Spatial Hashing" (Teschner et al., 2003) |
| instant-ngp | "Instant Neural Graphics Primitives" (Muller et al., NVIDIA, 2022) |
