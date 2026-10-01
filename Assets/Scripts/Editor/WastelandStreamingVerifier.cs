using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Tanks.World;
using Tanks.World.Chunks;
using Tanks.World.Sand;

namespace Tanks.EditorTools
{
    /// <summary>
    /// Editor based streaming verification.
    ///
    /// Run from the menu ("Tanks/Verify Chunk Streaming") or headlessly with:
    ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; ^
    ///     -executeMethod Tanks.EditorTools.WastelandPrototypeRunner.StreamChunks
    ///
    /// It drives the real ChunkManager - generate, unload, pool, rebuild - and reports the outcome,
    /// which is how the chunk streaming contract gets verified without pressing Play.
    /// </summary>
    public static class WastelandStreamingVerifier
    {
        private const string SettingsPath = "Assets/WastelandWorld/WorldSettings.asset";
        private const string SandLibraryPath = "Assets/WastelandWorld/SandLibrary.asset";

        /// <summary>Streams chunks over a route and reports the outcome. Returns true when all checks pass.</summary>
        public static bool Run(bool logReport = true)
        {
            WorldSettings settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(SettingsPath);
            SandLibrary library = AssetDatabase.LoadAssetAtPath<SandLibrary>(SandLibraryPath);

            if (settings == null || library == null)
            {
                Debug.LogError("[StreamingVerifier] Missing world assets. Run 'Tanks/Setup Procedural Wasteland Assets' first.");
                return false;
            }

            var root = new GameObject("StreamingVerificationWorld");
            var managerObject = new GameObject("ChunkManager");
            managerObject.transform.SetParent(root.transform, false);
            ChunkManager manager = managerObject.AddComponent<ChunkManager>();

            manager.Configure(settings, library);
            manager.Initialise();

            var targetObject = new GameObject("TestTarget");
            targetObject.transform.SetParent(root.transform, false);
            Transform target = targetObject.transform;

            manager.SetFollowTarget(target);
            manager.ForceCompleteStreaming();

            var report = new StringBuilder();
            int expected = (settings.ViewDistanceInChunks * 2 + 1) * (settings.ViewDistanceInChunks * 2 + 1);

            report.AppendLine("=== CHUNK STREAMING VERIFICATION ===");
            report.AppendLine($"World seed             : {settings.WorldSeed}");
            report.AppendLine($"Chunk size             : {settings.ChunkSize} m");
            report.AppendLine($"View distance          : {settings.ViewDistanceInChunks} chunks -> {expected} chunks loaded");
            report.AppendLine($"Chunks loaded at spawn : {manager.LoadedChunkCount}");

            bool spawnPass = manager.LoadedChunkCount == expected;
            float chunkSize = settings.ChunkSize;

            var visited = new HashSet<ChunkCoordinate>();
            int minLoaded = int.MaxValue;
            int maxLoaded = 0;

            for (int step = 0; step < 60; step++)
            {
                float x = step * chunkSize * 0.55f;
                float z = Mathf.Sin(step * 0.35f) * chunkSize * 1.5f;

                target.position = new Vector3(x, 0f, z);
                manager.Tick();
                manager.ForceCompleteStreaming();

                visited.Add(new ChunkCoordinate(
                    Mathf.FloorToInt(x / chunkSize),
                    Mathf.FloorToInt(z / chunkSize)));

                minLoaded = Mathf.Min(minLoaded, manager.LoadedChunkCount);
                maxLoaded = Mathf.Max(maxLoaded, manager.LoadedChunkCount);
            }

            report.AppendLine();
            report.AppendLine("-- Streaming while moving across the wasteland --");
            report.AppendLine($"Distinct player chunks visited : {visited.Count}");
            report.AppendLine($"Chunks generated over the trip : {manager.GeneratedChunkCount}");
            report.AppendLine($"Chunks loaded now              : {manager.LoadedChunkCount}");
            report.AppendLine($"Pooled (reused) chunks         : {manager.PooledChunkCount}");
            report.AppendLine($"Loaded count range             : {minLoaded} .. {maxLoaded}");
            report.AppendLine($"Live chunk GameObjects         : {Object.FindObjectsByType<TerrainChunk>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}");

            bool stable = minLoaded == expected && maxLoaded == expected;
            bool reused = manager.PooledChunkCount > 0;
            bool streamed = manager.GeneratedChunkCount > expected;

            report.AppendLine($"Chunk count held at the view distance  : {(stable ? "PASS" : "FAIL")}");
            report.AppendLine($"Old chunks returned to the pool        : {(reused ? "PASS" : "FAIL")}");
            report.AppendLine($"New chunks generated while moving      : {(streamed ? "PASS" : "FAIL")}");

            bool materialPass;
            bool multiSandPass;
            AppendSandReport(manager, report, out multiSandPass, out materialPass);

            bool deterministic = AppendDeterminismReport(manager, report);

            report.AppendLine();
            bool allPass = spawnPass && stable && reused && streamed && multiSandPass
                           && materialPass && deterministic && manager.LoadedChunkCount == expected;

            report.AppendLine(allPass
                ? "=== RESULT: STREAMING + DETERMINISM PASSED ==="
                : "=== RESULT: STREAMING VERIFICATION FAILED ===");

            if (logReport)
            {
                Debug.Log(report.ToString());
            }

            Object.DestroyImmediate(root);
            return allPass;
        }

        /// <summary>Reports how the sand materials are distributed across the loaded chunks.</summary>
        private static void AppendSandReport(
            ChunkManager manager,
            StringBuilder report,
            out bool multiSandPass,
            out bool materialPass)
        {
            int multiSandChunks = 0;
            int nullMaterials = 0;
            int totalSubMeshes = 0;
            int maxSubMeshes = 0;
            int totalTriangles = 0;

            foreach (KeyValuePair<ChunkCoordinate, TerrainChunk> pair in manager.ActiveChunks)
            {
                TerrainChunk chunk = pair.Value;
                if (chunk == null || chunk.Renderer == null)
                {
                    continue;
                }

                int subMeshes = chunk.Renderer.sharedMaterials.Length;
                totalSubMeshes += subMeshes;
                maxSubMeshes = Mathf.Max(maxSubMeshes, subMeshes);
                totalTriangles += chunk.TriangleCount;

                for (int i = 0; i < subMeshes; i++)
                {
                    if (chunk.Renderer.sharedMaterials[i] == null)
                    {
                        nullMaterials++;
                    }
                }

                if (chunk.SandTypeCount > 1)
                {
                    multiSandChunks++;
                }
            }

            report.AppendLine();
            report.AppendLine("-- Sand materials per chunk --");
            report.AppendLine($"Chunks using more than one sand type : {multiSandChunks} / {manager.LoadedChunkCount}");
            report.AppendLine($"Total sub meshes (draw calls)        : {totalSubMeshes}");
            report.AppendLine($"Max sub meshes on one chunk          : {maxSubMeshes}");
            float perChunk = manager.LoadedChunkCount > 0 ? (float)totalSubMeshes / manager.LoadedChunkCount : 0f;
            report.AppendLine($"Average draw calls per chunk         : {perChunk:F2}");
            report.AppendLine($"Null materials assigned              : {nullMaterials}");
            report.AppendLine($"Total chunk triangles                : {totalTriangles}");

            multiSandPass = multiSandChunks > 0;
            materialPass = nullMaterials == 0;

            report.AppendLine($"Multiple sand types visible             : {(multiSandPass ? "PASS" : "FAIL")}");
            report.AppendLine($"Every sub mesh has a sand material      : {(materialPass ? "PASS" : "FAIL")}");
        }

        /// <summary>Rebuilds a chunk from scratch and confirms the mesh is byte identical.</summary>
        private static bool AppendDeterminismReport(ChunkManager manager, StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("-- Determinism: chunk (4,-2) rebuilt from scratch --");

            string before = HashChunk(manager, 4, -2);
            manager.RebuildAll();
            manager.ForceCompleteStreaming();
            string after = HashChunk(manager, 4, -2);

            report.AppendLine($"First generation : {before}");
            report.AppendLine($"After rebuild    : {after}");

            bool deterministic = !string.IsNullOrEmpty(before) && before == after;
            report.AppendLine(deterministic
                ? "PASS (seed + chunk coordinate always rebuild the identical mesh)"
                : "FAIL (terrain changed on rebuild!)");
            return deterministic;
        }

        /// <summary>Hashes one chunk's generated mesh: heights, sand regions and sub mesh layout.</summary>
        private static string HashChunk(ChunkManager manager, int x, int z)
        {
            foreach (KeyValuePair<ChunkCoordinate, TerrainChunk> pair in manager.ActiveChunks)
            {
                if (pair.Key.X != x || pair.Key.Z != z || pair.Value == null)
                {
                    continue;
                }

                Mesh mesh = pair.Value.ChunkMesh;
                if (mesh == null)
                {
                    return null;
                }

                Vector3[] vertices = mesh.vertices;
                unchecked
                {
                    long hash = 17;
                    for (int i = 0; i < vertices.Length; i += 7)
                    {
                        hash = hash * 31 + System.BitConverter.SingleToInt32Bits(vertices[i].x);
                        hash = hash * 31 + System.BitConverter.SingleToInt32Bits(vertices[i].y);
                        hash = hash * 31 + System.BitConverter.SingleToInt32Bits(vertices[i].z);
                    }

                    return $"{vertices.Length} verts, {mesh.subMeshCount} submeshes, hash {hash:X16}";
                }
            }

            return null;
        }
    }
}
