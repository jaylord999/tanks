using System.Collections.Generic;
using UnityEngine;

namespace Tanks.World.Sand
{
    /// <summary>
    /// A single sand variation available to the procedural world.
    /// Wraps an existing project material so the terrain uses the art already in the project rather
    /// than generated placeholder looks.
    /// </summary>
    [System.Serializable]
    public class SandType
    {
        [Tooltip("Display name used by the debug overlay.")]
        public string displayName = "Sand";

        [Tooltip("Material applied to chunk sub meshes covered by this sand type.")]
        public Material material;

        [Tooltip("Relative area weight. Higher values make this sand type cover larger regions.")]
        [Range(0.05f, 4f)] public float regionWeight = 1f;

        [Tooltip("Height bias in metres. Positive sand types appear on ridges, negative in hollows.")]
        public float elevationBias;

        [Tooltip("Tint multiplied into the material for this sand type (1,1,1 = unmodified).")]
        public Color tint = Color.white;

        [Tooltip("Normal map strength override. 1 keeps the material default.")]
        [Range(0f, 2f)] public float normalStrength = 1f;

        /// <summary>Stable id used when streaming / debugging. Index inside the library.</summary>
        [System.NonSerialized] public int Index = -1;
    }

    /// <summary>
    /// Central registry of every sand variation the world can use.
    /// The procedural system supports all entries automatically: adding a new sand material to this
    /// list is the only step needed to make it appear in the generated wasteland.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SandLibrary",
        menuName = "Tanks/World/Sand Library",
        order = 1)]
    public class SandLibrary : ScriptableObject
    {
        [Tooltip("All sand variations the procedural world may use.")]
        [SerializeField] private List<SandType> sandTypes = new List<SandType>();

        [Tooltip("Fallback material used when a chunk has no sand type assigned yet.")]
        [SerializeField] private Material fallbackMaterial;

        [Tooltip("When the list is empty, load every sand material from the project automatically.")]
        [SerializeField] private bool autoPopulateFromProject = true;

        /// <summary>
        /// Folder searched at runtime when no library has been authored yet. Materials must live under
        /// a Resources folder for this to work (Unity only loads Resources-tracked assets at runtime).
        /// The editor setup (Tanks/Setup Procedural Wasteland Assets) is the normal path and uses
        /// AssetDatabase directly, so this is only a convenience fallback.
        /// </summary>
        public const string DefaultSandResourceFolder = "YughuesFreeSandMaterials/Materials";

        private Material[] cachedMaterials;

        public IReadOnlyList<SandType> SandTypes => sandTypes;
        public int Count => sandTypes.Count;
        public Material FallbackMaterial => fallbackMaterial;

        private void OnEnable()
        {
            if (autoPopulateFromProject && (sandTypes == null || sandTypes.Count == 0))
            {
                AutoPopulate();
            }

            RebuildIndices();
        }

        /// <summary>
        /// Loads every sand material shipped with the project so all available variations are used.
        /// Only runs when the list has not been authored, so an explicit setup always wins.
        /// </summary>
        public void AutoPopulate()
        {
            Material[] loaded = Resources.LoadAll<Material>(DefaultSandResourceFolder);
            if (loaded == null || loaded.Length == 0)
            {
                return;
            }

            var materials = new List<Material>(loaded);

            // Deterministic ordering so the world is identical across machines and sessions.
            materials.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            sandTypes = new List<SandType>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                sandTypes.Add(new SandType
                {
                    displayName = CleanName(materials[i].name),
                    material = materials[i],
                    regionWeight = DefaultRegionWeight(i),
                    normalStrength = 1f
                });
            }

            if (fallbackMaterial == null && sandTypes.Count > 0)
            {
                fallbackMaterial = sandTypes[0].material;
            }

            cachedMaterials = null;
        }

        /// <summary>Strips the asset pack prefix for readable debug output.</summary>
        private static string CleanName(string rawName)
        {
            return string.IsNullOrEmpty(rawName) ? "Sand" : rawName.Replace("M_YFSM_", string.Empty);
        }

        /// <summary>
        /// Slight weight variation so the region field produces a believable mix of large open areas
        /// and smaller local patches rather than evenly sized stripes.
        /// </summary>
        public static float DefaultRegionWeight(int index)
        {
            float[] weights = { 1.4f, 0.75f, 1.1f, 0.6f, 0.9f, 0.5f, 1.25f, 0.7f, 1.0f, 0.55f, 0.85f, 0.65f };
            return weights[index % weights.Length];
        }

        public SandType this[int index]
        {
            get
            {
                if (sandTypes == null || sandTypes.Count == 0)
                {
                    return null;
                }

                return sandTypes[Mathf.Clamp(index, 0, sandTypes.Count - 1)];
            }
        }

        /// <summary>Material array with stable indices, used to build chunk sub mesh renderers.</summary>
        public Material[] GetMaterialArray()
        {
            if (cachedMaterials != null && cachedMaterials.Length == sandTypes.Count)
            {
                return cachedMaterials;
            }

            cachedMaterials = new Material[Mathf.Max(1, sandTypes.Count)];
            for (int i = 0; i < sandTypes.Count; i++)
            {
                cachedMaterials[i] = sandTypes[i] != null ? sandTypes[i].material : fallbackMaterial;
            }

            if (cachedMaterials.Length > 0 && cachedMaterials[0] == null)
            {
                cachedMaterials[0] = fallbackMaterial;
            }

            return cachedMaterials;
        }

        /// <summary>
        /// True when the library has at least one usable entry.
        /// </summary>
        public bool IsValid()
        {
            if (sandTypes == null || sandTypes.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < sandTypes.Count; i++)
            {
                if (sandTypes[i] != null && sandTypes[i].material != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks that the assigned materials can actually be rendered by the active pipeline.
        ///
        /// URP cannot render the built-in Standard shader and draws such materials magenta. The sand
        /// asset pack ships separate HDRP and URP packages and its extracted folder contains the
        /// built-in variants, so this is a realistic failure to guard against. Returns the number of
        /// incompatible materials and logs a clear, actionable message when any are found.
        /// </summary>
        public int CountIncompatibleMaterials()
        {
            if (sandTypes == null)
            {
                return 0;
            }

            int incompatible = 0;
            var examples = new List<string>();

            for (int i = 0; i < sandTypes.Count; i++)
            {
                Material material = sandTypes[i] != null ? sandTypes[i].material : null;
                if (material == null || material.shader == null)
                {
                    continue;
                }

                string shaderName = material.shader.name;
                if (shaderName == "Standard" || shaderName.StartsWith("Standard ("))
                {
                    incompatible++;

                    if (examples.Count < 3)
                    {
                        examples.Add(material.name);
                    }
                }
            }

            if (incompatible > 0)
            {
                Debug.LogError(
                    $"[SandLibrary] {incompatible} sand material(s) still use the built-in 'Standard' shader " +
                    $"(for example: {string.Join(", ", examples)}). The active render pipeline cannot draw these, " +
                    "so the terrain will appear magenta. Run 'Tanks/Fix Sand Materials For URP' from the Unity menu.");
            }

            return incompatible;
        }

        /// <summary>Assigns stable indices. Called from the chunk manager on initialisation.</summary>
        public void RebuildIndices()
        {
            if (sandTypes == null)
            {
                sandTypes = new List<SandType>();
            }

            for (int i = 0; i < sandTypes.Count; i++)
            {
                if (sandTypes[i] != null)
                {
                    sandTypes[i].Index = i;
                }
            }

            cachedMaterials = null;
        }

        private void OnValidate()
        {
            cachedMaterials = null;

            if (autoPopulateFromProject && (sandTypes == null || sandTypes.Count == 0))
            {
                AutoPopulate();
            }

            RebuildIndices();
        }
    }
}
