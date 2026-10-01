using UnityEngine;
using UnityEngine.Rendering;

namespace Tanks.World.Chunks
{
    /// <summary>
    /// Runtime representation of one loaded slice of the wasteland.
    ///
    /// A chunk owns exactly one GameObject, one MeshFilter, one MeshRenderer and one MeshCollider,
    /// which keeps the scene hierarchy and draw call bookkeeping tiny for a large streaming world.
    /// Chunks are pooled / reused by <see cref="ChunkManager"/> instead of being destroyed, so
    /// driving around does not constantly allocate meshes and GameObjects.
    /// </summary>
    [DisallowMultipleComponent]
    public class TerrainChunk : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        /// <summary>Coordinate this chunk currently represents.</summary>
        public ChunkCoordinate Coordinate { get; private set; }

        /// <summary>True once the chunk has geometry and is visible.</summary>
        public bool IsGenerated { get; private set; }

        /// <summary>Number of sand types present in the chunk's most recent build.</summary>
        public int SandTypeCount { get; private set; }

        /// <summary>Approximate triangle count, useful for the debug overlay.</summary>
        public int TriangleCount { get; private set; }

        /// <summary>Mesh owned by this chunk. Reused between generations.</summary>
        public Mesh ChunkMesh { get; private set; }

        public MeshRenderer Renderer => meshRenderer;
        public MeshCollider Collider => meshCollider;

        private void Awake()
        {
            EnsureComponents();
        }

        /// <summary>Creates the (few) components a chunk needs. Called once, on first use.</summary>
        public void EnsureComponents()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    meshFilter = gameObject.AddComponent<MeshFilter>();
                }
            }

            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                {
                    meshRenderer = gameObject.AddComponent<MeshRenderer>();
                }
            }

            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = gameObject.AddComponent<MeshCollider>();
                }
            }
        }

        /// <summary>
        /// Called before the chunk is handed to the mesh builder, so the builder can reuse the mesh.
        /// </summary>
        public Mesh ReserveMesh()
        {
            EnsureComponents();
            ChunkMesh = meshFilter.sharedMesh;
            return ChunkMesh;
        }

        /// <summary>
        /// Applies freshly built geometry data to this chunk.
        /// <paramref name="materials"/> must match the sub mesh order produced by the mesh builder.
        /// </summary>
        public void Apply(
            ChunkCoordinate coordinate,
            Mesh mesh,
            Material[] materials,
            int sandTypeCount,
            int triangleCount,
            float chunkSize,
            bool enableCollider)
        {
            EnsureComponents();

            Coordinate = coordinate;
            ChunkMesh = mesh;
            SandTypeCount = sandTypeCount;
            TriangleCount = triangleCount;

            transform.position = coordinate.Origin(chunkSize);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            meshFilter.sharedMesh = mesh;

            // Material array must line up with the sub mesh count. Every slot is filled with a real
            // material: a null renderer material draws the geometry untextured, which shows up as
            // patches of terrain with no sand texture.
            int subMeshCount = mesh != null ? mesh.subMeshCount : 0;
            if (subMeshCount > 0)
            {
                Material firstValid = FindFirstValid(materials);

                if (materials == null || materials.Length != subMeshCount)
                {
                    var resized = new Material[subMeshCount];
                    for (int i = 0; i < subMeshCount; i++)
                    {
                        Material candidate = materials != null && i < materials.Length ? materials[i] : null;
                        resized[i] = candidate != null ? candidate : firstValid;
                    }

                    meshRenderer.sharedMaterials = resized;
                }
                else
                {
                    // Right length, but individual slots may still be empty.
                    bool hasGap = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null)
                        {
                            hasGap = true;
                            break;
                        }
                    }

                    if (hasGap && firstValid != null)
                    {
                        var patched = new Material[materials.Length];
                        for (int i = 0; i < materials.Length; i++)
                        {
                            patched[i] = materials[i] != null ? materials[i] : firstValid;
                        }

                        meshRenderer.sharedMaterials = patched;
                    }
                    else
                    {
                        meshRenderer.sharedMaterials = materials;
                    }
                }
            }

            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.enabled = true;

            if (enableCollider)
            {
                meshCollider.enabled = false;
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
                meshCollider.enabled = true;
            }
            else
            {
                meshCollider.sharedMesh = null;
                meshCollider.enabled = false;
            }

            IsGenerated = true;
        }

        /// <summary>
        /// Returns the first non-null material in the array, used as a last-resort fallback so a
        /// sub mesh can never end up with a null material (which renders untextured).
        /// </summary>
        private static Material FindFirstValid(Material[] materials)
        {
            if (materials == null)
            {
                return null;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    return materials[i];
                }
            }

            return null;
        }

        /// <summary>Shows or hides the chunk geometry without unloading it.</summary>
        public void SetVisible(bool visible)
        {
            EnsureComponents();

            meshRenderer.enabled = visible;
            if (meshCollider != null)
            {
                meshCollider.enabled = visible && meshCollider.sharedMesh != null;
            }
        }

        /// <summary>Releases the chunk so it can be re-used for a different coordinate.</summary>
        public void Release()
        {
            IsGenerated = false;
            SandTypeCount = 0;
            TriangleCount = 0;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            // Only destroy the mesh when this chunk itself is destroyed, never while pooled.
            if (ChunkMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(ChunkMesh);
            }
            else
            {
                DestroyImmediate(ChunkMesh);
            }

            ChunkMesh = null;
        }
    }
}
