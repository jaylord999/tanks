using UnityEngine;

namespace Tanks.World
{
    /// <summary>
    /// Central, inspector driven configuration for the procedural wasteland.
    /// Every procedural value lives here (or in the sand library) so nothing is hardcoded across
    /// the chunk / terrain systems and so tuning never requires touching code.
    /// </summary>
    [CreateAssetMenu(
        fileName = "WorldSettings",
        menuName = "Tanks/World/World Settings",
        order = 0)]
    public class WorldSettings : ScriptableObject
    {
        public enum SandBlendMode
        {
            /// <summary>Hard per vertex material assignment (default, a few materials per chunk).</summary>
            Hard,

            /// <summary>Per triangle material assignment with thin triangles on the region seams.</summary>
            Dithered
        }

        [Header("World Identity")]
        [Tooltip("Deterministic world seed. Same seed + same chunk coordinates = same terrain, always.")]
        [SerializeField] private int worldSeed = 123456;

        [Header("Chunk Layout")]
        [Tooltip("Side length of a single terrain chunk in world units (metres).")]
        [SerializeField, Min(8f)] private float chunkSize = 64f;

        [Tooltip("How many chunks in each direction around the player stay loaded. 2 = 5x5 grid.")]
        [SerializeField, Range(0, 16)] private int viewDistanceInChunks = 2;

        [Tooltip("Vertex grid resolution per chunk. 64 = 65x65 vertices (a good mobile default).")]
        [SerializeField, Range(8, 255)] private int terrainResolution = 64;

        [Tooltip("Extra chunk rings generated and kept resident so growing the view has no hitch.")]
        [SerializeField, Range(0, 4)] private int preloadChunkPadding = 1;

        [Tooltip("World Y origin offset of the terrain surface.")]
        [SerializeField] private float terrainBaseHeight = 0f;

        [Header("Terrain Shape - Gentle Rolling Hills")]
        [Tooltip("MASTER DIAL. Scales every part of the terrain height at once (hills, small hills, " +
                 "uneven folds and massive hills). 1 = as authored, 2 = twice as tall, 0.5 = half. " +
                 "Use this first: raise it until the terrain looks right, then fine tune the layers below.")]
        [SerializeField, Range(0f, 6f)] private float terrainReliefScale = 1f;

        [Tooltip("Maximum hill height above the base plane, in metres. Keep this small for tanks.")]
        [SerializeField, Range(0f, 200f)] private float maxHillHeight = 14f;

        [Tooltip("Noise frequency of the broad rolling hills. Lower = wider, smoother hills.")]
        [SerializeField, Range(0.0001f, 0.05f)] private float hillFrequency = 0.0075f;

        [Tooltip("Amplitude of the low frequency hills as a fraction of maxHillHeight.")]
        [SerializeField, Range(0f, 6f)] private float hillAmplitude = 2.4f;

        [Tooltip("Amplitude of the fine grain surface roughness (keeps the sand from looking flat).")]
        [SerializeField, Range(0f, 1f)] private float grainAmplitude = 0.1f;

        [Tooltip("Frequency of the fine grain surface roughness.")]
        [SerializeField, Range(0.0001f, 0.5f)] private float grainFrequency = 0.09f;

        [Tooltip("Number of noise octaves for the hill layer. More octaves = more detail, more cost.")]
        [SerializeField, Range(1, 8)] private int hillOctaves = 4;

        [Tooltip("How strongly hill tops flatten out, so tanks can always crest hills.")]
        [SerializeField, Range(0f, 1f)] private float hillFlattening = 0.2f;

        [Tooltip("Shallow depression amplitude. Keeps some terrain below the base plane.")]
        [SerializeField, Range(0f, 1f)] private float depressionAmplitude = 0.35f;

        [Tooltip("Frequency of the shallow depressions.")]
        [SerializeField, Range(0.0001f, 0.05f)] private float depressionFrequency = 0.0022f;

        [Tooltip("Shapes the height curve. 1 = raw noise. Higher = more low open ground and rarer tall hills.")]
        [SerializeField, Range(1f, 4f)] private float heightDistribution = 1.35f;

        [Header("Terrain Characters (region mask)")]
        [Tooltip("Frequency of the field that decides which terrain character covers an area.")]
        [SerializeField, Range(0.0001f, 0.02f)] private float terrainRegionScale = 0.00055f;

        [Tooltip("Frequency of the warp that keeps terrain region borders organic rather than straight.")]
        [SerializeField, Range(0.0001f, 0.02f)] private float terrainRegionWarp = 0.0032f;

        [Tooltip("World unit strength of the terrain region border warping.")]
        [SerializeField, Range(0f, 400f)] private float terrainRegionWarpStrength = 90f;

        [Tooltip("How much of the world is mostly open, gentle ground.")]
        [SerializeField, Range(0f, 1f)] private float openRegionShare = 0.45f;

        [Tooltip("How much of the world is choppy, uneven ground full of small folds.")]
        [SerializeField, Range(0f, 1f)] private float unevenRegionShare = 0.35f;

        [Tooltip("How much of the world gets the massive hills.")]
        [SerializeField, Range(0f, 1f)] private float massiveHillRegionShare = 0.2f;

        [Header("Small Hills")]
        [Tooltip("Amplitude of the small hills present everywhere, as a fraction of maxHillHeight.")]
        [SerializeField, Range(0f, 4f)] private float smallHillAmplitude = 0.75f;

        [Tooltip("Frequency of the small hills.")]
        [SerializeField, Range(0.0001f, 0.12f)] private float smallHillFrequency = 0.019f;

        [Header("Uneven Ground")]
        [Tooltip("Amplitude of the choppy folds applied inside uneven regions.")]
        [SerializeField, Range(0f, 4f)] private float unevenAmplitude = 0.62f;

        [Tooltip("Frequency of the choppy folds. Higher = smaller, busier bumps.")]
        [SerializeField, Range(0.0001f, 0.25f)] private float unevenFrequency = 0.062f;

        [Header("Massive Hills")]
        [Tooltip("Extra height multiplier for massive hill regions, relative to maxHillHeight.")]
        [SerializeField, Range(1f, 4f)] private float massiveHillMultiplier = 2.1f;

        [Tooltip("Frequency of the massive hills. Lower = broader, more sprawling ridges.")]
        [SerializeField, Range(0.0001f, 0.02f)] private float massiveHillFrequency = 0.0011f;

        [Tooltip("How rounded the massive hills are. Higher = broader crests a tank can still crest.")]
        [SerializeField, Range(0f, 1f)] private float massiveHillRounding = 0.55f;

        [Header("Sand Regions")]
        [Tooltip("Frequency of the noise that picks which sand type covers an area. Low = large regions.")]
        [SerializeField, Range(0.0001f, 0.02f)] private float sandVariationScale = 0.0012f;

        [Tooltip("Second sand noise layer, used to warp the region borders so they are never straight.")]
        [SerializeField, Range(0.0001f, 0.05f)] private float sandWarpFrequency = 0.009f;

        [Tooltip("World unit strength of the border warping.")]
        [SerializeField, Range(0f, 200f)] private float sandWarpStrength = 26f;

        [Tooltip("How strongly the second sand layer perturbs the region value. Higher = more variety.")]
        [SerializeField, Range(0f, 1f)] private float sandVariationBlend = 0.4f;

        [Tooltip("Extra sand detail frequency used to avoid obvious repeating patches.")]
        [SerializeField, Range(0.0001f, 0.05f)] private float sandDetailFrequency = 0.02f;

        [Tooltip("How strongly sand regions follow elevation (ridges expose different sand).")]
        [SerializeField, Range(0f, 2f)] private float sandElevationInfluence = 0.35f;

        [Tooltip("Widens the sand region band so the outer sand types in the library are reachable.")]
        [SerializeField, Range(1f, 6f)] private float sandRangeScale = 3.2f;

        [Tooltip("Number of sand types used per chunk. 4 is the mobile sweet spot.")]
        [SerializeField, Range(1, 12)] private int maxSandTypesPerChunk = 4;

        [Tooltip("How sand types are painted onto the terrain mesh.")]
        [SerializeField] private SandBlendMode sandBlendMode = SandBlendMode.Hard;

        [Header("Sand Texturing")]
        [Tooltip("World size in metres of one sand albedo tile. Smaller = more visible detail.")]
        [SerializeField, Range(1f, 200f)] private float sandTextureWorldSize = 16f;

        [Tooltip("Random rotation applied to the sand texture per chunk to hide tiling.")]
        [SerializeField] private bool randomizeSandUvRotation = true;

        [Header("Atmosphere")]
        [Tooltip("Enable the atmospheric dust fog.")]
        [SerializeField] private bool enableFog = true;

        [Tooltip("Fog colour of the dusty wasteland haze.")]
        [SerializeField] private Color fogColor = new Color(0.76f, 0.68f, 0.55f, 1f);

        [Tooltip("Distance in metres where the fog starts to be visible.")]
        [SerializeField, Range(0f, 2000f)] private float fogStartDistance = 40f;

        [Tooltip("Distance in metres where the world is fully obscured. Must exceed the view distance.")]
        [SerializeField, Range(10f, 5000f)] private float fogEndDistance = 420f;

        [Tooltip("Fog exponent. 1 = linear. Values above 1 tighten the fog toward the horizon.")]
        [SerializeField, Range(0.25f, 4f)] private float fogDensityPower = 1f;

        [Tooltip("Ambient sky tint used for the wasteland lighting setup.")]
        [SerializeField] private Color ambientSkyColor = new Color(0.62f, 0.56f, 0.46f, 1f);

        [Tooltip("Ambient ground bounce tint.")]
        [SerializeField] private Color ambientGroundColor = new Color(0.32f, 0.27f, 0.21f, 1f);

        [Tooltip("Sun colour of the wasteland sky.")]
        [SerializeField] private Color sunColor = new Color(1f, 0.94f, 0.82f, 1f);

        [Tooltip("Sun brightness.")]
        [SerializeField, Range(0f, 8f)] private float sunIntensity = 1.35f;

        [Tooltip("Sun rotation in degrees (x = pitch, y = yaw).")]
        [SerializeField] private Vector2 sunAngles = new Vector2(38f, 140f);

        [Header("Debug")]
        [Tooltip("Draw the development overlay with seed / chunk / chunk count information.")]
        [SerializeField] private bool showDebugOverlay = true;

        [Tooltip("Log chunk load / unload events to the console.")]
        [SerializeField] private bool logChunkEvents = false;

        public int WorldSeed => worldSeed;
        public float ChunkSize => chunkSize;
        public int ViewDistanceInChunks => viewDistanceInChunks;
        public int TerrainResolution => terrainResolution;
        public int PreloadChunkPadding => preloadChunkPadding;
        public float TerrainBaseHeight => terrainBaseHeight;

        public float MaxHillHeight => maxHillHeight;
        public float TerrainReliefScale => terrainReliefScale;
        public float HillFrequency => hillFrequency;
        public float HillAmplitude => hillAmplitude;
        public float GrainAmplitude => grainAmplitude;
        public float GrainFrequency => grainFrequency;
        public int HillOctaves => hillOctaves;
        public float HillFlattening => hillFlattening;
        public float DepressionAmplitude => depressionAmplitude;
        public float DepressionFrequency => depressionFrequency;
        public float HeightDistribution => heightDistribution;

        public float TerrainRegionScale => terrainRegionScale;
        public float TerrainRegionWarp => terrainRegionWarp;
        public float TerrainRegionWarpStrength => terrainRegionWarpStrength;
        public float OpenRegionShare => openRegionShare;
        public float UnevenRegionShare => unevenRegionShare;
        public float MassiveHillRegionShare => massiveHillRegionShare;

        public float SmallHillAmplitude => smallHillAmplitude;
        public float SmallHillFrequency => smallHillFrequency;
        public float UnevenAmplitude => unevenAmplitude;
        public float UnevenFrequency => unevenFrequency;
        public float MassiveHillMultiplier => massiveHillMultiplier;
        public float MassiveHillFrequency => massiveHillFrequency;
        public float MassiveHillRounding => massiveHillRounding;

        public float SandVariationScale => sandVariationScale;
        public float SandWarpFrequency => sandWarpFrequency;
        public float SandWarpStrength => sandWarpStrength;
        public float SandVariationBlend => sandVariationBlend;
        public float SandDetailFrequency => sandDetailFrequency;
        public float SandElevationInfluence => sandElevationInfluence;
        public float SandRangeScale => sandRangeScale;
        public int MaxSandTypesPerChunk => maxSandTypesPerChunk;
        public SandBlendMode BlendMode => sandBlendMode;
        public float SandTextureWorldSize => sandTextureWorldSize;
        public bool RandomizeSandUvRotation => randomizeSandUvRotation;

        public bool EnableFog => enableFog;
        public Color FogColor => fogColor;
        public float FogStartDistance => fogStartDistance;
        public float FogEndDistance => fogEndDistance;
        public float FogDensityPower => fogDensityPower;
        public Color AmbientSkyColor => ambientSkyColor;
        public Color AmbientGroundColor => ambientGroundColor;
        public Color SunColor => sunColor;
        public float SunIntensity => sunIntensity;
        public Vector2 SunAngles => sunAngles;

        public bool ShowDebugOverlay => showDebugOverlay;
        public bool LogChunkEvents => logChunkEvents;

        /// <summary>Number of vertices along one edge of a chunk mesh (resolution + 1).</summary>
        public int ChunkVertexCount => terrainResolution + 1;

        /// <summary>Distance in metres from the player to the outer edge of the loaded area.</summary>
        public float LoadedWorldRadius => (viewDistanceInChunks + 0.5f) * chunkSize;

        /// <summary>
        /// Reports procedural dials that are effectively disabled, which would make the terrain
        /// flatter or more uniform than intended. Returns the number of problems found and logs each
        /// one by name, so a silently flattened world is never left to guesswork.
        /// </summary>
        public int ValidateProceduralSettings()
        {
            int problems = 0;

            problems += WarnIf(terrainRegionScale <= 0f, nameof(terrainRegionScale),
                "terrain character regions collapse to a single uniform character");
            problems += WarnIf(terrainReliefScale <= 0f, nameof(terrainReliefScale),
                "the master relief scale flattens the entire terrain to the base plane");
            problems += WarnIf(hillFrequency <= 0f, nameof(hillFrequency),
                "the rolling hill layer becomes a constant, removing all hills");
            problems += WarnIf(hillAmplitude <= 0f, nameof(hillAmplitude),
                "the rolling hill layer is muted");
            problems += WarnIf(smallHillFrequency <= 0f || smallHillAmplitude <= 0f,
                "smallHillAmplitude / smallHillFrequency",
                "small hills are muted, so open ground will be perfectly flat");
            problems += WarnIf(massiveHillMultiplier <= 0f, nameof(massiveHillMultiplier),
                "massive hills are multiplied down to zero height");
            problems += WarnIf(massiveHillFrequency <= 0f, nameof(massiveHillFrequency),
                "the massive hill layer becomes a constant");
            problems += WarnIf(unevenFrequency <= 0f || unevenAmplitude <= 0f,
                "unevenAmplitude / unevenFrequency",
                "uneven ground is muted, so all terrain will feel the same");
            problems += WarnIf(openRegionShare + unevenRegionShare + massiveHillRegionShare <= 0.0001f,
                "region shares",
                "no terrain character is selected anywhere");

            if (problems > 0)
            {
                Debug.LogError(
                    $"[WorldSettings] {problems} procedural setting(s) are disabled or contradictory, so the " +
                    "terrain will be flatter or more uniform than intended. Restore the defaults in " +
                    "WorldSettings.asset, or run 'Tanks/Repair World Settings Asset'. A value of exactly 0 " +
                    "usually means the asset predates a field that was added later, which Unity loads as 0.");
            }

            return problems;
        }

        /// <summary>
        /// Estimates the steepest slope the current settings will produce, in degrees.
        ///
        /// Slope is how much height is gained over how short a distance, so this divides the vertical
        /// scale by the hill wavelength. Wavelength is the reciprocal of frequency, which is why
        /// frequency belongs in this estimate and height alone does not predict steepness: wide hills at
        /// the same height are far gentler to climb.
        ///
        /// The raw single-ridge figure underestimates the real surface, because the terrain stacks four
        /// noise layers rather than being one clean sine. Measured against the actual generator across a
        /// grid of heights and frequencies, it needed this correction factor and a 90 degree clamp
        /// (slope cannot exceed vertical however tall the hills are).
        ///
        /// Calibrated result at the 30 degree drivability threshold: it errs slightly on the side of
        /// caution rather than letting unclimbable terrain through silently.
        /// </summary>
        public float EstimateMaxSlopeDegrees()
        {
            float amplitude = maxHillHeight * terrainReliefScale * hillAmplitude;

            // Wavelength of the tall terrain: broad hills dominate, with the massive hills mixed in.
            float hillWavelength = 1f / Mathf.Max(0.0001f, hillFrequency);
            float massiveWavelength = 1f / Mathf.Max(0.0001f, massiveHillFrequency);
            float dominantWavelength = Mathf.Lerp(hillWavelength, massiveWavelength, 0.25f);

            // Rise over run for a ridge of this height and wavelength.
            float slope = amplitude * Mathf.PI / Mathf.Max(1f, dominantWavelength);

            // Correction for the stacked layers the single-ridge model does not account for.
            const float layerCorrection = 1.95f;

            return Mathf.Min(90f, Mathf.Atan(slope * layerCorrection) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Warns when the current settings will create terrain a tank cannot climb.
        /// Returns 1 when a problem is found, otherwise 0.
        /// </summary>
        public int WarnIfTooSteep()
        {
            float slope = EstimateMaxSlopeDegrees();

            if (slope <= 30f)
            {
                return 0;
            }

            Debug.LogWarning(
                $"[WorldSettings] Terrain will be too steep to drive: estimated slopes up to " +
                $"{slope:F0} degrees (a tank can climb about 30). To reduce it, lower any of: " +
                $"maxHillHeight (currently {maxHillHeight:F0} m), terrainReliefScale " +
                $"({terrainReliefScale:F2}), hillAmplitude ({hillAmplitude:F2}), or raise hillFrequency " +
                $"({hillFrequency:F4}) to make hills wider and therefore gentler. " +
                "Note that height alone does not decide steepness - wide hills at the same height are " +
                "far easier to climb.");

            return 1;
        }

        private static int WarnIf(bool condition, string fieldName, string effect)
        {
            if (!condition)
            {
                return 0;
            }

            Debug.LogWarning($"[WorldSettings] '{fieldName}' is disabled: {effect}.");
            return 1;
        }

        private void OnValidate()
        {
            // Keep the fog sane so the chunk streaming boundary stays hidden but the world stays readable.
            if (fogEndDistance <= fogStartDistance)
            {
                fogEndDistance = fogStartDistance + 50f;
            }

            if (maxSandTypesPerChunk < 1)
            {
                maxSandTypesPerChunk = 1;
            }

            RepairMissingFields();
        }

        /// <summary>
        /// Restores defaults for procedural dials that deserialised as 0.
        ///
        /// Unity fills any field missing from the stored .asset with 0. For these dials that is never a
        /// valid value and is silently destructive: an amplitude of 0 removes a terrain layer, and a
        /// frequency of 0 collapses a noise layer to a constant - which is what made the wasteland
        /// render completely flat after new settings were added.
        ///
        /// Values are only replaced when they are exactly 0, so intentional tuning (including 0 on
        /// dials where 0 is meaningful, like the region shares) is left alone.
        /// </summary>
        private void RepairMissingFields()
        {
            // Amplitudes, frequencies and sizes: 0 is always invalid for these.
            if (terrainReliefScale <= 0f)
            {
                terrainReliefScale = 1f;
            }

            if (hillFrequency <= 0f)
            {
                hillFrequency = 0.0075f;
            }

            if (hillAmplitude <= 0f)
            {
                hillAmplitude = 2.4f;
            }

            if (smallHillFrequency <= 0f)
            {
                smallHillFrequency = 0.019f;
            }

            if (smallHillAmplitude <= 0f)
            {
                smallHillAmplitude = 0.75f;
            }

            if (unevenFrequency <= 0f)
            {
                unevenFrequency = 0.062f;
            }

            if (unevenAmplitude <= 0f)
            {
                unevenAmplitude = 0.62f;
            }

            if (massiveHillFrequency <= 0f)
            {
                massiveHillFrequency = 0.0011f;
            }

            if (massiveHillMultiplier <= 0f)
            {
                massiveHillMultiplier = 2.1f;
            }

            if (terrainRegionScale <= 0f)
            {
                terrainRegionScale = 0.00055f;
            }

            if (terrainRegionWarp <= 0f)
            {
                terrainRegionWarp = 0.0032f;
            }

            if (terrainRegionWarpStrength <= 0f)
            {
                terrainRegionWarpStrength = 90f;
            }

            if (grainFrequency <= 0f)
            {
                grainFrequency = 0.09f;
            }

            if (depressionFrequency <= 0f)
            {
                depressionFrequency = 0.0022f;
            }

            if (chunkSize <= 0f)
            {
                chunkSize = 64f;
            }

            // Terrain region shares: if they are all 0 the mask never selects anything, so fall back
            // to the intended mix. Individual zeros are allowed as long as the total is meaningful.
            float shareTotal = openRegionShare + unevenRegionShare + massiveHillRegionShare;
            if (shareTotal <= 0.0001f)
            {
                openRegionShare = 0.45f;
                unevenRegionShare = 0.35f;
                massiveHillRegionShare = 0.2f;
            }
        }
    }
}
