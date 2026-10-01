using System.Collections.Generic;
using UnityEngine;
using Tanks.World.Chunks;
using Tanks.World.Generation;
using Tanks.World.Sand;

namespace Tanks.World
{
    /// <summary>
    /// Streams terrain chunks around a follow target.
    ///
    /// Responsibilities:
    ///  - decide which chunk coordinates should exist for the current view distance
    ///  - generate missing chunks (reusing pooled chunk objects and meshes)
    ///  - hide chunks that fall outside the view distance and release them to the pool
    ///  - expose terrain height queries for gameplay systems added later
    ///
    /// This class knows nothing about tanks, combat or networking: it only manages terrain. Later
    /// systems (props, wrecks, vegetation, points of interest) can subscribe to ChunkLoaded /
    /// ChunkUnloaded and attach their own content to a chunk.
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private WorldSettings settings;
        [SerializeField] private SandLibrary sandLibrary;

        [Header("Streaming")]
        [Tooltip("Object streamed by the terrain. Usually the player / tank transform.")]
        [SerializeField] private Transform followTarget;

        [Tooltip("If set, the manager creates its own fly camera when no follow target is assigned.")]
        [SerializeField] private bool createFallbackCameraWhenNoTarget = true;

        [Tooltip("Offset above the terrain surface applied to the fallback camera.")]
        [SerializeField] private float fallbackCameraHeight = 14f;

        [Tooltip("How far the follow target must move (metres) before the chunk set is re-evaluated.")]
        [SerializeField, Min(0f)] private float streamingUpdateThreshold = 4f;

        [Tooltip("Generate chunk meshes as colliders too, so tanks and physics interact with the sand.")]
        [SerializeField] private bool generateColliders = true;

        [Tooltip("Maximum newly generated chunks per frame. Prevents a long hitch when streaming fast.")]
        [SerializeField, Range(1, 64)] private int maxChunkBuildsPerFrame = 3;

        // Ensures the "not configured" error is reported at most once instead of every frame.
        private bool warnOnMissingConfiguration = true;

        private readonly Dictionary<ChunkCoordinate, TerrainChunk> activeChunks =
            new Dictionary<ChunkCoordinate, TerrainChunk>();

        private readonly List<ChunkCoordinate> coordinatesToRemove = new List<ChunkCoordinate>(64);
        private readonly List<ChunkCoordinate> coordinatesToAdd = new List<ChunkCoordinate>(64);
        private readonly Stack<TerrainChunk> chunkPool = new Stack<TerrainChunk>(32);
        private readonly Queue<ChunkCoordinate> buildQueue = new Queue<ChunkCoordinate>(64);

        private TerrainGenerator generator;
        private TerrainMeshBuilder meshBuilder;
        private TerrainMeshBuilder.ScratchBuffers scratch;
        private Material[] sandMaterials;
        private Material[] chunkMaterials;
        private Transform chunkRoot;
        private Camera fallbackCamera;
        private ChunkCoordinate currentPlayerChunk;
        private Vector3 lastStreamingPosition;
        private bool initialised;

        private int generatedChunkCount;

        /// <summary>Raised after a chunk finished generating. Later gameplay systems hook in here.</summary>
        public event System.Action<TerrainChunk> ChunkLoaded;

        /// <summary>Raised right before a chunk is released back to the pool.</summary>
        public event System.Action<TerrainChunk> ChunkUnloaded;

        public WorldSettings Settings => settings;
        public TerrainGenerator Generator => generator;
        public int LoadedChunkCount => activeChunks.Count;
        public int GeneratedChunkCount => generatedChunkCount;
        public int PooledChunkCount => chunkPool.Count;
        public ChunkCoordinate CurrentPlayerChunk => currentPlayerChunk;
        public IReadOnlyDictionary<ChunkCoordinate, TerrainChunk> ActiveChunks => activeChunks;

        /// <summary>
        /// Injects configuration before Initialise. Lets a bootstrap component (ProceduralWorld) wire
        /// the world up at runtime while still allowing a fully inspector driven setup.
        /// </summary>
        public void Configure(WorldSettings worldSettings, SandLibrary library)
        {
            bool settingsChanged = settings != worldSettings;
            settings = worldSettings;

            if (sandLibrary != library || settingsChanged)
            {
                sandLibrary = library;
                initialised = false;
                generator = null;
            }

            enabled = true;

            // Configure is the point at which the world becomes fully specified. When a bootstrap
            // component (ProceduralWorld) wires the manager up at runtime, AddComponent has already
            // run Awake with no configuration, so initialisation must be retried here.
            Initialise();

            // The follow target may already have been assigned before configuration arrived.
            if (followTarget != null && activeChunks.Count == 0)
            {
                lastStreamingPosition = followTarget.position;
                UpdateStreaming(true);
            }
        }

        /// <summary>Assigns the transform streamed by the terrain (the camera now, the tank later).</summary>
        public void SetFollowTarget(Transform target)
        {
            followTarget = target;

            if (target != null)
            {
                lastStreamingPosition = target.position;
                UpdateStreaming(true);
            }
        }

        /// <summary>
        /// Clears every loaded chunk and rebuilds from scratch with the current settings / seed.
        /// Used to verify determinism: the same seed must always produce the same terrain.
        /// </summary>
        public void RebuildAll()
        {
            foreach (KeyValuePair<ChunkCoordinate, TerrainChunk> pair in activeChunks)
            {
                if (pair.Value != null)
                {
                    ChunkUnloaded?.Invoke(pair.Value);
                    pair.Value.Release();
                    chunkPool.Push(pair.Value);
                }
            }

            activeChunks.Clear();
            buildQueue.Clear();
            generatedChunkCount = 0;

            initialised = false;
            generator = null;
            Initialise();

            if (followTarget != null)
            {
                lastStreamingPosition = followTarget.position;
            }

            UpdateStreaming(true);
        }

        private void Awake()
        {
            // An inspector-assigned manager is fully configured already, so it can initialise here.
            // A manager created at runtime by ProceduralWorld has no settings yet: Awake runs inside
            // AddComponent, before Configure is called, so it must stay quiet and wait for Configure.
            if (settings != null && sandLibrary != null)
            {
                Initialise();
            }
        }

        private void Start()
        {
            // Nothing to stream until a world has been configured.
            if (!initialised && generator == null)
            {
                Initialise();
            }

            if (!initialised || settings == null)
            {
                return;
            }

            if (followTarget == null && createFallbackCameraWhenNoTarget)
            {
                followTarget = CreateFallbackCamera();
            }

            if (followTarget != null)
            {
                lastStreamingPosition = followTarget.position;
                UpdateStreaming(true);
            }
        }

        private void Update()
        {
            if (!initialised)
            {
                Initialise();
            }

            if (!initialised || settings == null || followTarget == null)
            {
                return;
            }

            Vector3 position = followTarget.position;
            float deltaX = position.x - lastStreamingPosition.x;
            float deltaZ = position.z - lastStreamingPosition.z;
            ChunkCoordinate targetChunk = ChunkCoordinate.FromWorldXZ(position.x, position.z, settings.ChunkSize);

            bool crossedChunk = targetChunk != currentPlayerChunk;
            if (crossedChunk || deltaX * deltaX + deltaZ * deltaZ >= streamingUpdateThreshold * streamingUpdateThreshold)
            {
                lastStreamingPosition = position;
                UpdateStreaming(crossedChunk);
            }

            DrainBuildQueue();
        }

        /// <summary>
        /// Builds the generator / builder / material tables.
        /// Safe to call repeatedly: it is a no-op once initialised, and quietly returns when the world
        /// has not been configured yet so a late Configure can still succeed.
        /// </summary>
        public void Initialise()
        {
            if (initialised && generator != null)
            {
                return;
            }

            if (settings == null)
            {
                // Not necessarily an error: a runtime-created manager is configured right after
                // AddComponent. Only report a problem once the scene has had a chance to settle.
                if (warnOnMissingConfiguration)
                {
                    Debug.LogError(
                        "[ChunkManager] No WorldSettings assigned. Assign one in the inspector, or let " +
                        "ProceduralWorld.Configure drive it. Terrain cannot be generated.");
                    warnOnMissingConfiguration = false;
                }

                return;
            }

            if (sandLibrary == null)
            {
                if (warnOnMissingConfiguration)
                {
                    Debug.LogError(
                        "[ChunkManager] No SandLibrary assigned. Assign one in the inspector, or let " +
                        "ProceduralWorld.Configure drive it. Terrain cannot be generated.");
                    warnOnMissingConfiguration = false;
                }

                return;
            }

            sandLibrary.RebuildIndices();
            sandMaterials = sandLibrary.GetMaterialArray();
            chunkMaterials = new Material[TerrainMeshBuilder.ScratchBuffers.MaxSandTypes];

            // The sand assignment is what every downstream index and material lookup depends on.
            // Verify it up front so a broken library fails loudly here instead of producing chunks
            // with untextured sub meshes at runtime.
            if (sandMaterials == null || sandMaterials.Length == 0)
            {
                Debug.LogError(
                    "[ChunkManager] The SandLibrary produced no materials. Every terrain sub mesh would " +
                    "render untextured. Assign sand types in the SandLibrary asset.");
                enabled = false;
                return;
            }

            int nullMaterials = 0;
            for (int i = 0; i < sandMaterials.Length; i++)
            {
                if (sandMaterials[i] == null)
                {
                    nullMaterials++;
                }
            }

            if (nullMaterials > 0)
            {
                Debug.LogWarning(
                    $"[ChunkManager] {nullMaterials} of {sandMaterials.Length} sand library entries have no " +
                    "material. Those sand regions will fall back to another sand material. Assign them in the " +
                    "SandLibrary asset to give them their own look.");
            }

            var selector = new SandMaterialSelector(sandLibrary, settings);
            generator = new TerrainGenerator(settings, selector);
            meshBuilder = new TerrainMeshBuilder(generator, selector);
            scratch = new TerrainMeshBuilder.ScratchBuffers();

            chunkRoot = new GameObject("TerrainChunks").transform;
            chunkRoot.SetParent(transform, false);

            initialised = true;
        }

        /// <summary>
        /// Recomputes the desired chunk set for the current player chunk.
        /// 1. work out which chunks should exist
        /// 2. queue the missing ones for generation
        /// 3. release the ones that are now out of range (back to the pool, not destroyed)
        /// 4. apply the visibility change immediately so nothing stale is visible
        /// </summary>
        private void UpdateStreaming(bool immediatelyVisible)
        {
            ChunkCoordinate center = currentPlayerChunk;

            if (followTarget != null)
            {
                center = ChunkCoordinate.FromWorldXZ(
                    followTarget.position.x,
                    followTarget.position.z,
                    settings.ChunkSize);
            }

            bool chunkChanged = center != currentPlayerChunk;
            currentPlayerChunk = center;

            int viewDistance = settings.ViewDistanceInChunks;

            // ---- 1. Determine which chunks should now exist --------------------------------------
            coordinatesToRemove.Clear();
            coordinatesToAdd.Clear();
            buildQueue.Clear();

            foreach (KeyValuePair<ChunkCoordinate, TerrainChunk> pair in activeChunks)
            {
                int dx = Mathf.Abs(pair.Key.X - center.X);
                int dz = Mathf.Abs(pair.Key.Z - center.Z);
                bool inside = dx <= viewDistance && dz <= viewDistance;

                if (!inside)
                {
                    coordinatesToRemove.Add(pair.Key);
                }
            }

            for (int z = center.Z - viewDistance; z <= center.Z + viewDistance; z++)
            {
                for (int x = center.X - viewDistance; x <= center.X + viewDistance; x++)
                {
                    var coordinate = new ChunkCoordinate(x, z);
                    if (!activeChunks.ContainsKey(coordinate))
                    {
                        coordinatesToAdd.Add(coordinate);
                    }
                }
            }

            // ---- 2. Remove chunks outside the range ---------------------------------------------
            for (int i = 0; i < coordinatesToRemove.Count; i++)
            {
                ChunkCoordinate coordinate = coordinatesToRemove[i];
                if (!activeChunks.TryGetValue(coordinate, out TerrainChunk chunk))
                {
                    continue;
                }

                activeChunks.Remove(coordinate);
                ChunkUnloaded?.Invoke(chunk);

                if (settings.LogChunkEvents)
                {
                    Debug.Log($"[ChunkManager] Unloaded chunk {coordinate}. Active: {activeChunks.Count}");
                }

                chunk.Release();
                chunkPool.Push(chunk);
            }

            // ---- 3. Queue missing chunks, nearest first so the player's surroundings fill in first -
            coordinatesToAdd.Sort((a, b) =>
            {
                int da = Mathf.Abs(a.X - center.X) + Mathf.Abs(a.Z - center.Z);
                int db = Mathf.Abs(b.X - center.X) + Mathf.Abs(b.Z - center.Z);
                return da.CompareTo(db);
            });

            for (int i = 0; i < coordinatesToAdd.Count; i++)
            {
                buildQueue.Enqueue(coordinatesToAdd[i]);
            }
        }

        /// <summary>
        /// Generates queued chunks, respecting the per frame budget so streaming never stalls.
        /// Already visible chunks stay visible while new ones stream in, which is why the fog can
        /// hide the boundary without the player ever seeing a hole in the world.
        /// </summary>
        private void DrainBuildQueue()
        {
            int budget = Mathf.Max(1, maxChunkBuildsPerFrame);

            while (budget > 0 && buildQueue.Count > 0)
            {
                ChunkCoordinate coordinate = buildQueue.Dequeue();
                if (activeChunks.ContainsKey(coordinate))
                {
                    continue;
                }

                BuildChunk(coordinate);
                budget--;
            }
        }

        /// <summary>
        /// Generates one chunk: pulls a pooled chunk object, builds the mesh into its existing mesh
        /// object where possible, then assigns the sand materials that the chunk actually uses.
        /// </summary>
        private void BuildChunk(ChunkCoordinate coordinate)
        {
            TerrainChunk chunk = RentChunk(coordinate);

            Mesh mesh = meshBuilder.Build(coordinate, chunk.ReserveMesh(), scratch, out int sandTypeCount);

            if (sandTypeCount == 0)
            {
                // The whole chunk fell into a single sand region, or the library is empty. Fall back
                // to a single sub mesh so the chunk still renders correctly.
                sandTypeCount = 1;
            }

            meshBuilder.GetLastSubMeshMaterials(chunkMaterials, sandMaterials);

            int triangleCount = scratch.Vertices != null
                ? (settings.TerrainResolution * settings.TerrainResolution * 2)
                : 0;

            // Material array sized to the sub mesh count emitted by the builder. Every slot must end up
            // with a real material: a null renderer material draws untextured geometry.
            int subMeshCount = mesh != null ? mesh.subMeshCount : 0;
            Material[] materials = new Material[subMeshCount];
            Material fallback = sandMaterials != null && sandMaterials.Length > 0
                ? sandMaterials[0]
                : null;

            for (int i = 0; i < subMeshCount; i++)
            {
                Material candidate = i < chunkMaterials.Length ? chunkMaterials[i] : null;
                materials[i] = candidate != null ? candidate : fallback;
            }

            chunk.Apply(
                coordinate,
                mesh,
                materials,
                sandTypeCount,
                triangleCount,
                settings.ChunkSize,
                generateColliders);

            activeChunks.Add(coordinate, chunk);
            generatedChunkCount++;

            if (settings.LogChunkEvents)
            {
                Debug.Log($"[ChunkManager] Generated chunk {coordinate} with {sandTypeCount} sand types. Active: {activeChunks.Count}");
            }

            ChunkLoaded?.Invoke(chunk);
        }

        /// <summary>Reuses a released chunk when available, otherwise creates a new one.</summary>
        private TerrainChunk RentChunk(ChunkCoordinate coordinate)
        {
            while (chunkPool.Count > 0)
            {
                TerrainChunk pooled = chunkPool.Pop();
                if (pooled != null)
                {
                    pooled.name = $"Chunk_{coordinate.X}_{coordinate.Z}";
                    return pooled;
                }
            }

            var gameObject = new GameObject($"Chunk_{coordinate.X}_{coordinate.Z}");
            gameObject.transform.SetParent(chunkRoot, false);
            TerrainChunk created = gameObject.AddComponent<TerrainChunk>();
            created.EnsureComponents();
            return created;
        }

        /// <summary>
        /// Height of the wasteland surface at a world position.
        /// Works whether or not the containing chunk is currently loaded, because the height field is
        /// a pure function of the seed - useful for spawn logic, tank placement and AI later.
        /// </summary>
        public float SampleTerrainHeight(float worldX, float worldZ)
        {
            if (generator == null)
            {
                Initialise();
            }

            return generator != null ? generator.SampleHeight(worldX, worldZ) : 0f;
        }

        /// <summary>Height of the wasteland surface at a world position.</summary>
        public float SampleTerrainHeight(Vector3 worldPosition)
        {
            return SampleTerrainHeight(worldPosition.x, worldPosition.z);
        }

        /// <summary>Snaps a position onto the terrain surface plus an optional offset.</summary>
        public Vector3 SnapToTerrain(Vector3 worldPosition, float heightOffset = 0f)
        {
            worldPosition.y = SampleTerrainHeight(worldPosition.x, worldPosition.z) + heightOffset;
            return worldPosition;
        }

        /// <summary>
        /// Performs one streaming update immediately. Exposed so tooling and tests can drive the
        /// world deterministically without waiting for a frame.
        /// </summary>
        public void Tick()
        {
            if (!initialised)
            {
                Initialise();
            }

            if (!initialised || settings == null)
            {
                return;
            }

            Vector3 position = followTarget != null ? followTarget.position : lastStreamingPosition;
            ChunkCoordinate targetChunk = ChunkCoordinate.FromWorldXZ(position.x, position.z, settings.ChunkSize);

            if (targetChunk != currentPlayerChunk && followTarget != null)
            {
                lastStreamingPosition = position;
                UpdateStreaming(true);
            }

            DrainBuildQueue();
        }

        /// <summary>
        /// Flushes the whole pending build queue regardless of the per frame budget. Used by the
        /// verification tooling; gameplay never needs this because the budget protects the frame rate.
        /// </summary>
        public void ForceCompleteStreaming()
        {
            while (buildQueue.Count > 0)
            {
                ChunkCoordinate coordinate = buildQueue.Dequeue();
                if (!activeChunks.ContainsKey(coordinate))
                {
                    BuildChunk(coordinate);
                }
            }
        }

        /// <summary>
        /// Creates the temporary fly camera used to drive around the terrain.
        /// This exists only so the prototype can be played before the real tank exists; the manager
        /// is designed so the tank simply replaces the follow target later.
        /// </summary>
        public Transform CreateFallbackCamera()
        {
            if (fallbackCamera != null)
            {
                return fallbackCamera.transform;
            }

            GameObject cameraObject = GameObject.Find("Main Camera");
            if (cameraObject != null)
            {
                fallbackCamera = cameraObject.GetComponent<Camera>();
            }

            if (fallbackCamera == null)
            {
                cameraObject = new GameObject("WastelandFlyCamera");
                fallbackCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            fallbackCamera.transform.position = new Vector3(0f, fallbackCameraHeight, -30f);
            fallbackCamera.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            fallbackCamera.farClipPlane = Mathf.Max(
                fallbackCamera.farClipPlane,
                settings.FogEndDistance + 100f);

            return fallbackCamera.transform;
        }
    }
}
