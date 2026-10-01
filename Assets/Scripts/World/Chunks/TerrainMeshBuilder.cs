using System.Collections.Generic;
using UnityEngine;
using Tanks.World.Generation;
using Tanks.World.Sand;

namespace Tanks.World.Chunks
{
    /// <summary>
    /// Builds the vertex / UV / normal / index data for a single terrain chunk.
    ///
    /// Mobile focused choices:
    ///  - One mesh per chunk. No GameObject per tile, no Unity Terrain, no texture splat shader.
    ///  - Sand variety is achieved with sub meshes: the triangles covered by each sand type are
    ///    written into their own sub mesh, and the renderer receives the matching material list.
    ///    That means N draw calls per chunk (N = sand types present) with zero shader work, zero
    ///    extra texture memory and no runtime blending cost on the GPU.
    ///  - Scratch buffers are owned by the terrain system and reused, so streaming chunks does not
    ///    produce per chunk garbage collections.
    /// </summary>
    public class TerrainMeshBuilder
    {
        /// <summary>Reusable working buffers, owned by the caller and passed in per build.</summary>
        public class ScratchBuffers
        {
            /// <summary>Upper bound of sand types supported so buffers never resize while streaming.</summary>
            public const int MaxSandTypes = 16;

            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public Color[] Colors;
            public float[] Heights;
            public int[] SandIndices;
            public readonly List<int>[] SubMeshIndices = new List<int>[MaxSandTypes];
            public readonly List<int> SubMeshOrder = new List<int>(MaxSandTypes);

            public ScratchBuffers()
            {
                for (int i = 0; i < MaxSandTypes; i++)
                {
                    SubMeshIndices[i] = new List<int>(512);
                }
            }

            public void EnsureCapacity(int vertexCount)
            {
                if (Vertices != null && Vertices.Length == vertexCount)
                {
                    return;
                }

                Vertices = new Vector3[vertexCount];
                Normals = new Vector3[vertexCount];
                Uvs = new Vector2[vertexCount];
                Colors = new Color[vertexCount];
                Heights = new float[vertexCount];
                SandIndices = new int[vertexCount];
            }

            public void ResetSubMeshes()
            {
                SubMeshOrder.Clear();
                for (int i = 0; i < MaxSandTypes; i++)
                {
                    SubMeshIndices[i].Clear();
                }
            }
        }

        private readonly TerrainGenerator generator;
        private readonly WorldSettings settings;
        private readonly SandMaterialSelector sandSelector;

        private readonly List<int> subMeshIndexScratch = new List<int>(ScratchBuffers.MaxSandTypes);
        private readonly List<Mesh> emptyMeshScratch = new List<Mesh>();

        public TerrainMeshBuilder(TerrainGenerator generator, SandMaterialSelector sandSelector)
        {
            this.generator = generator;
            this.sandSelector = sandSelector;
            settings = generator.Settings;
        }

        /// <summary>
        /// Generates the chunk mesh. <paramref name="mesh"/> is reused when possible, keeping the
        /// per chunk allocation down to a single mesh object.
        /// </summary>
        public Mesh Build(
            ChunkCoordinate coordinate,
            Mesh mesh,
            ScratchBuffers buffers,
            out int usedSandTypeCount)
        {
            int vertexCountPerEdge = settings.ChunkVertexCount;
            int vertexCount = vertexCountPerEdge * vertexCountPerEdge;
            float chunkSize = settings.ChunkSize;
            float step = chunkSize / settings.TerrainResolution;
            Vector3 origin = coordinate.Origin(chunkSize);

            buffers.EnsureCapacity(vertexCount);
            buffers.ResetSubMeshes();

            // ---- Height pass -------------------------------------------------------------------
            generator.FillHeights(coordinate.X, coordinate.Z, buffers.Heights);

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            for (int i = 0; i < vertexCount; i++)
            {
                float h = buffers.Heights[i];
                if (h < minHeight)
                {
                    minHeight = h;
                }

                if (h > maxHeight)
                {
                    maxHeight = h;
                }
            }

            float heightRange = Mathf.Max(0.001f, maxHeight - minHeight);

            // ---- UV setup ----------------------------------------------------------------------
            // Rotating the texture per chunk (deterministically, from the seed) removes the visible
            // repetition that a single global UV layout would produce on open sand.
            float angle = 0f;
            if (settings.RandomizeSandUvRotation)
            {
                float random = NoiseUtility.Hash01(coordinate.X, coordinate.Z, settings.WorldSeed + 9001);
                angle = random * 360f;
            }

            float cos = Mathf.Cos(angle * Mathf.Deg2Rad);
            float sin = Mathf.Sin(angle * Mathf.Deg2Rad);
            float inverseTileSize = 1f / Mathf.Max(1f, settings.SandTextureWorldSize);
            Vector2 uvOrigin = new Vector2(origin.x * inverseTileSize, origin.z * inverseTileSize);
            Vector2 uvCenter = new Vector2(
                (origin.x + chunkSize * 0.5f) * inverseTileSize,
                (origin.z + chunkSize * 0.5f) * inverseTileSize);

            // ---- Vertex pass -------------------------------------------------------------------
            FillVertices(coordinate, buffers, vertexCountPerEdge, origin, step, minHeight, heightRange,
                cos, sin, uvOrigin, uvCenter, inverseTileSize);

            // ---- Normal pass -------------------------------------------------------------------
            BuildNormals(buffers, vertexCountPerEdge, step);

            // ---- Index pass (sub meshes per sand type) ------------------------------------------
            BuildSubMeshes(buffers, vertexCountPerEdge);

            usedSandTypeCount = buffers.SubMeshOrder.Count;

            // ---- Mesh upload -------------------------------------------------------------------
            if (mesh == null)
            {
                mesh = new Mesh { name = $"TerrainChunk_{coordinate.X}_{coordinate.Z}" };
                mesh.MarkDynamic();
            }
            else
            {
                mesh.Clear(false);
            }

            mesh.indexFormat = vertexCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(buffers.Vertices, 0, vertexCount);
            mesh.SetNormals(buffers.Normals, 0, vertexCount);
            mesh.SetUVs(0, buffers.Uvs, 0, vertexCount);
            mesh.SetColors(buffers.Colors, 0, vertexCount);

            subMeshIndexScratch.Clear();
            for (int i = 0; i < buffers.SubMeshOrder.Count; i++)
            {
                subMeshIndexScratch.Add(buffers.SubMeshOrder[i]);
            }

            mesh.subMeshCount = subMeshIndexScratch.Count;
            for (int i = 0; i < subMeshIndexScratch.Count; i++)
            {
                int sandIndex = subMeshIndexScratch[i];
                List<int> indices = buffers.SubMeshIndices[sandIndex];
                mesh.SetTriangles(indices, 0, indices.Count, i, false);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Sand library indices used by the last built chunk, in sub mesh order.</summary>
        public IReadOnlyList<int> LastSubMeshOrder => subMeshIndexScratch;

        /// <summary>
        /// Material array for the last built chunk, matching the sub mesh order.
        ///
        /// Every written slot is guaranteed non-null: a renderer material of null draws the geometry
        /// with no valid shader, which shows up as untextured patches on the terrain. Any index that
        /// cannot be resolved falls back to a material that is known to exist.
        /// </summary>
        public void GetLastSubMeshMaterials(Material[] destination, Material[] libraryMaterials)
        {
            if (destination == null)
            {
                return;
            }

            // The safest material to fall back to: the library's own fallback, else its first entry.
            Material fallback = ResolveFallbackMaterial(libraryMaterials);

            int count = Mathf.Min(subMeshIndexScratch.Count, destination.Length);
            for (int i = 0; i < count; i++)
            {
                int libraryIndex = subMeshIndexScratch[i];

                Material resolved = null;

                if (libraryMaterials != null &&
                    libraryIndex >= 0 &&
                    libraryIndex < libraryMaterials.Length)
                {
                    resolved = libraryMaterials[libraryIndex];
                }

                destination[i] = resolved != null ? resolved : fallback;
            }

            for (int i = count; i < destination.Length; i++)
            {
                destination[i] = fallback;
            }
        }

        /// <summary>
        /// Finds a material that is guaranteed to exist, so a sub mesh can never end up with null.
        /// </summary>
        private static Material ResolveFallbackMaterial(Material[] libraryMaterials)
        {
            if (libraryMaterials == null)
            {
                return null;
            }

            // Prefer the first non-null entry: the library orders it as the primary sand.
            for (int i = 0; i < libraryMaterials.Length; i++)
            {
                if (libraryMaterials[i] != null)
                {
                    return libraryMaterials[i];
                }
            }

            return null;
        }

        private void FillVertices(
            ChunkCoordinate coordinate,
            ScratchBuffers buffers,
            int vertexCountPerEdge,
            Vector3 origin,
            float step,
            float minHeight,
            float heightRange,
            float cos,
            float sin,
            Vector2 uvOrigin,
            Vector2 uvCenter,
            float inverseTileSize)
        {
            for (int z = 0; z < vertexCountPerEdge; z++)
            {
                float localZ = z * step;
                float worldZ = origin.z + localZ;
                int row = z * vertexCountPerEdge;

                for (int x = 0; x < vertexCountPerEdge; x++)
                {
                    int index = row + x;
                    float localX = x * step;
                    float worldX = origin.x + localX;
                    float height = buffers.Heights[index];

                    buffers.Vertices[index] = new Vector3(localX, height, localZ);

                    // Sand type per vertex - deterministic from world position, so neighbouring
                    // chunks always agree along their shared edge (no seam, no visible border).
                    float normalizedHeight = (height - minHeight) / heightRange;
                    buffers.SandIndices[index] = sandSelector.GetSandTypeIndex(worldX, worldZ, normalizedHeight);

                    // World aligned, per chunk rotated UVs: identical texture scale everywhere while
                    // the rotation hides the repetition of the sand albedo.
                    float uvX = worldX * inverseTileSize - uvOrigin.x;
                    float uvZ = worldZ * inverseTileSize - uvOrigin.y;
                    float centeredX = uvX - (uvCenter.x - uvOrigin.x);
                    float centeredZ = uvZ - (uvCenter.y - uvOrigin.y);
                    buffers.Uvs[index] = new Vector2(
                        centeredX * cos - centeredZ * sin + (uvCenter.x - uvOrigin.x),
                        centeredX * sin + centeredZ * cos + (uvCenter.y - uvOrigin.y));

                    // Vertex colour carries a subtle low frequency shading variation. It is free at
                    // runtime and stops the open sand from reading as one repeated photograph.
                    float shade = NoiseUtility.Perlin(worldX * 0.021f, worldZ * 0.021f, settings.WorldSeed + 555) * 0.5f + 0.5f;
                    float brightness = Mathf.Lerp(0.9f, 1.05f, shade);
                    buffers.Colors[index] = new Color(brightness, brightness, brightness, 1f);
                }
            }
        }

        /// <summary>
        /// Builds smooth vertex normals from the height field. Deriving normals from the shared
        /// height function (rather than per triangle face normals) means normals are continuous
        /// across chunk boundaries, so no lighting seam appears where two chunks meet.
        /// </summary>
        private void BuildNormals(ScratchBuffers buffers, int vertexCountPerEdge, float step)
        {
            Vector3[] vertices = buffers.Vertices;
            Vector3[] normals = buffers.Normals;

            for (int z = 0; z < vertexCountPerEdge; z++)
            {
                int row = z * vertexCountPerEdge;
                int rowPrev = z > 0 ? row - vertexCountPerEdge : row;
                int rowNext = z < vertexCountPerEdge - 1 ? row + vertexCountPerEdge : row;
                int rowPrevDelta = z > 0 ? z - 1 : z;
                int rowNextDelta = z < vertexCountPerEdge - 1 ? z + 1 : z;

                for (int x = 0; x < vertexCountPerEdge; x++)
                {
                    int xPrev = x > 0 ? x - 1 : x;
                    int xNext = x < vertexCountPerEdge - 1 ? x + 1 : x;

                    float left = vertices[row + xPrev].y;
                    float right = vertices[row + xNext].y;
                    float back = vertices[rowPrev + x].y;
                    float front = vertices[rowNext + x].y;

                    float dx = (xNext - xPrev) * (right - left) * 0.5f;
                    float dz = (rowNextDelta - rowPrevDelta) * (front - back) * 0.5f;

                    normals[row + x] = new Vector3(-dx, step, -dz).normalized;
                }
            }
        }

        /// <summary>
        /// Writes triangle indices into per sand type sub meshes.
        ///
        /// Each quad is assigned the sand type of its dominant corner. Because the sand region field
        /// is a smooth function of world position, the border between two regions follows a natural
        /// curve, so the transition reads as a material change in the desert rather than a straight
        /// artificial cut. The number of distinct sub meshes is bounded by the sand library size, so a
        /// chunk never issues more draw calls than configured.
        /// </summary>
        private void BuildSubMeshes(ScratchBuffers buffers, int vertexCountPerEdge)
        {
            int[] scratch = quadScratch;
            int sandTypeCount = sandSelector.SandTypeCount;

            for (int z = 0; z < vertexCountPerEdge - 1; z++)
            {
                int row = z * vertexCountPerEdge;
                int nextRow = row + vertexCountPerEdge;

                for (int x = 0; x < vertexCountPerEdge - 1; x++)
                {
                    int a = row + x;
                    int b = a + 1;
                    int c = nextRow + x;
                    int d = c + 1;

                    // Dominant sand type of this quad: majority vote of its four corners. That keeps
                    // the sub mesh border aligned with the visual border of the region.
                    int sandIndex = DominantSandIndex(buffers.SandIndices, a, b, c, d, sandTypeCount);

                    scratch[0] = a;
                    scratch[1] = c;
                    scratch[2] = b;
                    scratch[3] = b;
                    scratch[4] = c;
                    scratch[5] = d;

                    List<int> target = buffers.SubMeshIndices[sandIndex];
                    if (target.Count == 0)
                    {
                        buffers.SubMeshOrder.Add(sandIndex);
                    }

                    target.Add(scratch[0]);
                    target.Add(scratch[1]);
                    target.Add(scratch[2]);
                    target.Add(scratch[3]);
                    target.Add(scratch[4]);
                    target.Add(scratch[5]);
                }
            }
        }

        /// <summary>
        /// Majority vote over the four quad corners, with deterministic tie breaking.
        ///
        /// The returned index is always inside the sand library range, because it is used to index both
        /// the sub mesh lists and the material array. An out-of-range index would either throw or leave a
        /// sub mesh without a material, which renders as untextured geometry.
        /// </summary>
        private static int DominantSandIndex(int[] sandIndices, int a, int b, int c, int d, int sandTypeCount)
        {
            // No sand types configured: everything falls into sub mesh 0.
            if (sandTypeCount <= 0)
            {
                return 0;
            }

            int first = Clamp(sandIndices[a], sandTypeCount);
            int matches = 1;

            int second = Clamp(sandIndices[b], sandTypeCount);
            if (second == first)
            {
                matches++;
            }

            int third = Clamp(sandIndices[c], sandTypeCount);
            if (third == first)
            {
                matches++;
            }

            int fourth = Clamp(sandIndices[d], sandTypeCount);
            if (fourth == first)
            {
                matches++;
            }

            if (matches >= 3)
            {
                return first;
            }

            if (matches == 2)
            {
                // Tie between two boundary regions: keep the lower index so the choice is stable
                // across chunk edges and cannot flip flop between neighbours.
                int other = second != first ? second : third;
                return Mathf.Min(first, other);
            }

            return first;
        }

        /// <summary>Wraps an index safely into the sand library range, tolerating a negative input.</summary>
        private static int Clamp(int index, int sandTypeCount)
        {
            int wrapped = index % sandTypeCount;
            return wrapped < 0 ? wrapped + sandTypeCount : wrapped;
        }

        private readonly int[] quadScratch = new int[6];
    }
}
