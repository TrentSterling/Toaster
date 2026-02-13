# Toaster — Future Techniques Reference

Advanced voxel, volumetric, and GI techniques relevant to Toaster's evolution. Each entry includes what the technique is, why it matters for voxel-based volumetric lighting, and key implementation details.

---

## 1. Brickmaps / Sparse Brick Maps

**What it is:**
A two-level sparse voxel storage structure. A coarse 3D grid (e.g., 64x64x64 or 128x128x128) stores `uint` indices per cell. Empty cells contain a sentinel value (e.g., `0xFFFFFFFF`). Filled cells point into a flat `StructuredBuffer` of bricks, where each brick is a small dense block (typically 8x8x8) of voxel data.

**Why it matters for Toaster:**
Toaster currently uses a dense `RWTexture3D` grid. For large worlds this wastes massive GPU memory on empty air. Brickmaps achieve sparse storage with only two indirection levels, which means ray traversal can be hard-coded (no recursion, minimal branching), making it far more GPU-friendly than octrees. Effective resolution scales to 1024^3+ while only allocating memory for occupied regions.

**Key implementation details:**
- Top-level grid: `RWTexture3D<uint>` or `RWBuffer<uint>` at coarse resolution. Each entry is either `EMPTY` or an index into the brick pool.
- Brick pool: `RWStructuredBuffer<BrickData>` where `BrickData` contains 8x8x8 = 512 voxels of `half4` (color + density). Pool grows via doubling allocation.
- World is optionally divided into "superchunks" (e.g., 16x16x16 bricks) for locality. Index fits in 12 bits (4096 max bricks per superchunk).
- Ray traversal: DDA through the coarse grid; on hit, DDA through the brick. Two nested loops, zero recursion.
- Construction: during voxelization, `InterlockedCompareExchange` on the coarse grid to atomically allocate brick slots from a free-list counter.
- Memory: ~5 bytes/voxel effective for typical scenes (vs. 8 bytes/voxel for dense `RGBAHalf`).

**References:**
- [direct to video — Real-time Ray Tracing Part 2](https://directtovideo.wordpress.com/2013/05/08/real-time-ray-tracing-part-2/) — original brickmap concept for GPU
- [VoxelRT — Sparse 64-trees guide](https://dubiousconst282.github.io/2024/10/03/voxel-ray-tracing/) — two-level brickmap traversal details
- [stijnherfst/BrickMap](https://github.com/stijnherfst/BrickMap) — CUDA brickmap path tracer
- Christensen, P. — "Point-Based Approximate Color Bleeding" (Pixar, 2008) — original brick map concept for offline rendering

---

## 2. Spatial Hashing for Voxels

**What it is:**
A hash-table-based sparse voxel storage scheme. Instead of a hierarchical tree or a two-level grid, voxel blocks are stored in a flat GPU hash table keyed by their integer world coordinates. Provides O(1) lookup and supports dynamic insertion/deletion.

**Why it matters for Toaster:**
Spatial hashing eliminates the need for any hierarchical structure, enabling constant-time access with full GPU parallelism. For dynamic scenes where geometry changes between bakes, the hash table can be partially updated without rebuilding the entire grid. It also scales to arbitrary world sizes without pre-allocating a bounding volume.

**Key implementation details:**
- Hash function: maps `int3` voxel-block coordinates to a fixed-size table. Common choice: `hash(x,y,z) = (x * 73856093 ^ y * 19349669 ^ z * 83492791) % tableSize`.
- Collision handling: open addressing with linear probing, or chained bucket lists in a secondary buffer.
- Storage: each hash entry points to a voxel block (e.g., 8x8x8 chunk) stored in a pool buffer, similar to brickmaps but without spatial coherence guarantees.
- NVIDIA Instant-NGP extension: multi-resolution hash encoding uses multiple hash tables at geometric progression of resolutions (coarse to fine), with trilinear interpolation at each level. Useful if Toaster ever moves to neural radiance field representations.
- Niesser et al. (2013) demonstrated real-time TSDF fusion at scale using this approach on consumer GPUs.
- Drawback vs. brickmaps: loss of spatial locality hurts cache coherence during ray marching. Best suited for random-access queries (probe lookups) rather than sequential traversal.

**References:**
- Niessner, M. et al. — ["Real-time 3D Reconstruction at Scale using Voxel Hashing"](https://niessnerlab.org/papers/2013/4hashing/niessner2013hashing.pdf) (SIGGRAPH Asia 2013)
- Lefebvre, S. & Hoppe, H. — ["Perfect Spatial Hashing"](https://hhoppe.com/perfecthash.pdf) (SIGGRAPH 2006)
- Mueller, T. et al. — "Instant Neural Graphics Primitives with a Multiresolution Hash Encoding" (SIGGRAPH 2022)

---

## 3. Clipmap Cascades

**What it is:**
A multi-resolution cascading volume system where multiple 3D texture "cascades" are centered on the camera, each covering a larger world extent at coarser resolution. The innermost cascade has the finest voxels; outer cascades double the extent and halve the density. Unlike a regular mip chain, each cascade is clipped to a region around the camera and scrolled as the camera moves.

**Why it matters for Toaster:**
Toaster's single fixed-resolution grid forces a tradeoff between coverage and detail. Clipmaps solve this: fine detail near the camera (where fog is most visible), coarse data far away (where it blends into atmosphere). Stress Level Zero's Custom-URP uses two cascades (near 80m / far 160m) for their Bonelab volumetrics.

**Key implementation details:**
- Typical setup: 2-4 cascades, each a `Texture3D` of the same resolution (e.g., 128^3) but covering 2x the world extent of the previous.
- Camera-centered scrolling: when the camera moves more than one voxel width, the volume is "scrolled" by shifting texture coordinates and only re-voxelizing/re-baking the newly exposed slice.
- Compositing: during ray marching, sample the finest cascade that covers the current ray position. Blend between cascades at boundaries to avoid seams.
- Frostbite uses 3 cascades for extinction volumes, composited during the froxel integration pass.
- VXGI (NVIDIA) uses clipmaps instead of octrees for cone tracing, achieving real-time performance on fully dynamic scenes.
- For Toaster: bake each cascade at its respective resolution. Runtime fog shader selects cascade by distance from camera.

**References:**
- Hillaire, S. — ["Physically-based & Unified Volumetric Rendering in Frostbite"](https://www.ea.com/frostbite/news/physically-based-unified-volumetric-rendering-in-frostbite) (SIGGRAPH 2015)
- [Stress Level Zero Custom-URP](https://github.com/StressLevelZero/Custom-URP) — clipmap compositing for Bonelab
- Teixeira, T. — "Real-Time Global Illumination using Voxel Cone Tracing with a 3D Clipmap" (2018)

---

## 4. Froxel Grids

**What it is:**
A frustum-aligned voxel grid ("froxel" = frustum + voxel). The volume is subdivided along the camera frustum's XY (matching screen tiles) and Z (depth slices, typically with exponential/logarithmic spacing). Each froxel represents a small frustum-shaped cell in world space.

**Why it matters for Toaster:**
Toaster's world-space grid wastes resolution on areas not visible to the camera. A froxel grid concentrates resolution where it matters: screen-visible space, with finer depth slices near the camera. This is the industry-standard approach for real-time volumetric fog (used in Frostbite, Unreal Engine, Unity HDRP).

**Key implementation details:**
- Grid dimensions: typically 160x90x64 (matching 16x16 screen tiles at 1080p with 64 depth slices).
- Depth distribution: exponential (`z_slice = near * pow(far/near, slice/numSlices)`) concentrates slices near the camera.
- Two-pass algorithm:
  1. **Material pass**: inject participating media properties (scattering, extinction, emission, phase function) into the froxel grid via compute shader.
  2. **Lighting pass**: for each froxel, accumulate in-scattered light from all affecting lights (point, spot, directional with shadow maps).
  3. **Integration pass**: front-to-back ray march through froxels, accumulating transmittance and in-scattered light. Output to a "scattering + transmittance" 3D lookup texture.
- Final apply: single `tex3D` lookup during the main shading pass using screen UV + linear depth.
- Fixed cost regardless of scene complexity (~1ms on modern GPUs).
- Toaster hybrid approach: bake the `LightingGrid` in world space (offline), then inject it into a froxel grid at runtime for proper depth-aware fog compositing.

**References:**
- Hillaire, S. — ["Physically-based & Unified Volumetric Rendering in Frostbite"](https://www.ea.com/frostbite/news/physically-based-unified-volumetric-rendering-in-frostbite) (SIGGRAPH 2015)
- Wronski, B. — ["Volumetric Fog: Unified Compute Shader Based Solution to Atmospheric Scattering"](https://bartwronski.com/wp-content/uploads/2014/08/bwronski_volumetric_fog_siggraph2014.pdf) (SIGGRAPH 2014)
- [diharaw/volumetric-fog](https://github.com/diharaw/volumetric-fog) — OpenGL froxel fog implementation

---

## 5. Temporal Reprojection for Volumetrics

**What it is:**
A noise-reduction technique that blends the current frame's volumetric lighting result with the reprojected result from previous frames. Each frame, a sub-voxel jitter offset is applied to the ray marching start position. The current and previous results are blended using an exponential moving average, effectively super-sampling over time.

**Why it matters for Toaster:**
Toaster's runtime fog shader ray marches through the `LightingGrid`. With limited step counts (for performance), banding and noise are visible. Temporal reprojection lets the shader use fewer steps per frame while converging to a smooth result over 8-16 frames. This is especially important at quarter-res rendering.

**Key implementation details:**
- Per-frame jitter: offset ray start by `blueNoise * stepSize` with a different sample each frame (cycling through a Bayer matrix or blue noise tile).
- Reprojection: use the previous frame's view-projection matrix to find where the current froxel/voxel was last frame. Sample the history buffer at that location.
- Blending: `result = lerp(history, current, alpha)` where `alpha` is typically 0.05-0.1 (5-10% new data per frame). Depth-dependent alpha: higher near the camera (faster response), lower far away (more stable).
- Rejection: if reprojected coordinates fall outside the volume, or if depth/motion discontinuities are detected, increase `alpha` toward 1.0 (trust current frame).
- Ghosting tradeoff: low alpha = smooth but ghosty behind moving lights/objects. High alpha = responsive but noisy. Dynamic lights (muzzle flash, flashlight) need faster convergence.
- Storage: one additional `Texture3D` for the history buffer, ping-ponged each frame.

**References:**
- Hillaire, S. — "Physically-based & Unified Volumetric Rendering in Frostbite" (SIGGRAPH 2015)
- Wronski, B. — "Volumetric Fog" (SIGGRAPH 2014) — temporal integration details
- [Unity HDRP Volumetric Lighting docs](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Volumetric-Lighting.html)

---

## 6. Signed Distance Fields (SDF)

**What it is:**
A scalar field where each point stores the signed distance to the nearest surface (positive outside, negative inside). Enables "sphere tracing" — ray marching with adaptive step sizes equal to the distance value, guaranteeing no surface is skipped.

**Why it matters for Toaster:**
SDFs accelerate ray marching dramatically: instead of fixed small steps through the voxel grid, sphere tracing skips large empty regions in O(log n) steps. A global SDF also provides cheap ambient occlusion (sample a few distances along the normal hemisphere) and soft shadows (track closest approach during the march). For Toaster's path tracer, an SDF could replace or accelerate the current voxel-grid ray intersection.

**Key implementation details:**
- Generation: for each voxel in a 3D grid, compute the signed distance to the nearest mesh surface. Can be done via jump flooding algorithm (JFA) on GPU, or by rasterizing mesh triangles into a distance field.
- Storage: single-channel `RWTexture3D<float>` or `RWTexture3D<half>`. Typical resolution: 128^3 to 256^3 for a global SDF.
- Sphere tracing: `while(dist > epsilon) { pos += dir * dist; dist = sampleSDF(pos); }`. Converges in 20-40 steps for most scenes.
- Ambient occlusion: sample SDF at increasing distances along the normal (e.g., 0.1, 0.2, 0.4, 0.8 units). If the distance value is less than the sample distance, there is occlusion. Cheap (~4 samples), no rays needed.
- Soft shadows: during a ray march toward a light, track `min(k * sdf / t, 1.0)` where `t` is distance along ray and `k` controls penumbra softness.
- For Toaster: generate an SDF during the voxelization pass (write distance instead of or alongside color). Use it in the path tracer for accelerated ray-voxel intersection, and in the fog shader for AO.

**References:**
- Hart, J.C. — "Sphere Tracing: A Geometric Method for the Antialiased Ray Tracing of Implicit Surfaces" (1996)
- Wright, D. — ["Dynamic Occlusion with Signed Distance Fields"](https://advances.realtimerendering.com/s2015/DynamicOcclusionWithSignedDistanceFields.pdf) (SIGGRAPH 2015, Epic Games)
- [Unreal Engine Mesh Distance Fields](https://dev.epicgames.com/documentation/en-us/unreal-engine/mesh-distance-fields-in-unreal-engine)

---

## 7. Spherical Harmonics: L1 vs. L0 Tradeoffs

**What it is:**
Spherical Harmonics (SH) encode a spherical function (like irradiance) as a weighted sum of basis functions. L0 (band 0) is a single coefficient representing the average value over the sphere — isotropic, no directional information. L1 (band 1) adds 3 coefficients encoding a linear directional gradient. L2 (band 2) adds 5 more for quadratic detail.

**Why it matters for Toaster:**
Toaster currently uses L0 (1 `RGBAHalf` texture), trading directional fidelity for 75% memory savings over L1. This section documents when upgrading to L1 is worth the cost, and potential middle-ground alternatives.

**Key tradeoffs:**

| Property          | L0 (Toaster current) | L1 (Bakery default)     | L2                          |
|--------------------|-----------------------|--------------------------|------------------------------|
| Coefficients/color | 1                     | 4                        | 9                            |
| Textures needed    | 1 x `RGBAHalf`       | 4 x `RGBAHalf` (or 3 packed) | 9 x channels               |
| Decode cost        | Free (`tex3D`)        | 4 `tex3D` + dot product  | 9 `tex3D` + matrix multiply  |
| Directional info   | None (flat ambient)   | Linear gradient (dominant direction) | Quadratic (soft lobes)      |
| Best for           | Fog, ambient glow     | Directional fog, surface GI | High-fidelity surface GI     |

**Implementation details for L1 upgrade path:**
- Store 4 `Texture3D<RGBAHalf>`: `SH_L0` (DC term), `SH_L1x`, `SH_L1y`, `SH_L1z` (directional coefficients).
- During path tracing, accumulate: `L0 += lightColor`, `L1 += lightColor * direction` (where direction is the normalized vector from voxel to light hit).
- At runtime decode: `irradiance = L0 + L1x * normal.x + L1y * normal.y + L1z * normal.z`.
- Alternative: Zonal Harmonics (ZH3) — Activision's approach uses 3 zonal harmonic coefficients instead of 4 SH L1 coefficients, saving 25% with negligible quality loss for diffuse lighting.
- Toaster recommendation: stay L0 for fog/volumetrics (direction is meaningless inside a volume). Consider L1 only if Toaster expands to surface GI (irradiance probes on mesh surfaces).

**References:**
- Ramamoorthi, R. & Hanrahan, P. — ["An Efficient Representation for Irradiance Environment Maps"](https://cseweb.ucsd.edu/~ravir/papers/envmap/envmap.pdf) (SIGGRAPH 2001)
- Sloan, P.-P. — ["Stupid Spherical Harmonics Tricks"](https://www.ppsloan.org/publications/StupidSH36.pdf) (GDC 2008)
- Roughton, T. & Silvennoinen, A. — ["ZH3: Quadratic Zonal Harmonics"](https://www.activision.com/cdn/research/ZH3-I3D-Presentation-Slides.pdf) (I3D 2024, Activision)
- Hazel, G. — ["Spherical Harmonics for Lighting"](https://community.arm.com/cfs-file/__key/telligent-evolution-components-attachments/01-2066-00-00-00-01-27-70/Simplifying_2D00_Spherical_2D00_Harmonics_2D00_for_2D00_Lighting.pdf) (ARM/Geomerics)

---

## 8. Hardware Ray Tracing (DXR/VKR) for Voxel Light Baking

**What it is:**
Using GPU hardware ray tracing acceleration (DXR on DirectX 12, VK_KHR_ray_tracing on Vulkan) to replace or accelerate the software ray marching in Toaster's path tracer bake step. Hardware BVH traversal and ray-triangle intersection run on dedicated RT cores (NVIDIA RTX, AMD RDNA2+).

**Why it matters for Toaster:**
Toaster's compute-shader path tracer currently marches rays through the voxel grid, which is O(n) per ray in grid resolution. DXR provides hardware-accelerated ray-scene intersection against the original mesh BVH, which is O(log n) and runs on dedicated silicon. This could dramatically speed up the bake step, especially for multi-bounce GI.

**Key implementation details:**
- **Ray generation shader**: dispatch one thread per voxel in the `LightingGrid`. For each voxel, generate N rays in random directions (hemisphere or sphere sampling).
- **Acceleration structure**: build a BLAS (Bottom-Level Acceleration Structure) per mesh, compose into a TLAS (Top-Level Acceleration Structure) for the scene. Unity HDRP handles this automatically; for URP/custom, use `RayTracingAccelerationStructure` API.
- **Closest-hit shader**: on ray-mesh intersection, read the hit mesh's albedo (from the pre-baked Meta Pass texture using hit UV), contribute to the voxel's accumulated light.
- **Miss shader**: sample sky/environment, contribute ambient term.
- **DXR 1.1 inline ray tracing**: `RayQuery` objects usable directly in compute shaders — no separate ray generation/hit shader pipeline needed. Simpler integration with existing compute-based Toaster pipeline.
- **Hybrid approach**: use DXR for shadow rays and first-bounce visibility, fall back to voxel grid marching for secondary bounces (where exact geometry is less important).
- Unity requirement: DX12 backend, `SystemInfo.supportsRayTracing` check. Fallback to current compute path on unsupported hardware.
- SLZ Custom-URP includes a DXR baking path with Moller-Trumbore software fallback.

**References:**
- Benyoub, A. — ["DXR Ray Tracing"](https://advances.realtimerendering.com/s2019/Benyoub-DXR%20Ray%20tracing-%20SIGGRAPH2019-final.pdf) (SIGGRAPH 2019)
- [NVIDIA RTXGI / DDGI](https://github.com/NVIDIAGameWorks/RTXGI-DDGI) — probe-based GI using DXR
- [Unity HDRP Ray Tracing](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/Ray-Tracing-Getting-Started.html)

---

## 9. Blue Noise Sampling for Volumetric Ray Marching

**What it is:**
Replacing white noise or regular-interval sampling in ray marching with blue noise (noise with suppressed low-frequency content), so that sampling errors manifest as high-frequency grain rather than structured banding or clumpy artifacts. The human eye is far more tolerant of blue noise error, making results look cleaner at the same sample count.

**Why it matters for Toaster:**
Toaster's fog shader ray marches the `LightingGrid` with a fixed step count. Low step counts produce visible banding (regular intervals) or clumpy noise (white noise jitter). Blue noise jittering produces perceptually superior results with the same number of steps, and the high-frequency error is trivially removed by temporal reprojection or a light blur.

**Key implementation details:**
- **Blue noise texture**: a tiled 2D texture (e.g., 128x128) containing pre-computed blue noise values in [0,1]. Use `_BlueNoise.Load(screenPos % 128)` in the shader.
- **Per-frame animation**: cycle through multiple blue noise textures or apply a per-frame offset (e.g., golden ratio increment: `offset = frac(blueNoise + frameIndex * 0.618034)`). This decorrelates temporal samples.
- **Application**: offset the ray march start position by `blueNoiseSample * stepSize` along the ray direction. This converts banding into grain.
- **Comparison**: white noise breaks banding but produces clumpy low-frequency patterns. Interleaved gradient noise (IGN) is good but blue noise is measurably better for volumetrics.
- **Temporal blue noise**: when combined with temporal reprojection, blue noise converges faster than other noise types because successive frames' errors don't correlate.
- **Source**: free blue noise textures available from Christoph Peters (momentsingraphics.de), pre-generated with void-and-cluster algorithm.

**References:**
- Georgiev, I. & Fajardo, M. — ["Blue-noise Dithered Sampling"](https://dl.acm.org/doi/10.1145/2897839.2927430) (SIGGRAPH 2016)
- Wolfe, A. — ["Using Blue Noise for Ray Traced Soft Shadows"](https://link.springer.com/content/pdf/10.1007/978-1-4842-7185-8_24.pdf) (Ray Tracing Gems II, Ch. 24)
- [blog.demofox.org — Ray Marching Fog With Blue Noise](https://blog.demofox.org/2020/05/10/ray-marching-fog-with-blue-noise/)
- [momentsingraphics.de — Free Blue Noise Textures](https://momentsingraphics.de/BlueNoise.html)

---

## 10. Radiance Cascades (Alexander Sannikov)

**What it is:**
A multi-scale radiance field technique where multiple "cascades" encode the light field at different angular and spatial resolutions. Each cascade traces rays over a specific distance interval. Cascade 0 traces short rays at high spatial resolution (many probes, few directions). Each subsequent cascade traces longer rays at lower spatial resolution but higher angular resolution (fewer probes, more directions). Cascades are merged bottom-up so that each probe inherits far-field radiance from the cascade above it.

**Why it matters for Toaster:**
Radiance cascades offer geometry-agnostic, noiseless GI at constant cost regardless of scene complexity or light count. Unlike path tracing (which Toaster uses for baking), radiance cascades produce temporally stable results with zero noise and zero temporal latency (no accumulation needed). A 3D extension could replace Toaster's path tracer entirely for real-time or near-real-time GI updates.

**Key implementation details:**
- **Cascade structure**: N cascades (typically 4-8). Cascade `i` has `resolution / 2^i` spatial probes and `4^i` ray directions (in 2D; in 3D, angular scaling follows solid angle coverage).
- **Interval tracing**: each cascade traces rays only within its assigned distance interval `[near_i, far_i]`. No ray traces the full scene distance — work is distributed across cascades.
- **Penumbra merging**: 4 intervals from cascade `i+1` merge into 1 interval of cascade `i`, propagating far-field angular information downward. This correctly reproduces penumbra widening with distance.
- **3D extension**: world-space 3D grid of probes at each cascade level. Cascade 0 might be 256^3 probes x 6 directions; cascade 3 might be 32^3 probes x 384 directions. Total memory remains bounded because spatial and angular resolution trade off.
- **Constant cost**: computation time is independent of scene complexity, number of lights, or number of polygons. Only depends on cascade resolution settings.
- **No temporal accumulation**: each frame is self-contained. Moving lights and geometry produce instant, correct response with no ghosting.
- **Deployed in**: Path of Exile 2 (Grinding Gear Games) — full 3D, real-time GI.
- **Toaster relevance**: could serve as a real-time alternative to the offline path tracer bake, or as a runtime update mechanism for dynamic lights.

**References:**
- Sannikov, A. — "Radiance Cascades" (2023) — [radiance-cascades.com](https://radiance-cascades.com)
- [GM Shaders Guest: Radiance Cascades](https://mini.gmshaders.com/p/radiance-cascades) — detailed technical walkthrough
- [MΛX — Fundamentals of Radiance Cascades](https://m4xc.dev/articles/fundamental-rc/) — penumbra merging explained
- [jason.today — Radiance Cascades: Building Real-Time Global Illumination](https://jason.today/rc)
- [tmpvar — Radiance Cascades 3D](https://tmpvar.com/poc/radiance-cascades-3d/) — interactive 3D demo

---

## 11. Light Propagation Volumes (LPV)

**What it is:**
A real-time GI technique that injects virtual point lights (VPLs) from reflective shadow maps (RSMs) into a 3D grid, then iteratively propagates radiance through the grid using spherical harmonics. Each propagation step spreads light to the 6 face-adjacent neighbors, simulating one "hop" of diffuse inter-reflection.

**Why it matters for Toaster:**
LPV is conceptually similar to Toaster's architecture: both use a 3D grid to store and propagate light. LPV's iterative propagation is much cheaper than path tracing (no random rays, deterministic convergence in ~8 iterations), making it viable for real-time updates. However, it only handles diffuse single-bounce and suffers from light leaking through thin walls.

**Key implementation details:**
- **Injection**: render a reflective shadow map (RSM) from each light. Each RSM pixel becomes a VPL. Inject the VPL's flux into the nearest grid cell, encoded as L1 SH (4 coefficients per color channel).
- **Propagation**: iterative compute pass. Each cell distributes its SH radiance to its 6 face neighbors, weighted by the solid angle subtended. Repeat 8-16 iterations for roughly one diffuse bounce.
- **Geometry volume**: a separate occlusion grid blocks propagation through solid surfaces, reducing light leaking.
- **Cascaded LPV**: multiple grid resolutions centered on the camera (like clipmaps) to extend coverage without sacrificing near-field detail.
- **Grid resolution**: typically 32^3 per cascade. Low resolution is acceptable because diffuse GI is inherently low-frequency.
- **SH encoding**: L1 (4 coefficients) minimum for meaningful directional propagation. L0-only propagation would spread light uniformly, losing directionality.
- **Performance**: ~0.5ms for injection + 8 propagation iterations on modern GPUs.
- **Limitations**: no specular, single bounce only (multiple bounces require cascaded iterations), light leaking through thin geometry.
- **For Toaster**: LPV propagation could serve as a cheap "preview mode" during editing, with the full path tracer reserved for final bakes.

**References:**
- Kaplanyan, A. — ["Light Propagation Volumes in CryEngine 3"](https://advances.realtimerendering.com/s2009/Light_Propagation_Volumes.pdf) (SIGGRAPH 2009)
- Kaplanyan, A. & Dachsbacher, C. — ["Cascaded Light Propagation Volumes for Real-time Indirect Illumination"](https://cgg.mff.cuni.cz/~jaroslav/gicourse2010/giai2010-07-anton_kaplanyan-slides.pdf) (I3D 2010)

---

## 12. Sparse Voxel Octrees (SVO)

**What it is:**
A hierarchical tree structure where a cube is recursively subdivided into 8 children. Only occupied children are allocated, providing sparse storage. The root represents the entire scene; leaves represent the finest voxels. Interior nodes store filtered (averaged) versions of their children, functioning as a built-in LOD / mip chain.

**Why it matters for Toaster:**
SVOs provide automatic LOD (sample coarser nodes for far-away or blurred lookups, like cone tracing), logarithmic-depth ray traversal, and memory efficiency for sparse scenes. They are the foundation of SVOGI (CryEngine) and early VXGI. However, they have poor GPU cache behavior due to pointer chasing and recursive traversal.

**Key implementation details:**
- **Node layout**: each node is 1-2 bytes (child mask + pointer). A child mask (8 bits) indicates which of 8 children exist. A pointer (24-32 bits) indexes into a node pool.
- **Construction**: bottom-up GPU construction via atomics. Voxelize at leaf resolution, then build parent levels by merging children.
- **Ray traversal**: stack-based DDA. Push/pop nodes as the ray enters/exits octants. NVIDIA's "Efficient Sparse Voxel Octrees" paper achieves real-time performance with contour-based leaf encoding.
- **LOD / filtering**: interior nodes store the average color/opacity of their children. Sampling at level `L` gives a filtered view equivalent to `2^L` resolution. This is exploited by cone tracing (see section 14).
- **Storage**: ~5 bytes/voxel for typical scenes. Sparse scenes (10% occupancy) see ~20x compression vs. dense grids.
- **GPU drawbacks**: pointer chasing causes cache misses and warp divergence. Brickmaps (section 1) are preferred when only 2 levels suffice.
- **SVDAG variant**: Sparse Voxel Directed Acyclic Graphs merge identical subtrees, achieving further 5-10x compression. Read-only (no dynamic updates).

**References:**
- Laine, S. & Karras, T. — ["Efficient Sparse Voxel Octrees"](https://research.nvidia.com/sites/default/files/pubs/2010-02_Efficient-Sparse-Voxel/laine2010tr1_paper.pdf) (NVIDIA Research, 2010)
- Crassin, C. et al. — "GigaVoxels: Ray-Guided Streaming for Efficient and Detailed Voxel Rendering" (I3D 2009)
- [Sparse Voxel Octree — Wikipedia](https://en.wikipedia.org/wiki/Sparse_voxel_octree)

---

## 13. Screen-Space Volumetric Scattering

**What it is:**
Rendering volumetric light scattering (god rays, fog) in screen space at reduced resolution (half or quarter), then upsampling to full resolution using bilateral filtering that respects depth edges. This is the "cheap and cheerful" approach used when a full 3D froxel grid is too expensive.

**Why it matters for Toaster:**
If Toaster's runtime fog shader becomes a performance bottleneck (especially on mobile/VR), rendering the ray march at quarter resolution with bilateral upsampling can reduce cost by 8-16x while preserving edge quality. This is a pragmatic optimization for the final compositing step.

**Key implementation details:**
- **Downscale**: render the fog ray march into a quarter-res (1/4 width, 1/4 height) render target. Each pixel marches through the `LightingGrid` as normal.
- **Depth-aware jitter**: use checkerboard or interleaved sampling patterns across the quarter-res pixels, with blue noise jitter per pixel.
- **Bilateral upsample**: when compositing back to full resolution, sample the quarter-res fog texture at the 4 nearest texels. Weight each sample by `exp(-|depth_full - depth_quarter| / sigma)`. This preserves hard edges at geometry boundaries while smoothly interpolating fog in open space.
- **Alternative: temporal upsampling**: render at quarter-res with a different sub-pixel offset each frame, accumulate over 4 frames to reconstruct full-res. Lower latency than bilateral but introduces 4-frame convergence delay.
- **Performance**: Wronski reports ~1.1ms total (including shadow map prep) for full volumetric fog at quarter-res on PS4/Xbox One.
- **Artifacts**: bilateral upsampling can produce halos at depth discontinuities if sigma is too large. Checkerboard rendering can show residual patterns with fast camera motion.

**References:**
- Wronski, B. — ["Volumetric Fog: Unified Compute Shader Based Solution"](https://bartwronski.com/wp-content/uploads/2014/08/bwronski_volumetric_fog_siggraph2014.pdf) (SIGGRAPH 2014)
- [NVIDIA GPU Gems 3, Ch. 13 — Volumetric Light Scattering as a Post-Process](https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-13-volumetric-light-scattering-post-process)
- Gotow, A. — ["Screenspace Volumetric Shadowing"](https://andrewgotow.com/2016/10/05/screenspace-volumetric-shadowing/)

---

## 14. Cone Tracing (VXGI Style)

**What it is:**
A GI technique that traces cones (rather than rays) through a mipmapped 3D voxel texture. Narrow cones approximate specular reflections; wide cones approximate diffuse irradiance. The cone's increasing width maps to higher mip levels of the 3D texture, providing automatic filtering and soft light transport without noise.

**Why it matters for Toaster:**
Cone tracing could let Toaster provide real-time indirect lighting (not just baked ambient). If the `LightingGrid` is extended with a mip chain, the fog shader or a surface shader can trace cones to gather soft bounced light. The technique is deterministic (no random rays, no noise, no temporal accumulation needed).

**Key implementation details:**
- **Voxelization**: rasterize the scene into a 3D texture (Toaster already does this). Store outgoing radiance and opacity at the finest level.
- **Mip chain**: generate mipmaps for the 3D texture. Each mip level averages 2x2x2 voxels from the level below, creating a filtered LOD pyramid. Use anisotropic mipmapping for better directional filtering.
- **Cone marching**: for each shading point, trace several cones into the voxel volume:
  - Diffuse: 5-9 cones distributed over the hemisphere (60-90 degree aperture each).
  - Specular: 1 cone in the reflection direction (aperture based on roughness).
- **Mip selection**: at distance `t` along a cone with aperture `theta`, the effective voxel diameter is `2 * t * tan(theta/2)`. Sample the mip level where voxel size matches this diameter: `mipLevel = log2(diameter / voxelSize)`.
- **Accumulation**: front-to-back alpha compositing along the cone, similar to standard ray marching but with mip-dependent opacity.
- **Clipmap variant** (VXGI): use a clipmap instead of a regular mip chain. The clipmap provides higher resolution near the camera and coarser resolution far away, better matching the cone's increasing sample footprint.
- **Cost**: ~2-4ms for diffuse + specular on a 256^3 grid at 1080p. Scales with cone count and march distance.
- **Limitations**: light leaking through thin geometry (voxel resolution dependent), overly soft results at low resolution, anisotropy handling requires 6 directional opacity textures (3 axes x 2 directions).

**References:**
- Crassin, C. et al. — "Interactive Indirect Illumination Using Voxel Cone Tracing" (Pacific Graphics 2011)
- [NVIDIA VXGI GDC 2015](https://developer.download.nvidia.com/assets/events/GDC15/GEFORCE/VXGI_Dynamic_Global_Illumination_GDC15.pdf)
- [Wicked Engine — Voxel-based Global Illumination](https://wickedengine.net/2017/08/voxel-based-global-illumination/)
- [Friduric/voxel-cone-tracing](https://github.com/Friduric/voxel-cone-tracing) — C++/GLSL implementation

---

## Technique Comparison Matrix

| Technique               | Storage       | Build Cost   | Query Cost  | Dynamic | Directional | Noise-Free |
|-------------------------|---------------|-------------|-------------|---------|-------------|------------|
| Dense Grid (Toaster v0) | O(n^3)        | Fast        | O(n) march  | Baked   | L0 only     | Yes        |
| Brickmaps               | Sparse 2-lvl  | Fast        | O(n) 2-lvl  | Partial | Any         | Yes        |
| Spatial Hashing          | Sparse hash   | Fast        | O(1) lookup | Yes     | Any         | Yes        |
| Clipmap Cascades         | Multi-res     | Medium      | O(1) lookup | Scroll  | Any         | Yes        |
| Froxel Grid              | Frustum-aligned| Per-frame   | O(1) lookup | Yes     | Any         | Yes*       |
| SVO                      | Sparse tree   | Slow (GPU)  | O(log n)    | Partial | Any         | Yes        |
| SDF                      | Dense/Sparse  | Medium      | O(log n) sphere trace | Partial | N/A   | Yes        |
| LPV                      | Dense low-res | Per-frame   | O(1) lookup | Yes     | L1 SH       | Yes        |
| Cone Tracing (VXGI)      | Dense + mips  | Per-frame   | O(n) cone   | Yes     | Cone-filtered| Yes       |
| Radiance Cascades         | Multi-cascade | Per-frame   | O(1) merge  | Yes     | Full        | Yes        |
| Path Tracing (Toaster v0)| N/A (bake)    | Slow        | N/A         | Baked   | Full        | No**       |

\* With temporal reprojection.
\** Converges with sufficient samples.

---

## Recommended Evolution Path for Toaster

1. **Near-term (v0.2)**: Blue noise jittering + temporal reprojection in the fog shader. Cheapest quality win.
2. **Short-term (v0.3)**: Brickmap sparse storage to handle larger worlds without blowing GPU memory.
3. **Medium-term (v0.4)**: Clipmap cascades (2 levels) for multi-resolution coverage. Froxel grid for runtime fog compositing.
4. **Medium-term (v0.5)**: DXR hardware ray tracing path for the bake step (with compute fallback).
5. **Long-term (v1.0)**: Radiance cascades for real-time GI updates, replacing the offline path tracer for dynamic content.
6. **Optional**: SDF generation for accelerated AO/soft shadows. Cone tracing if surface GI is needed. L1 SH only for surface lighting (keep L0 for volumetrics).
