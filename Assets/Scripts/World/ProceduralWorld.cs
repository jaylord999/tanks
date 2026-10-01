using UnityEngine;
using Tanks.Debugging;
using Tanks.World.Environment;
using Tanks.World.Sand;

namespace Tanks.World
{
    /// <summary>
    /// Top level entry point for the procedural wasteland.
    ///
    /// ProceduralWorld
    ///   -> ChunkManager      (streaming: which chunks exist, generate, pool, release)
    ///        -> ChunkCoordinate / TerrainChunk
    ///        -> TerrainGenerator  (deterministic heights)
    ///        -> TerrainMeshBuilder -> SandMaterialSelector -> SandLibrary
    ///   -> WastelandFogController  (atmosphere / hiding the streaming boundary)
    ///   -> TerrainFlyCamera        (temporary development camera)
    ///   -> TerrainDebugOverlay     (temporary diagnostics)
    ///
    /// Only terrain lives here. Tanks, combat, multiplayer, AI, loot and points of interest will be
    /// added as separate systems that hook into ChunkManager.ChunkLoaded / ChunkUnloaded.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProceduralWorld : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private WorldSettings settings;
        [SerializeField] private SandLibrary sandLibrary;

        [Header("Systems")]
        [Tooltip("Create the streaming chunk manager automatically when the scene has none.")]
        [SerializeField] private bool createChunkManager = true;

        [Tooltip("Create the atmospheric fog controller automatically when the scene has none.")]
        [SerializeField] private bool createFogController = true;

        [Tooltip("Create the temporary development camera when the scene has none.")]
        [SerializeField] private bool createDevelopmentCamera = true;

        [Tooltip("Create the temporary debug overlay when the scene has none.")]
        [SerializeField] private bool createDebugOverlay = true;

        [Header("Development Camera")]
        [SerializeField] private float developmentCameraHeight = 8f;
        [SerializeField] private float developmentCameraSpeed = 45f;

        private ChunkManager chunkManager;
        private WastelandFogController fogController;
        private TerrainFlyCamera flyCamera;
        private TerrainDebugOverlay debugOverlay;

        public ChunkManager ChunkManager => chunkManager;
        public WorldSettings Settings => settings;
        public SandLibrary SandLibrary => sandLibrary;

        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogError("[ProceduralWorld] No WorldSettings assigned. Assign one in the inspector.");
                enabled = false;
                return;
            }

            if (sandLibrary == null)
            {
                // Nothing authored yet: build a runtime library from the sand materials already in
                // the project so the wasteland renders with the real art instead of failing.
                sandLibrary = ScriptableObject.CreateInstance<SandLibrary>();
                sandLibrary.name = "SandLibrary (runtime)";
                sandLibrary.AutoPopulate();

                if (!sandLibrary.IsValid())
                {
                    Debug.LogError(
                        "[ProceduralWorld] No SandLibrary assigned and no sand materials were found under " +
                        $"Resources/{SandLibrary.DefaultSandResourceFolder}. " +
                        "Run 'Tanks/Setup Procedural Wasteland Assets' or assign a SandLibrary in the inspector.");
                    enabled = false;
                    return;
                }

                Debug.Log($"[ProceduralWorld] Built a runtime sand library with {sandLibrary.Count} sand types " +
                          "from the project materials. Create the asset via 'Tanks/Setup Procedural Wasteland Assets' " +
                          "to edit it in the inspector.");
            }

            EnsureChunkManager();
            EnsureFogController();
            EnsureDevelopmentCamera();
            EnsureDebugOverlay();

            // Surface a pipeline/material mismatch immediately, with the fix in the message, rather
            // than leaving a magenta terrain to be diagnosed from the render output.
            sandLibrary.CountIncompatibleMaterials();

            // Surface procedural settings that are silently disabled, which would make the terrain
            // flatter than intended (for example an asset saved before a field was added).
            settings.ValidateProceduralSettings();

            // Separately warn if the tuning would create terrain too steep to drive. This is a
            // deliberate authoring choice rather than a broken setting, so it is reported as a warning.
            settings.WarnIfTooSteep();
        }

        /// <summary>Finds or creates the streaming chunk manager.</summary>
        private void EnsureChunkManager()
        {
            chunkManager = FindFirstObjectByType<ChunkManager>();

            if (chunkManager == null)
            {
                if (!createChunkManager)
                {
                    Debug.LogError("[ProceduralWorld] No ChunkManager in the scene and creation is disabled.");
                    return;
                }

                GameObject managerObject = new GameObject("ChunkManager");
                managerObject.transform.SetParent(transform, false);
                chunkManager = managerObject.AddComponent<ChunkManager>();
            }

            chunkManager.Configure(settings, sandLibrary);
            chunkManager.Initialise();
        }

        /// <summary>Finds or creates the atmosphere controller.</summary>
        private void EnsureFogController()
        {
            fogController = FindFirstObjectByType<WastelandFogController>();

            if (fogController == null)
            {
                if (!createFogController)
                {
                    return;
                }

                GameObject fogObject = new GameObject("WastelandAtmosphere");
                fogObject.transform.SetParent(transform, false);
                fogController = fogObject.AddComponent<WastelandFogController>();
            }

            fogController.SetSettings(settings);
        }

        /// <summary>
        /// Finds or creates the temporary development camera. The camera becomes the streamed
        /// follow target, which is exactly the slot the real tank will occupy later.
        /// </summary>
        private void EnsureDevelopmentCamera()
        {
            if (!createDevelopmentCamera)
            {
                return;
            }

            Transform cameraTransform = Camera.main != null ? Camera.main.transform : null;

            if (cameraTransform == null)
            {
                GameObject cameraObject = new GameObject("DevelopmentCamera");
                cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                cameraObject.tag = "MainCamera";
                cameraTransform = cameraObject.transform;
            }

            flyCamera = cameraTransform.GetComponent<TerrainFlyCamera>();
            if (flyCamera == null)
            {
                flyCamera = cameraTransform.gameObject.AddComponent<TerrainFlyCamera>();
            }

            flyCamera.SetChunkManager(chunkManager);
            flyCamera.Configure(developmentCameraHeight, developmentCameraSpeed);

            // Place the camera on the terrain surface so the very first frame already shows sand.
            Vector3 position = new Vector3(0f, 0f, -30f);
            position.y = chunkManager.SampleTerrainHeight(position.x, position.z) + developmentCameraHeight;
            cameraTransform.position = position;

            chunkManager.SetFollowTarget(cameraTransform);
        }

        /// <summary>Finds or creates the temporary debug overlay.</summary>
        private void EnsureDebugOverlay()
        {
            debugOverlay = FindFirstObjectByType<TerrainDebugOverlay>();

            if (debugOverlay == null)
            {
                if (!createDebugOverlay || !settings.ShowDebugOverlay)
                {
                    return;
                }

                GameObject overlayObject = new GameObject("TerrainDebugOverlay");
                overlayObject.transform.SetParent(transform, false);
                debugOverlay = overlayObject.AddComponent<TerrainDebugOverlay>();
            }

            debugOverlay.SetChunkManager(chunkManager);
            debugOverlay.SetVisible(settings.ShowDebugOverlay);
        }

        /// <summary>
        /// Rebuilds the terrain with the current settings. Used to verify determinism during
        /// development: the same seed must always produce the same world.
        /// </summary>
        public void RebuildWorld()
        {
            if (chunkManager == null)
            {
                return;
            }

            chunkManager.RebuildAll();
            fogController?.SetSettings(settings);
        }
    }
}
