using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Tanks.World;
using Tanks.World.Environment;
using Tanks.World.Sand;

namespace Tanks.EditorTools
{
    /// <summary>
    /// One click project bootstrap for the procedural wasteland prototype.
    ///
    /// It creates the WorldSettings + SandLibrary assets, registers every existing sand material from
    /// the project's sand asset pack, and adds a ProceduralWorld object to the open scene.
    ///
    /// Nothing here changes or replaces the imported sand art: the library simply references the
    /// materials that already ship in the project so the terrain uses the real assets.
    /// </summary>
    public static class WastelandPrototypeSetup
    {
        private const string WorldFolder = "Assets/WastelandWorld";
        private const string SettingsPath = WorldFolder + "/WorldSettings.asset";
        private const string SandLibraryPath = WorldFolder + "/SandLibrary.asset";
        private const string SandMaterialFolder = "Assets/YughuesFreeSandMaterials/Materials";

        [MenuItem("Tanks/Setup Procedural Wasteland Assets")]
        public static void CreateAssets()
        {
            EnsureFolder();

            WorldSettings settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<WorldSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            else
            {
                // An existing asset may predate fields added since it was saved. Unity loads those as 0,
                // which silently flattens the terrain, so repair before anything reads the values.
                WorldSettingsRepair.Repair();
            }

            SandLibrary library = AssetDatabase.LoadAssetAtPath<SandLibrary>(SandLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SandLibrary>();
                AssetDatabase.CreateAsset(library, SandLibraryPath);
            }

            PopulateSandLibrary(library);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[WastelandPrototypeSetup] Created / updated {SettingsPath} and {SandLibraryPath} " +
                      $"with {library.Count} sand types.");
        }

        /// <summary>
        /// Registers every material reference from a project folder into the sand library, in a
        /// deterministic order so the sand layout is identical across machines.
        /// </summary>
        private static List<Material> CollectSandMaterials()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { SandMaterialFolder });
            if (guids.Length == 0)
            {
                guids = AssetDatabase.FindAssets("t:Material M_YFSM");
            }

            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".mat"))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(System.StringComparer.Ordinal);

            var materials = new List<Material>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                if (material != null)
                {
                    materials.Add(material);
                }
            }

            return materials;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(WorldFolder))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(WorldFolder));
            }
        }

        /// <summary>
        /// Fills the library with every sand material found in the imported sand asset folder, so all
        /// available sand variations are used by the procedural world instead of only one.
        /// </summary>
        public static void PopulateSandLibrary(SandLibrary library)
        {
            if (library == null)
            {
                return;
            }

            List<Material> materials = CollectSandMaterials();

            var serialized = new SerializedObject(library);
            SerializedProperty list = serialized.FindProperty("sandTypes");
            SerializedProperty fallback = serialized.FindProperty("fallbackMaterial");

            list.ClearArray();

            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];

                list.InsertArrayElementAtIndex(i);
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("displayName").stringValue = material.name.Replace("M_YFSM_", string.Empty);
                element.FindPropertyRelative("material").objectReferenceValue = material;
                element.FindPropertyRelative("regionWeight").floatValue = SandLibrary.DefaultRegionWeight(i);
                element.FindPropertyRelative("elevationBias").floatValue = 0f;
                element.FindPropertyRelative("tint").colorValue = Color.white;
                element.FindPropertyRelative("normalStrength").floatValue = 1f;
            }

            if (fallback.objectReferenceValue == null && materials.Count > 0)
            {
                fallback.objectReferenceValue = materials[0];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
        }

        [MenuItem("Tanks/Setup Procedural Wasteland Scene")]
        public static void SetupScene()
        {
            // URP cannot render the built-in Standard shader, so convert the sand materials first:
            // otherwise the very first Play would show a magenta wasteland.
            SandMaterialUrpConverter.ConvertAll();

            CreateAssets();

            WorldSettings settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(SettingsPath);
            SandLibrary library = AssetDatabase.LoadAssetAtPath<SandLibrary>(SandLibraryPath);

            Scene scene = SceneManager.GetActiveScene();

            ProceduralWorld existing = Object.FindFirstObjectByType<ProceduralWorld>();
            if (existing == null)
            {
                GameObject worldObject = new GameObject("ProceduralWorld");
                existing = worldObject.AddComponent<ProceduralWorld>();
            }

            var serialized = new SerializedObject(existing);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.FindProperty("sandLibrary").objectReferenceValue = library;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // The fog controller must exist before Play so the atmosphere is authored in the scene.
            if (Object.FindFirstObjectByType<WastelandFogController>() == null)
            {
                GameObject fogObject = new GameObject("WastelandAtmosphere");
                WastelandFogController fog = fogObject.AddComponent<WastelandFogController>();
                var fogSerialized = new SerializedObject(fog);
                fogSerialized.FindProperty("settings").objectReferenceValue = settings;
                fogSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[WastelandPrototypeSetup] Scene configured. Press Play to explore the wasteland.");
        }

        /// <summary>
        /// Creates lightweight Resources proxies that point at the project's existing sand materials.
        ///
        /// The sand art is NOT moved, copied or replaced: only the material assets themselves are
        /// placed under Resources so the runtime sand library fallback can find them. The editor setup
        /// path does not need this, which is why it is a separate, optional step.
        /// </summary>
        [MenuItem("Tanks/Create Sand Resources Bridge")]
        public static void CreateSandResourcesBridge()
        {
            const string bridgeFolder = "Assets/Resources";
            const string materialFolder = bridgeFolder + "/" + SandLibrary.DefaultSandResourceFolder;

            if (!AssetDatabase.IsValidFolder(bridgeFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            EnsureFolderRecursive(materialFolder);

            List<Material> materials = CollectSandMaterials();
            int created = 0;

            for (int i = 0; i < materials.Count; i++)
            {
                Material source = materials[i];
                string destination = $"{materialFolder}/{source.name}.mat";

                if (AssetDatabase.LoadAssetAtPath<Material>(destination) != null)
                {
                    continue;
                }

                // CopyAsset duplicates the material asset, which is required because Resources cannot
                // reference assets outside its own tree at runtime.
                if (AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), destination))
                {
                    created++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[WastelandPrototypeSetup] Sand Resources bridge ready: {created} materials copied to {materialFolder}.");
        }

        /// <summary>Creates each missing folder segment under Assets.</summary>
        private static void EnsureFolderRecursive(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);

            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolderRecursive(parent);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
