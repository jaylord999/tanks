using UnityEngine;
using Tanks.World.Generation;

namespace Tanks.World.Sand
{
    /// <summary>
    /// Resolves which sand type covers a world position.
    ///
    /// Design notes:
    ///  - A low frequency noise field defines large contiguous sand regions (never a checkerboard).
    ///  - The field is warped by a second noise layer so region borders are irregular and organic
    ///    instead of straight lines.
    ///  - Everything is derived only from (world position, seed), so neighbouring chunks agree on
    ///    the sand type along their shared edge and the result is fully deterministic.
    ///
    /// Performance: the region noise is evaluated per chunk vertex, which is a few thousand samples
    /// per chunk - cheap compared to building the mesh itself.
    /// </summary>
    public class SandMaterialSelector
    {
        private readonly SandLibrary library;
        private readonly int seed;
        private readonly float variationScale;
        private readonly float warpFrequency;
        private readonly float warpStrength;
        private readonly float variationBlend;
        private readonly float detailFrequency;
        private readonly float elevationInfluence;
        private readonly float sandRangeScale;

        // Per sand type cumulative sampling window inside the normalised region field [0,1].
        private float[] windowStart;
        private float[] windowEnd;

        public SandMaterialSelector(SandLibrary library, WorldSettings settings)
        {
            this.library = library;
            seed = settings.WorldSeed;
            variationScale = settings.SandVariationScale;
            warpFrequency = settings.SandWarpFrequency;
            warpStrength = settings.SandWarpStrength;
            variationBlend = settings.SandVariationBlend;
            detailFrequency = Mathf.Max(0.00001f, settings.SandDetailFrequency);
            elevationInfluence = settings.SandElevationInfluence;
            sandRangeScale = Mathf.Max(1f, settings.SandRangeScale);
            RebuildWindows();
        }

        /// <summary>
        /// Recomputes the cumulative sampling windows from the library weights.
        /// Called once at start up (the sand library is treated as immutable while playing).
        /// </summary>
        public void RebuildWindows()
        {
            int count = library != null ? library.Count : 0;
            windowStart = new float[count];
            windowEnd = new float[count];

            if (count == 0)
            {
                return;
            }

            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
            {
                SandType type = library[i];
                totalWeight += type != null ? Mathf.Max(0.05f, type.regionWeight) : 1f;
            }

            float cursor = 0f;
            for (int i = 0; i < count; i++)
            {
                SandType type = library[i];
                float weight = type != null ? Mathf.Max(0.05f, type.regionWeight) : 1f;
                float span = weight / totalWeight;
                windowStart[i] = cursor;
                windowEnd[i] = cursor + span;
                cursor += span;
            }

            // Guard against float drift so the last window always reaches 1.
            windowEnd[count - 1] = 1.0001f;
        }

        /// <summary>Number of sand types known to the selector.</summary>
        public int SandTypeCount => windowStart != null ? windowStart.Length : 0;

        /// <summary>
        /// Normalised region value in [0,1] at a world position. 0 and 1 are opposite ends of the
        /// sand spectrum, intermediate values pick the sand types in library order.
        /// </summary>
        public float GetSandRegionValue(float worldX, float worldZ, float normalizedHeight)
        {
            // Layer 1: large, very low frequency regions.
            float baseValue = NoiseUtility.Fbm(
                worldX,
                worldZ,
                2,
                variationScale,
                2.11f,
                0.5f,
                seed + 4021);

            // Layer 2: mid frequency layer that gives each region its own internal character.
            float detailValue = NoiseUtility.Fbm(
                worldX,
                worldZ,
                2,
                detailFrequency,
                2.03f,
                0.45f,
                seed + 80231);

            // Warp the sampling position so borders meander instead of running straight.
            float warpX = NoiseUtility.Perlin(
                worldX * warpFrequency,
                worldZ * warpFrequency,
                seed + 17011);
            float warpZ = NoiseUtility.Perlin(
                worldX * warpFrequency + 31.7f,
                worldZ * warpFrequency - 17.3f,
                seed + 27011);

            float warpedRegion = NoiseUtility.Fbm(
                worldX + warpX * warpStrength,
                worldZ + warpZ * warpStrength,
                3,
                variationScale * 1.65f,
                2.13f,
                0.5f,
                seed + 53021);

            float value = warpedRegion * 0.55f + baseValue * 0.25f + detailValue * 0.2f * variationBlend;

            // Elevation influence: ridges and hollows expose slightly different sand.
            value += (normalizedHeight - 0.5f) * elevationInfluence * 0.25f;

            // Normalise. The weighted sum above only spans a fraction of [-1,1] in practice, which
            // would leave the outer sand types permanently unused. Widening the band before clamping
            // makes the full library reachable while keeping the large scale structure intact.
            return Mathf.Clamp01((value * sandRangeScale) * 0.5f + 0.5f);
        }

        /// <summary>
        /// Picks the sand type index (into <see cref="SandLibrary"/>) for a world position.
        /// </summary>
        public int GetSandTypeIndex(float worldX, float worldZ, float normalizedHeight)
        {
            return ResolveIndex(GetSandRegionValue(worldX, worldZ, normalizedHeight));
        }

        /// <summary>Maps a normalised region value onto a sand library index.</summary>
        public int ResolveIndex(float regionValue)
        {
            if (windowStart == null || windowStart.Length == 0)
            {
                return 0;
            }

            // Guard against NaN arriving from upstream maths: a NaN fails every comparison below and
            // would otherwise fall through to the last index, silently skewing the sand distribution.
            if (float.IsNaN(regionValue))
            {
                return 0;
            }

            for (int i = 0; i < windowStart.Length; i++)
            {
                if (regionValue <= windowEnd[i])
                {
                    return i;
                }
            }

            return windowStart.Length - 1;
        }

        /// <summary>
        /// True when the selector has a usable sand library. The mesh builder relies on this to avoid
        /// producing sub meshes it cannot supply a material for.
        /// </summary>
        public bool HasSandTypes => windowStart != null && windowStart.Length > 0;
    }
}
