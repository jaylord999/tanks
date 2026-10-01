using UnityEngine;
using Tanks.World.Sand;

namespace Tanks.World.Generation
{
    /// <summary>
    /// Pure, deterministic height/noise maths for the wasteland surface.
    ///
    /// The generator is presentation-agnostic: it fills plain float arrays so the same code can later
    /// run on a background thread or a job without touching the Unity API.
    ///
    /// Shape goals:
    ///  - mostly open, near flat sand
    ///  - gentle rolling hills with broad rounded tops
    ///  - shallow depressions
    ///  - no spikes, no cliffs, no artificial looking geometry
    /// </summary>
    public class TerrainGenerator
    {
        /// <summary>
        /// Measured centre of the multi-octave region field. Used to normalise the field's narrow
        /// output band across the full [0,1] range, so the configured region shares are reachable.
        /// </summary>
        private const float RegionFieldCenter = 0.466f;

        /// <summary>Measured half span of the region field, matching <see cref="RegionFieldCenter"/>.</summary>
        private const float RegionFieldHalfSpan = 0.30f;

        private readonly WorldSettings settings;
        private readonly SandMaterialSelector sandSelector;

        public TerrainGenerator(WorldSettings settings, SandMaterialSelector sandSelector)
        {
            this.settings = settings;
            this.sandSelector = sandSelector;
        }

        public WorldSettings Settings => settings;
        public SandMaterialSelector SandSelector => sandSelector;

        /// <summary>
        /// Fills <paramref name="heights"/> with the raw terrain height for every vertex of a chunk.
        /// The vertex array is laid out row major: index = z * vertexCount + x, where x runs along
        /// +X and z runs along +Z of the chunk in world space.
        /// </summary>
        public void FillHeights(int chunkX, int chunkZ, float[] heights)
        {
            int vertexCount = settings.ChunkVertexCount;
            float chunkSize = settings.ChunkSize;
            float step = chunkSize / settings.TerrainResolution;
            float originX = chunkX * chunkSize;
            float originZ = chunkZ * chunkSize;

            for (int z = 0; z < vertexCount; z++)
            {
                float worldZ = originZ + z * step;
                int row = z * vertexCount;

                for (int x = 0; x < vertexCount; x++)
                {
                    float worldX = originX + x * step;
                    heights[row + x] = SampleHeight(worldX, worldZ);
                }
            }
        }

        /// <summary>
        /// Evaluates the wasteland height at a single world position.
        /// Exposed publicly so gameplay code (tank placement, prop scattering, spawn logic) can query
        /// the exact surface height without waiting for a chunk to be loaded.
        ///
        /// The surface is composed of four layers:
        ///  1. broad rolling hills  - the base shape of the landscape
        ///  2. small hills          - gentle undulation everywhere, breaks up open sand
        ///  3. uneven folds         - choppy local detail, concentrated in uneven regions
        ///  4. massive hills        - occasional large landforms, only in massive-hill regions
        ///
        /// A low frequency region mask decides how strongly layers 3 and 4 apply, so the world reads as
        /// patches of open ground, patches of rough broken ground, and the occasional big hill - rather
        /// than one uniform field.
        /// </summary>
        public float SampleHeight(float worldX, float worldZ)
        {
            float maxHillHeight = settings.MaxHillHeight;
            if (maxHillHeight <= 0f)
            {
                return settings.TerrainBaseHeight;
            }

            // Which terrain character covers this spot.
            SampleTerrainRegionWeights(
                worldX, worldZ,
                out float openWeight, out float unevenWeight, out float massiveWeight);

            // ---- Layer 1: broad rolling hills (always present, the dominant shape) ----------------
            float hills = NoiseUtility.RidgedWasteland(
                worldX,
                worldZ,
                settings.HillOctaves,
                settings.HillFrequency,
                2.07f,
                0.48f,
                settings.WorldSeed + 101);

            hills = SoftenHills(hills, settings.HillFlattening);

            // ---- Layer 2: small hills (gentle undulation everywhere) ------------------------------
            // Reduced inside uneven regions, where layer 3 takes over and would otherwise double up.
            float smallHillStrength = settings.SmallHillAmplitude * Mathf.Lerp(1f, 0.45f, unevenWeight);

            float smallHills = NoiseUtility.Fbm(
                worldX,
                worldZ,
                3,
                settings.SmallHillFrequency,
                2.09f,
                0.5f,
                settings.WorldSeed + 811);

            // ---- Layer 3: uneven folds (choppy ground, only in uneven regions) --------------------
            // Two frequencies at an angle to each other so the folds never line up into rows.
            float folds = NoiseUtility.RidgedWasteland(
                worldX,
                worldZ,
                3,
                settings.UnevenFrequency,
                2.17f,
                0.52f,
                settings.WorldSeed + 907);

            float crossFolds = NoiseUtility.Fbm(
                worldX * 0.7f + worldZ * 0.4f,
                worldZ * 0.7f - worldX * 0.4f,
                2,
                settings.UnevenFrequency * 1.9f,
                2.03f,
                0.5f,
                settings.WorldSeed + 1009);

            float unevenDetail = folds * 0.62f + crossFolds * 0.38f;
            float unevenStrength = settings.UnevenAmplitude * unevenWeight;

            // ---- Layer 4: massive hills (rare, large landforms) -----------------------------------
            float massiveHills = NoiseUtility.RidgedWasteland(
                worldX,
                worldZ,
                2,
                settings.MassiveHillFrequency,
                2.03f,
                0.5f,
                settings.WorldSeed + 1103);

            // Round the big hills off so their crests stay broad and a tank can still crest them
            // rather than being stopped by a sharp peak.
            massiveHills = SoftenHills(massiveHills, settings.MassiveHillRounding);

            // ---- Fine grain surface roughness ----------------------------------------------------
            float grain = NoiseUtility.Perlin(
                worldX * settings.GrainFrequency,
                worldZ * settings.GrainFrequency,
                settings.WorldSeed + 307);

            // ---- Shallow depressions -------------------------------------------------------------
            float depressions = NoiseUtility.Fbm(
                worldX,
                worldZ,
                2,
                settings.DepressionFrequency,
                2.01f,
                0.5f,
                settings.WorldSeed + 409);

            return ComposeHeight(
                hills, smallHills, smallHillStrength, unevenDetail, unevenStrength,
                massiveHills, massiveWeight, grain, depressions, maxHillHeight);
        }

        /// <summary>
        /// Blend weights for the three terrain characters at a world position.
        ///
        /// The wasteland is not one uniform noise field: different areas have a different feel. A very
        /// low frequency, warped noise field decides which character covers an area, and the weights
        /// fade between them so the transitions are gradual rather than abrupt.
        ///
        /// Like everything else in the generator this is a pure function of world position, so
        /// neighbouring chunks agree along their shared edge.
        /// </summary>
        /// <param name="openWeight">Mostly flat, gentle, driveable ground.</param>
        /// <param name="unevenWeight">Choppy ground full of small folds and dips.</param>
        /// <param name="massiveWeight">Occasional large rolling hills.</param>
        public void SampleTerrainRegionWeights(
            float worldX,
            float worldZ,
            out float openWeight,
            out float unevenWeight,
            out float massiveWeight)
        {
            // Warp the sample position so region borders wander instead of running in straight lines.
            float warpX = NoiseUtility.Perlin(
                worldX * settings.TerrainRegionWarp,
                worldZ * settings.TerrainRegionWarp,
                settings.WorldSeed + 6101);

            float warpZ = NoiseUtility.Perlin(
                worldX * settings.TerrainRegionWarp + 19.3f,
                worldZ * settings.TerrainRegionWarp - 43.1f,
                settings.WorldSeed + 6203);

            float region = NoiseUtility.Fbm(
                worldX + warpX * settings.TerrainRegionWarpStrength,
                worldZ + warpZ * settings.TerrainRegionWarpStrength,
                3,
                settings.TerrainRegionScale,
                2.05f,
                0.5f,
                settings.WorldSeed + 6301);

            // Multi-octave noise averages toward its middle, so the raw field only spans a narrow band
            // (measured: roughly 0.19 .. 0.74 rather than 0 .. 1). Feeding that straight into the share
            // thresholds would make the outer characters unreachable - the massive hills would simply
            // never appear no matter how the shares were set. Stretching the observed range back out
            // across [0,1] makes the configured shares mean what they say.
            float normalized = Mathf.Clamp01((region - RegionFieldCenter) / RegionFieldHalfSpan + 0.5f);
            float value = normalized;

            // Cumulative thresholds derived from the configured shares, so the dials stay intuitive:
            // openRegionShare = 0.45 means 45% of the world is open ground.
            float openThreshold = settings.OpenRegionShare;
            float unevenThreshold = Mathf.Clamp01(openThreshold + settings.UnevenRegionShare);

            // Soft blend bands keep the borders organic instead of stepping between characters.
            // The band width is a fraction of the share, so small shares still get soft edges.
            float openBand = Mathf.Max(0.02f, openThreshold * 0.35f);
            float unevenBand = Mathf.Max(0.02f, settings.UnevenRegionShare * 0.35f);

            float open = 1f - SmoothStep(openThreshold - openBand, openThreshold + openBand, value);
            float massive = SmoothStep(unevenThreshold - unevenBand, unevenThreshold + unevenBand, value);
            float uneven = Mathf.Max(0f, 1f - open - massive);

            float total = open + uneven + massive;
            if (total <= 0.0001f)
            {
                openWeight = 1f;
                unevenWeight = 0f;
                massiveWeight = 0f;
                return;
            }

            openWeight = open / total;
            unevenWeight = uneven / total;
            massiveWeight = massive / total;
        }

        /// <summary>Hermite smoothstep between two edges.</summary>
        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1))
            {
                return x < edge0 ? 0f : 1f;
            }

            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Combines the four layers into the final surface height.</summary>
        private float ComposeHeight(
            float hills,
            float smallHills,
            float smallHillStrength,
            float unevenDetail,
            float unevenStrength,
            float massiveHills,
            float massiveBlend,
            float grain,
            float depressions,
            float maxHillHeight)
        {
            // The hill term eases off in massive regions, because there the big hill layer takes over
            // rather than stacking on top of the ordinary hills. That keeps total height predictable
            // and prevents the two layers from adding up into an unintended spike.
            float hillTerm = Mathf.Lerp(hills, massiveHills, massiveBlend) * settings.HillAmplitude;

            float baseAmplitude = settings.HillAmplitude;
            float weightSum = Mathf.Max(
                0.0001f,
                baseAmplitude + smallHillStrength + unevenStrength + settings.GrainAmplitude);

            float normalized =
                (hillTerm +
                 smallHills * smallHillStrength +
                 unevenDetail * unevenStrength +
                 grain * settings.GrainAmplitude) / weightSum;

            // Remap the noise band so the terrain spends most of its time low and only occasionally
            // climbs toward the maximum. The distribution relaxes as massive hills take over, so big
            // hills actually reach their height instead of being flattened by the curve.
            float distribution = Mathf.Lerp(settings.HeightDistribution, 1.3f, massiveBlend);
            float shaped = Mathf.Pow(Mathf.Clamp01(normalized * 0.5f + 0.5f), distribution);

            // Massive hills get extra headroom so they stand out above the surrounding desert.
            float heightScale = Mathf.Lerp(1f, settings.MassiveHillMultiplier, massiveBlend);
            float positiveHeight = shaped * maxHillHeight * heightScale;

            // Shallow depressions subtract a little so some ground sits below the base plane.
            float depressionDepth = Mathf.Max(0f, -depressions) * maxHillHeight * settings.DepressionAmplitude;

            // Master relief dial: scales the whole profile at once, so the terrain can be made
            // noticeably taller or flatter without re-tuning every individual layer. Applied after the
            // layers are combined, which keeps their relative proportions intact.
            float relief = settings.TerrainReliefScale;

            return settings.TerrainBaseHeight + (positiveHeight - depressionDepth) * relief;
        }

        /// <summary>
        /// Rounds off the hill profile. Uses a smooth signed power curve so peaks flatten and stay
        /// crest-able by a tank while valleys remain broad and shallow.
        /// </summary>
        private static float SoftenHills(float value, float flattening)
        {
            if (flattening <= 0f)
            {
                return value;
            }

            // Lift the raw noise into a strictly positive, gently rounded profile. The 0.5 + 0.5
            // remap followed by a mild power keeps the hill tops rounded (broad crests a tank can
            // climb) without collapsing the landscape into a flat plane.
            float positive = Mathf.Clamp01(value * 0.5f + 0.5f);
            float exponent = 1f / (1f + flattening * 0.5f);

            // Blend between raw and shaped so the tuning dial stays continuous.
            return Mathf.Lerp(value, Mathf.Pow(positive, exponent) * 2f - 1f, flattening);
        }
    }
}
