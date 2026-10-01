using UnityEditor;
using UnityEngine;
using Tanks.World;

namespace Tanks.EditorTools
{
    /// <summary>
    /// Repairs WorldSettings assets that were saved before newer fields existed.
    ///
    /// Unity deserialises any field missing from the .asset file as 0 (or null). That is dangerous for
    /// procedural dials: a new amplitude arriving as 0 silently flattens terrain, and a new frequency
    /// arriving as 0 collapses a noise layer to a constant. This tool detects that case and restores
    /// sensible defaults, so adding a field never breaks an existing world.
    ///
    /// Run "Tanks/Repair World Settings Asset" after pulling changes that add new settings.
    /// </summary>
    public static class WorldSettingsRepair
    {
        private const string SettingsPath = "Assets/WastelandWorld/WorldSettings.asset";

        /// <summary>
        /// Fields that must never be 0, with the default they should fall back to.
        /// A zero here means the asset predates the field and Unity loaded it as 0.
        /// </summary>
        private static readonly (string Field, float Default)[] NonZeroFloats =
        {
            ("terrainReliefScale", 1f),
            ("terrainRegionScale", 0.00055f),
            ("terrainRegionWarp", 0.0032f),
            ("terrainRegionWarpStrength", 90f),
            ("openRegionShare", 0.45f),
            ("unevenRegionShare", 0.35f),
            ("massiveHillRegionShare", 0.2f),
            ("smallHillAmplitude", 0.2f),
            ("smallHillFrequency", 0.011f),
            ("unevenAmplitude", 0.62f),
            ("unevenFrequency", 0.062f),
            ("massiveHillMultiplier", 2.1f),
            ("massiveHillFrequency", 0.0011f),
            ("maxHillHeight", 7f),
            ("hillFrequency", 0.0028f),
            ("hillAmplitude", 1.35f),
            ("grainFrequency", 0.09f),
            ("depressionFrequency", 0.0022f),
            ("sandVariationScale", 0.0012f),
            ("sandRangeScale", 3.2f),
            ("chunkSize", 64f)
        };

        [MenuItem("Tanks/Repair World Settings Asset")]
        public static void Repair()
        {
            WorldSettings settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(SettingsPath);
            if (settings == null)
            {
                Debug.LogError($"[WorldSettingsRepair] No WorldSettings found at {SettingsPath}. " +
                               "Run 'Tanks/Setup Procedural Wasteland Scene' first.");
                return;
            }

            var serialized = new SerializedObject(settings);
            serialized.Update();

            int repaired = 0;

            for (int i = 0; i < NonZeroFloats.Length; i++)
            {
                string fieldName = NonZeroFloats[i].Field;
                float defaultValue = NonZeroFloats[i].Default;

                SerializedProperty property = serialized.FindProperty(fieldName);
                if (property == null)
                {
                    // Field genuinely does not exist on the class any more; skip it.
                    continue;
                }

                if (Mathf.Approximately(property.floatValue, 0f))
                {
                    Debug.Log($"[WorldSettingsRepair] {fieldName} was 0 (stale asset) -> restoring default {defaultValue}");
                    property.floatValue = defaultValue;
                    repaired++;
                }
            }

            // A missing mass of new fields is the signature of a pre-upgrade asset, so say so plainly.
            if (repaired > 0)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[WorldSettingsRepair] Restored {repaired} field(s) on {SettingsPath}. " +
                          "The world will change shape - that is expected, the terrain layers are active again.");
            }
            else
            {
                Debug.Log("[WorldSettingsRepair] WorldSettings looks complete; nothing to repair.");
            }
        }
    }
}
