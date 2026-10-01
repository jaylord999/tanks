# Procedural Wasteland — Prototype 1

A deterministic, chunk-streamed, procedurally generated 3D apocalypse desert for Unity 6 / URP.
This is the **terrain foundation only**: no tank, combat, multiplayer, inventory, enemies or UI yet.

---

## 1. What was inspected in the project first

| Item | Finding |
|---|---|
| Unity version | **6000.6.0f1** (Unity 6), revision `f7f8ed4d1e24` |
| Render pipeline | **URP 17.6.0** (`com.unity.render-pipelines.universal`) |
| Quality levels | `Mobile` → `Mobile_RPAsset`, `PC` → `PC_RPAsset` |
| Mobile RP asset | `m_RequireDepthTexture: 0`, `m_RequireOpaqueTexture: 0` |
| Scene | `Assets/Scenes/SampleScene.unity` (Main Camera, Directional Light, Global Volume) |
| Input | `com.unity.inputsystem` 1.20.0 |
| Existing sand assets | `Assets/YughuesFreeSandMaterials/` |

### Existing sand assets found (all reused, nothing replaced)

**12 sand materials** in `Assets/YughuesFreeSandMaterials/Materials/`:

`M_YFSM_Sand01.mat` … `M_YFSM_Sand12.mat`

Each uses the URP-compatible built-in shader with `_NORMALMAP` and `_SPECGLOSSMAP` keywords and
references its own albedo (`_MainTex`), normal (`_BumpMap`) and specular/gloss (`_SpecGlossMap`)
texture set. **All 12 have a diffuse texture**, so all 12 are usable.

**Textures** in `Assets/YughuesFreeSandMaterials/Textures/` — 31 files, `T_YFSM_XX_d.tga`
(diffuse, sRGB), `T_YFSM_XX_n.tga` (normal), `T_YFSM_XX_s.tga` (specular).
Imported at max size 1024 (default platform) / 2048 (Standalone), trilinear filtering, 16× aniso,
repeating wrap — ideal for tiling terrain UVs.

**Prefabs / terrain assets:** there is no terrain asset, TerrainData or sand prefab. The pack ships a
preview scene (`Preview/Sc_Preview.unity`) and `Preview/P_Sphere_01.prefab` for material preview
only. There is **no existing terrain**, so the chunk system was built from scratch on top of the
existing materials.

---

## 2. Approach chosen (and why)

`Mobile_RPAsset` has **depth texture and opaque texture disabled**. That rules out a splatmap /
height-blend terrain shader, which would either need those features enabled or put a custom shader
on the critical path for mobile GPUs.

Instead, terrain sand variety uses **sub meshes**:

* One `Mesh` per chunk, one `MeshFilter`, one `MeshRenderer`, one `MeshCollider`.
* Triangles covered by each sand type are written into that sand type's own sub mesh, and the
  renderer receives the matching material list.
* Result: **N draw calls per chunk** (N = sand types actually present, typically 2–5, average ~4),
  **zero shader work**, zero extra texture memory, no runtime blending cost.
* All 12 sand types are supported automatically — adding a material to the library is the only step.

The alternative (one GameObject per material per chunk) was rejected because it multiplies object
counts and hurts mobile batching.

---

## 3. Architecture

```
ProceduralWorld                 (bootstrap + wiring: the only thing you place in a scene)
│
├── ChunkManager                (streaming: which chunks exist, generate, pool, release)
│   ├── ChunkCoordinate         (integer chunk id; the determinism key)
│   ├── TerrainChunk            (one chunk = 1 GameObject + mesh + renderer + collider)
│   ├── TerrainGenerator        (deterministic heights; no rendering dependency)
│   ├── TerrainMeshBuilder      (vertices / UVs / normals / sub meshes, reused scratch buffers)
│   └── SandMaterialSelector    (noise-driven sand regions + organic borders)
│       └── SandLibrary         (the 12 sand types; an asset you can edit in the inspector)
│
├── WastelandFogController      (dust fog + sun + ambient; hides the streaming boundary)
├── TerrainFlyCamera            (TEMPORARY dev camera — delete when the tank exists)
└── TerrainDebugOverlay         (TEMPORARY dev readout — delete when the tank exists)
```

| File | Role |
|---|---|
| `Assets/Scripts/World/WorldSettings.cs` | central tuning (ScriptableObject) |
| `Assets/Scripts/World/ProceduralWorld.cs` | bootstrap |
| `Assets/Scripts/World/ChunkManager.cs` | streaming |
| `Assets/Scripts/World/Chunks/ChunkCoordinate.cs` | chunk id struct |
| `Assets/Scripts/World/Chunks/TerrainChunk.cs` | per-chunk component |
| `Assets/Scripts/World/Chunks/TerrainMeshBuilder.cs` | mesh generation |
| `Assets/Scripts/World/Generation/NoiseUtility.cs` | Perlin / fBm / rotated multi-octave noise |
| `Assets/Scripts/World/Generation/TerrainGenerator.cs` | height field |
| `Assets/Scripts/World/Sand/SandLibrary.cs` | sand type registry |
| `Assets/Scripts/World/Sand/SandMaterialSelector.cs` | sand region field |
| `Assets/Scripts/World/Environment/WastelandFogController.cs` | atmosphere |
| `Assets/Scripts/World/Environment/TerrainFlyCamera.cs` | dev camera |
| `Assets/Scripts/Debug/TerrainDebugOverlay.cs` | dev readout |
| `Assets/Scripts/Editor/WastelandPrototypeSetup.cs` | one-click asset + scene setup |
| `Assets/Scripts/Editor/WastelandPrototypeVerifier.cs` | pure terrain checks |
| `Assets/Scripts/Editor/WastelandStreamingVerifier.cs` | chunk streaming checks |
| `Assets/Scripts/Editor/WastelandPrototypeRunner.cs` | batchmode entry points |

---

## 4. Setup

Open the project, then run **`Tanks → Setup Procedural Wasteland Scene`**.

That single menu item:

1. creates `Assets/WastelandWorld/WorldSettings.asset`
2. creates `Assets/WastelandWorld/SandLibrary.asset` and registers **all 12 sand materials**
3. adds a `ProceduralWorld` object to the open scene
4. adds a `WastelandAtmosphere` object (fog)

Then press **Play**.

Optional: **`Tanks → Create Sand Resources Bridge`** copies the 12 materials into `Assets/Resources/…`
so the runtime sand-library fallback also works without the asset files.

**Controls (temporary dev camera):** `W A S D` move · `Q`/`Ctrl` down · `E`/`Space` up ·
`Shift` boost · click to capture mouse look · `Esc` release cursor.
The camera follows the terrain surface, and chunk streaming keys off the camera transform — swap
`ChunkManager.followTarget` to the tank transform when the tank exists.

---

## 5. Configuration

Everything procedural lives in `WorldSettings.asset` (or `SandLibrary.asset`). Nothing is hardcoded
in the generators.

| Setting | Default | Meaning |
|---|---|---|
| `worldSeed` | `123456` | Deterministic world identity |
| `chunkSize` | `64` m | Chunk side length |
| `viewDistanceInChunks` | `2` | 5×5 = 25 chunks resident |
| `terrainResolution` | `64` | 65×65 vertices/chunk (8 192 tris) |
| `terrainReliefScale` | `1` | **MASTER DIAL — scales all terrain height at once** |
| `maxHillHeight` | `14` m | Hard cap on hill height |
| `hillFrequency` | `0.0075` | Broad rolling hills (~130 m wavelength) |
| `hillAmplitude` | `2.4` | Hill dominance |
| `hillFlattening` | `0.2` | Rounds crests; low = more pronounced hills |
| `heightDistribution` | `1.35` | >1 ⇒ mostly low ground, rarer high hills |
| `terrainRegionScale` | `0.00055` | **Size of the terrain character patches** |
| `openRegionShare` | `0.45` | Share of the world that is gentle open ground |
| `unevenRegionShare` | `0.35` | Share that is choppy, uneven ground |
| `massiveHillRegionShare` | `0.2` | Share that gets the massive hills |
| `smallHillAmplitude` / `smallHillFrequency` | `0.75` / `0.019` | Gentle undulation everywhere |
| `unevenAmplitude` / `unevenFrequency` | `0.62` / `0.062` | Choppy folds inside uneven regions |
| `massiveHillMultiplier` | `2.1` | How much taller massive hills are |
| `massiveHillFrequency` | `0.0011` | Broadness of the massive hills |
| `massiveHillRounding` | `0.55` | Rounds the big hill crests so tanks can crest them |
| `grainAmplitude` / `grainFrequency` | `0.1` / `0.09` | Fine surface roughness |
| `depressionAmplitude` / `depressionFrequency` | `0.35` / `0.0022` | Shallow hollows |
| `sandVariationScale` | `0.0012` | **Sand region size** (lower = larger regions) |
| `sandWarpFrequency` / `sandWarpStrength` | `0.009` / `26` | Warps borders so they are never straight |
| `sandRangeScale` | `3.2` | Widens the band so all 12 sand types are reachable |
| `maxSandTypesPerChunk` | `4` | Sub meshes (draw calls) per chunk |
| `sandTextureWorldSize` | `16` m | Sand albedo tile size |
| `randomizeSandUvRotation` | `true` | Per-chunk texture rotation to hide tiling |
| `fogStartDistance` / `fogEndDistance` | `40` / `420` m | Dust fog band |
| `fogColor` | `(0.76, 0.68, 0.55)` | Dusty haze |
| `showDebugOverlay` | `true` | The temporary readout |

### If the terrain renders magenta

Magenta means the active render pipeline cannot draw the material's shader. `Mobile_RPAsset` /
`PC_RPAsset` are URP, and **URP cannot render the built-in `Standard` shader**.

The sand asset pack ships separate HDRP and URP unitypackages; the extracted
`Assets/YughuesFreeSandMaterials/Materials` folder originally contained the **built-in Standard**
variants, which is why the terrain first appeared magenta.

Fix (one click, non-destructive):
**`Tanks → Fix Sand Materials For URP`**, or headlessly
`-executeMethod Tanks.EditorTools.WastelandPrototypeRunner.FixSandMaterials`.

It updates the 12 materials in place:

| Standard | → URP Lit |
|---|---|
| `_MainTex` | `_BaseMap` |
| `_SpecGlossMap` | `_MetallicGlossMap` |
| `_Glossiness` | `_Smoothness` |
| `_Color` | `_BaseColor` |
| `_BumpMap`, `_Metallic`, `_BumpScale` | same names |

`_Metallic` is forced to 0 (sand is a dielectric) and `_Smoothness` is clamped to `0.05–0.35`,
otherwise the desert reads as polished metal.

**Missing albedo textures.** Only **8 of the 12** sand types ship a `_d` (diffuse) texture —
sand 01, 02, 04 and 05 have normal + specular maps but no albedo, so those materials had no
`_MainTex` at all and would render as flat untextured surfaces. For those four, an albedo is derived
from the pack's own specular map: luminance is taken (preserving the real grain while discarding the
specular highlight) and tinted with the material's authored `_Color`. The generated textures are
written to `Assets/YughuesFreeSandMaterials/Generated/` — **the original pack art is never modified
or overwritten.**

`SetupScene` and `SetupAndSaveScene` run the conversion automatically, so a fresh setup can never
produce a magenta world. `ProceduralWorld` also calls `SandLibrary.CountIncompatibleMaterials()` at
start-up and logs the exact fix if any material is still on `Standard`.

### Initialisation order (why `Awake` is guarded)

`AddComponent<ChunkManager>()` runs `ChunkManager.Awake()` **immediately**, before the caller can
call `Configure(...)`. So `Awake` only initialises when configuration is already present (the
inspector-assigned case); otherwise it stays quiet and `Configure` performs the initialisation.
This keeps both setup paths working:

* **Inspector-assigned** — settings exist at `Awake`, so the manager initialises there.
* **Runtime-created by `ProceduralWorld`** — `Awake` waits, then `Configure` initialises and
  re-enables the component.

`Initialise()` never disables the component; when the world is not configured yet it returns
quietly (reporting the problem at most once) so a later `Configure` can still succeed.

---

## 6. Determinism

The same `worldSeed` + the same chunk coordinates always rebuild byte-identical terrain.

* All noise is a hash of `(integer lattice point, seed)` — `NoiseUtility.Hash`.
* Heights and sand types are **pure functions of world position**: no per-chunk RNG state, no
  iteration-order dependence.
* Texture rotation comes from `Hash01(chunkX, chunkZ, seed)`, never `Random`.
* The sand type ordering comes from an ordinal path sort, so it is identical on every machine.

Verified by hashing the actual generated mesh of chunk `(4, -2)`, rebuilding from scratch and
comparing.

---

## 7. Terrain results (measured, not estimated)

From the terrain math harness over a wide sample area using the shipped defaults:

| Metric | Result |
|---|---|
| **Determinism** | `0.000E+000` m difference between repeated evaluations; 0 sand mismatches |
| **Chunk seams** | max height step across a chunk edge **0.000165 m** (float precision — no visible seam) |
| **Terrain characters** | open **41.2 %**, uneven **27.8 %**, massive hills **23.4 %**, transitions **7.7 %** |
| **Visible relief** | **1.76 m mean / 4.30 m worst within one 64 m chunk**; 7.19 m across the loaded 320 m view |
| **Global range** | −0.01 … 11.33 m over ~4 km (11.33 m span) |
| **Open ground** | 0.44 … 5.00 m, mean 2.57, roughness 0.0214 |
| **Uneven ground** | 0.69 … 5.09 m, mean 2.64, roughness **0.0296** (38 % choppier than open) |
| **Massive hills** | 3.43 … **11.42 m**, mean 7.45 |
| **Max slope** | **9.9°** per vertex step (open 6.6°, massive 9.9°) — comfortably drivable |
| **Sand types used** | **12 / 12** |
| **Regional cohesion** | **85.6 %** — large contiguous patches, not a checkerboard |

### Tuning the terrain size — use `terrainReliefScale` first

`terrainReliefScale` multiplies the **entire** terrain profile at once (hills, small hills, uneven
folds, massive hills), so you can make the world bigger or flatter without re-tuning each layer.
It is the first dial to reach for.

Measured with the other defaults, so you can pick a value directly:

| `terrainReliefScale` | Relief within a 64 m chunk | Across the 320 m view | Max slope | Notes |
|---|---|---|---|---|
| 0.5 | 1.76 m | 7.0 m | 8.1° | very flat |
| **1.0** (default) | **3.53 m** | **14.0 m** | **15.9°** | good balance |
| 1.5 | 5.29 m | 21.1 m | 23.1° | clearly hilly |
| 2.0 | 7.05 m | 28.1 m | 29.6° | **at the limit of tank drivability** |
| 3.0 | 10.6 m | 42.1 m | 40.4° | ⚠️ slopes too steep to climb |
| 6.0 | 21.2 m | 84.3 m | 59.6° | ⚠️ effectively cliffs |

Slopes cross the ~30° tank-climbable limit at about **relief 2**. Past that, `WorldSettings` warns at
start-up with an estimate of the slope your settings will produce, so you can experiment without
silently making the world undrivable.

`maxHillHeight` interacts with `hillFrequency`: **wide hills at the same height are much gentler than
short ones.** Raising `hillFrequency` widens the hills and softens the slopes, so it is a valid way to
keep tall terrain driveable. For reference, at `hillFrequency 0.01` a `maxHillHeight` of 96 m measures
**68° slopes with 44 % of the terrain unclimbable** — so height alone is not a safe dial.

For finer control, the individual dials still work as expected:

| Want | Change |
|---|---|
| Taller hills only | `hillAmplitude` ↑ (0–6) |
| More everyday undulation | `smallHillAmplitude` ↑ (0–4) |
| Rougher broken ground | `unevenAmplitude` ↑ (0–4), `unevenFrequency` ↑ |
| Bigger rare landforms | `massiveHillMultiplier` ↑ |
| Wider/narrower hills | `hillFrequency` ↓ / ↑ |
| Everything at once | `terrainReliefScale` |

### If parts of the terrain have no texture

Untextured patches (geometry present but no sand surface) mean a sub mesh was assigned a **null
material**, which the renderer draws with no valid shader.

This was a real bug and is now fixed. There were three compounding faults:

1. `TerrainMeshBuilder.GetLastSubMeshMaterials` cleared the **unused tail** of the material array to
   `null`.
2. Both callers then used **"the last element"** as their fallback — which was exactly that nulled
   slot. So every unresolved sub mesh inherited `null`.
3. `DominantSandIndex` derived indices with `% sandTypeCount`, which throws when the library is empty
   and could hand out an index outside the material array.

The fix guarantees a non-null material on every sub mesh:

* indices are wrapped safely into the library range (`Clamp`, tolerant of negatives)
* the fallback is the **first populated** library entry, never the nulled tail
* `TerrainChunk.Apply` patches any remaining gaps before assigning to the renderer
* `ChunkManager.Initialise` rejects an empty library up front and warns if any library entry is
  missing a material, naming it rather than silently falling back

So a sand type with no material now borrows another sand's material and logs a warning, instead of
rendering as a hole in the world.

### If patches of terrain look flat-coloured and untextured

Large flat patches with no visible sand grain mean the material is showing an **albedo with almost no
contrast**, not a missing material. Two things caused this and both are fixed:

1. **The pack's real diffuse art was being shadowed.** 8 of the 12 sands ship a genuine `_d` diffuse,
   but all 12 materials had been pointed at albedos derived from the specular maps instead. Those
   derived albedos are built from low contrast sources, so they render as washed-out, near-white
   planes (measured mean luminance 225-254 with a standard deviation of only 4-11), and Sand01's was
   so flat the exported PNG was 20 KB for a 1024x1024 image.
2. **The derivation itself was too weak.** It mapped the specular luminance straight onto a fixed
   0.65-1.35 brightness band, which clipped most sand types to near white and left almost no grain.

The converter now:

* prefers the pack's own diffuse whenever it exists (`M_YFSM_Sand03` -> `T_YFSM_03_d`), and re-derives
  only when a sand type genuinely ships no albedo (01, 02, 04, 05), where the derived albedo already
  present is left untouched rather than regenerated;
* **auto-levels** the source luminance (stretches the measured range to the full 0-1) so the real
  grain survives even on the nearly-uniform specular maps;
* normalises the authored tint to a sunlit brightness instead of multiplying the raw colour, so a
  dark Standard-era tint cannot drag the sand down;
* refuses to write a near-black albedo at all, logging a warning instead, because a black albedo is
  indistinguishable from an untextured patch.

Regenerating the four derived albedos brought them to mean luminance 179-200 with a standard
deviation of ~20-26 - real grain at a sunlit brightness, sitting correctly alongside the pack's own
sand art.

### If the terrain looks flat

A flat wasteland almost always means **procedural dials are sitting at 0**, because Unity
deserialises any field missing from a stored `.asset` as `0`. For these dials that is silently
destructive: an amplitude of 0 removes a terrain layer, and a frequency of 0 collapses a noise layer
to a constant.

Fix (one click): **`Tanks → Repair World Settings Asset`**, or headlessly
`-executeMethod Tanks.EditorTools.WastelandPrototypeRunner.RepairWorldSettings`.

This is now self-healing:

* `WorldSettings.OnValidate` → `RepairMissingFields()` restores defaults for any dial left at 0
  (only when it is exactly 0, so intentional values are preserved).
* `ProceduralWorld` calls `WorldSettings.ValidateProceduralSettings()` at start-up and logs a
  warning naming each disabled dial and what it does to the terrain.
* `Tanks → Setup Procedural Wasteland Assets` repairs the existing asset before reading it.

> **This is what happened once.** After the terrain-character fields were added, the previously saved
> `WorldSettings.asset` had none of them, so `terrainRegionScale`, `smallHillAmplitude`,
> `unevenAmplitude` and `massiveHillMultiplier` all loaded as **0** and the world rendered flat. The
> bug was invisible in the code — it lived entirely in the serialized asset — which is why the
> repair and the validation warning now exist.

### How the terrain variety works

The wasteland is **not one uniform noise field**. A very low frequency, warped region mask decides
which of three terrain characters covers each area, with soft borders between them:

| Character | What it looks like |
|---|---|
| **Open** | Mostly gentle, near-flat sand with small undulations — the driveable desert |
| **Uneven** | Choppy broken ground full of small folds and dips, from two crossed noise bands |
| **Massive hills** | Occasional large rolling landforms, broad-crested and 2–3× taller than anything else |

A small-hill layer applies everywhere so no area is ever perfectly flat. In massive-hill regions the
big hills **replace** the ordinary hills rather than stacking on them, which keeps total height
predictable instead of letting layers add into an unintended spike.

> **Why the region field is normalised.** Multi-octave noise averages toward its middle, so the raw
> region field only spanned **0.19 … 0.74** instead of 0 … 1. Feeding that straight into the share
> thresholds made massive hills *mathematically unreachable* — the share was configured at 20 % but
> measured at **0.0 %**. The field is now stretched by its measured band
> (`RegionFieldCenter = 0.466`, `RegionFieldHalfSpan = 0.30`) so the configured shares mean what they
> say. This was caught by the terrain harness, not by eye.

Sand coverage per type:

```
Sand01  3.79%   Sand02  4.94%   Sand03 14.02%   Sand04 11.06%
Sand05 18.73%   Sand06 10.05%   Sand07 19.67%   Sand08  7.58%
Sand09  5.90%   Sand10  2.06%   Sand11  1.57%   Sand12  0.64%
```

Borders between sand types follow the warped noise field, so they are irregular curves rather than
straight lines. Region borders land where the surface already changes, which reads as a natural
material change in the desert.

### Shader-level seam avoidance

* **Geometry:** every chunk samples one shared, continuous height function, so its edge vertices
  produce exactly the same heights as its neighbour's — no gaps, no cracks, no T-junctions.
* **Lighting:** normals are derived from the height field (not per-triangle face normals), so they
  are continuous across chunk boundaries — no lighting seam.
* **Texturing:** UVs are world-aligned at a fixed metres-per-tile scale, so the sand pattern lines up
  across chunks. The per-chunk rotation is applied about the chunk centre, which avoids introducing
  a directional discontinuity at the border.
* **Sand regions:** because the sand field is a function of world position, adjacent chunks agree on
  the sand type along their shared edge.

---

## 8. Mobile performance measures

* **Chunk pooling.** Released chunks are pushed to a `Stack` and rented again; the mesh is rebuilt
  into the existing `Mesh` object (`Clear(false)` + `SetVertices`), so driving does not allocate. Old
  meshes are only destroyed when the chunk object is destroyed.
* **Reused scratch buffers.** One `ScratchBuffers` instance owns the vertex / normal / UV / colour /
  height arrays and the sub mesh index lists for the whole session — no per-chunk GC.
* **Per-frame build budget.** `maxChunkBuildsPerFrame` (default 3) prevents a long hitch when
  streaming quickly. A chunk costs ~8 192 triangles, so three per frame is a manageable spike.
* **Budget maths.** 25 chunks × 8 192 tris = **204 800 triangles**, **~100 draw calls**, roughly
  1.6 MB of resident mesh data. Well inside mobile budgets.
* **No per-vertex GameObjects.** One GameObject per chunk, not per tile.
* **Renderer flags.** Light probes, reflection probes and motion vectors are disabled per chunk
  (unnecessary for terrain); shadows stay on for the directional sun.
* **32-bit indices only when needed.** Chunks above 65 000 vertices use `UInt32`; the default
  4 225-vertex chunk stays on the cheaper `UInt16` path.
* **Fog boundary.** The fog end distance (420 m) is inside the loaded radius (160 m from the player
  to the outer chunk edge at default settings) extended by the far clip plane, so newly generated
  chunks appear behind haze rather than popping into a clear view.

---

## 9. Verification

Run from the Unity menu, or headlessly:

```bat
Unity.exe -batchmode -quit -projectPath <project> ^
  -executeMethod Tanks.EditorTools.WastelandPrototypeRunner.SetupAndSaveScene

Unity.exe -batchmode -quit -projectPath <project> ^
  -executeMethod Tanks.EditorTools.WastelandPrototypeRunner.VerifyAll
```

| Entry point | What it checks |
|---|---|
| `Verify` | Determinism, seams, hill height, slopes, sand distribution, seed sensitivity |
| `StreamChunks` | Chunk generate / unload / pool, sand materials per chunk, mesh-hash determinism |
| `VerifyAll` | Both of the above |
| `SetupAndSaveScene` | Creates the assets and saves the configured scene |

Manual Play-Mode checklist:

1. Terrain appears under the camera immediately — real sand, not placeholder geometry.
2. Hills are small, broad and rounded; you can see a long way across the desert.
3. Multiple sand colours appear in large irregular regions with soft, non-straight borders.
4. Drive forward: new chunks stream in behind the fog; the loaded chunk count stays at 25.
5. The debug overlay shows the seed, chunk X/Z, loaded and generated counts.
6. Fog hides the streaming boundary without blocking your view of where you drive.

---

## 10. What is deliberately NOT here yet

No tank, no combat, no multiplayer, no inventory, no upgrades, no enemies, no UI, no networking.

Extension points already in place:

* `ChunkManager.ChunkLoaded` / `ChunkUnloaded` events — hook prop scattering, wrecks, vegetation,
  loot and points of interest onto a chunk as it appears.
* `ChunkManager.SampleTerrainHeight(x, z)` and `SnapToTerrain(position)` — place the tank, AI and
  spawns on the surface, even before the chunk loads.
* `ChunkManager.SetFollowTarget(transform)` — swap the dev camera for the tank in one line.
* `SandLibrary` — additive: add sand variations without touching generator code.
* `TerrainGenerator` — pure float maths, so it can move to a background thread or a job later.
* `WorldSettings` — add new procedural dials without hunting through code.



