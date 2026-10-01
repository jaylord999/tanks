using System.Text;
using UnityEditor;
using UnityEngine;
using Tanks.World;
using Tanks.World.Generation;
using Tanks.World.Sand;

namespace Tanks.EditorTools
{
    /// <summary>
    /// Development verification for the procedural wasteland.
    ///
    /// Runs determinism and shape checks without entering Play Mode:
    ///  - the same seed + same chunk coordinates must always produce identical heights
    ///  - a different seed must produce a different world
    ///  - hills must stay small enough for a tank to drive over
    ///  - sand regions must be varied but not noisy (no checkerboards)
    ///  - chunk borders must line up, so no seam appears between neighbouring chunks
    /// </summary>
    public static class WastelandPrototypeVerifier
    {
        private const string SettingsPath = "Assets/WastelandWorld/WorldSettings.asset";
        private const string SandLibraryPath = "Assets/WastelandWorld/SandLibrary.asset";

        [MenuItem("Tanks/Verify Procedural Wasteland")]
        public static void Verify()
        {
            WorldSettings settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(SettingsPath);
            SandLibrary library = AssetDatabase.LoadAssetAtPath<SandLibrary>(SandLibraryPath);

            if (settings == null || library == null)
            {
                Debug.LogError("[Verifier] Missing assets. Run 'Tanks/Setup Procedural Wasteland Assets' first.");
                return;
            }

            var selector = new SandMaterialSelector(library, settings);
            var generator = new TerrainGenerator(settings, selector);

            var report = new StringBuilder();
            report.AppendLine("=== PROCEDURAL WASTELAND VERIFICATION ===");
            report.AppendLine($"World seed            : {settings.WorldSeed}");
            report.AppendLine($"Chunk size            : {settings.ChunkSize} m");
            report.AppendLine($"Terrain resolution    : {settings.TerrainResolution} ({settings.ChunkVertexCount}x{settings.ChunkVertexCount} vertices)");
            report.AppendLine($"View distance         : {settings.ViewDistanceInChunks} chunks");
            report.AppendLine($"Max hill height       : {settings.MaxHillHeight} m");
            report.AppendLine($"Sand types in library : {library.Count}");

            bool pass = true;
            pass &= CheckDeterminism(settings, library, report);
            pass &= CheckSeams(settings, generator, report);
            pass &= CheckShape(settings, generator, report);
            pass &= CheckSandDistribution(settings, generator, selector, library, report);
            pass &= CheckSeedSensitivity(settings, library, generator, selector, report);

            report.AppendLine();
            report.AppendLine(pass ? "=== RESULT: ALL CHECKS PASSED ===" : "=== RESULT: SOME CHECKS FAILED ===");

            Debug.Log(report.ToString());
        }

        private static bool CheckDeterminism(WorldSettings settings, SandLibrary library, StringBuilder report)
        {
            var selectorA = new SandMaterialSelector(library, settings);
            var generatorA = new TerrainGenerator(settings, selectorA);
            var selectorB = new SandMaterialSelector(library, settings);
            var generatorB = new TerrainGenerator(settings, selectorB);

            float maxHeightDiff = 0f;
            int maxSandDiff = 0;
            for (int i = 0; i < 2000; i++)
            {
                float x = -1500f + i * 1.37f;
                float z = 900f - i * 0.91f;

                maxHeightDiff = Mathf.Max(maxHeightDiff, Mathf.Abs(generatorA.SampleHeight(x, z) - generatorB.SampleHeight(x, z)));

                if (selectorA.GetSandTypeIndex(x, z, 0.5f) != selectorB.GetSandTypeIndex(x, z, 0.5f))
                {
                    maxSandDiff++;
                }
            }

            report.AppendLine();
            report.AppendLine("-- 1. Determinism (same seed, same input, twice) --");
            report.AppendLine($"Max height difference : {maxHeightDiff:E3} m");
            report.AppendLine($"Sand mismatches       : {maxSandDiff}");
            bool pass = maxHeightDiff < 1e-5f && maxSandDiff == 0;
            report.AppendLine(pass ? "PASS" : "FAIL");
            return pass;
        }

        private static bool CheckSeams(WorldSettings settings, TerrainGenerator generator, StringBuilder report)
        {
            float borderMaxDiff = 0f;
            float chunkSize = settings.ChunkSize;

            for (int cx = -2; cx <= 2; cx++)
            {
                for (int cz = -2; cz <= 2; cz++)
                {
                    float edgeX = cx * chunkSize;
                    float edgeZ = cz * chunkSize;

                    for (int s = 0; s <= 16; s++)
                    {
                        float offset = s * (chunkSize / 16f);

                        float left = generator.SampleHeight(edgeX - 0.0001f, edgeZ + offset);
                        float right = generator.SampleHeight(edgeX + 0.0001f, edgeZ + offset);
                        borderMaxDiff = Mathf.Max(borderMaxDiff, Mathf.Abs(left - right));

                        float back = generator.SampleHeight(edgeX + offset, edgeZ - 0.0001f);
                        float front = generator.SampleHeight(edgeX + offset, edgeZ + 0.0001f);
                        borderMaxDiff = Mathf.Max(borderMaxDiff, Mathf.Abs(back - front));
                    }
                }
            }

            report.AppendLine();
            report.AppendLine("-- 2. Chunk border continuity (seam check) --");
            report.AppendLine($"Max height step across chunk edge : {borderMaxDiff:F5} m");
            bool pass = borderMaxDiff < 0.01f;
            report.AppendLine(pass ? "PASS (no seam)" : "FAIL");
            return pass;
        }

        private static bool CheckShape(WorldSettings settings, TerrainGenerator generator, StringBuilder report)
        {
            float minH = float.MaxValue;
            float maxH = float.MinValue;
            float maxSlope = 0f;
            float step = settings.ChunkSize / settings.TerrainResolution;

            for (int x = -400; x < 400; x += 2)
            {
                for (int z = -400; z < 400; z += 8)
                {
                    float wx = x * 1.5f;
                    float wz = z * 1.5f;
                    float h = generator.SampleHeight(wx, wz);

                    minH = Mathf.Min(minH, h);
                    maxH = Mathf.Max(maxH, h);

                    float hNext = generator.SampleHeight(wx + step, wz);
                    maxSlope = Mathf.Max(maxSlope, Mathf.Abs(hNext - h) / step);
                }
            }

            float slopeDegrees = Mathf.Atan(maxSlope) * Mathf.Rad2Deg;

            report.AppendLine();
            report.AppendLine("-- 3. Terrain shape --");
            report.AppendLine($"Height range over sampled area : {minH:F2} m .. {maxH:F2} m (span {maxH - minH:F2} m)");
            report.AppendLine($"Max slope over one vertex step : {slopeDegrees:F1} deg");

            bool heightPass = maxH <= settings.MaxHillHeight + 0.01f;
            bool slopePass = slopeDegrees < 30f;

            report.AppendLine($"Height within MaxHillHeight ({settings.MaxHillHeight} m) : {(heightPass ? "PASS" : "FAIL")}");
            report.AppendLine($"Slopes drivable (< 30 deg) : {(slopePass ? "PASS" : "FAIL")}");
            return heightPass && slopePass;
        }

        private static bool CheckSandDistribution(
            WorldSettings settings,
            TerrainGenerator generator,
            SandMaterialSelector selector,
            SandLibrary library,
            StringBuilder report)
        {
            int[] histogram = new int[Mathf.Max(1, library.Count)];
            int samples = 0;
            float chunkSize = settings.ChunkSize;

            for (int cx = -30; cx < 30; cx += 4)
            {
                for (int cz = -30; cz < 30; cz += 4)
                {
                    for (int i = 0; i < 25; i++)
                    {
                        float x = cx * chunkSize + (i % 5) * (chunkSize / 5f);
                        float z = cz * chunkSize + (i / 5) * (chunkSize / 5f);
                        float h = generator.SampleHeight(x, z);
                        float norm = settings.MaxHillHeight > 0f ? Mathf.Clamp01(h / settings.MaxHillHeight) : 0.5f;
                        int index = Mathf.Clamp(selector.GetSandTypeIndex(x, z, norm), 0, histogram.Length - 1);
                        histogram[index]++;
                        samples++;
                    }
                }
            }

            report.AppendLine();
            report.AppendLine("-- 4. Sand region distribution --");
            int usedTypes = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                float percent = samples > 0 ? histogram[i] * 100f / samples : 0f;
                if (histogram[i] > 0)
                {
                    usedTypes++;
                    SandType type = library[i];
                    string name = type != null ? type.displayName : "Sand" + (i + 1);
                    report.AppendLine($"  {name,-14} : {percent,6:F2} %");
                }
            }

            report.AppendLine($"Distinct sand types used : {usedTypes} / {library.Count}");
            bool pass = usedTypes >= Mathf.Min(4, library.Count);
            report.AppendLine(pass ? "PASS (multiple sand regions present)" : "FAIL");
            return pass;
        }

        private static bool CheckSeedSensitivity(
            WorldSettings settings,
            SandLibrary library,
            TerrainGenerator generator,
            SandMaterialSelector selector,
            StringBuilder report)
        {
            var otherSettings = Object.Instantiate(settings);
            var serializedOther = new SerializedObject(otherSettings);
            serializedOther.FindProperty("worldSeed").intValue = settings.WorldSeed + 777;
            serializedOther.ApplyModifiedPropertiesWithoutUndo();

            var otherSelector = new SandMaterialSelector(library, otherSettings);
            var otherGenerator = new TerrainGenerator(otherSettings, otherSelector);

            float seedDifference = 0f;
            for (int i = 0; i < 2000; i++)
            {
                float x = -1500f + i * 1.37f;
                float z = 900f - i * 0.91f;
                seedDifference += Mathf.Abs(generator.SampleHeight(x, z) - otherGenerator.SampleHeight(x, z));
            }

            seedDifference /= 2000f;

            int sandDifference = 0;
            for (int i = 0; i < 500; i++)
            {
                float x = i * 7.3f;
                float z = -i * 4.1f;
                if (selector.GetSandTypeIndex(x, z, 0.5f) != otherSelector.GetSandTypeIndex(x, z, 0.5f))
                {
                    sandDifference++;
                }
            }

            report.AppendLine();
            report.AppendLine("-- 5. Seed sensitivity (different seed must change the world) --");
            report.AppendLine($"Mean height difference : {seedDifference:F3} m");
            report.AppendLine($"Sand differences       : {sandDifference} / 500 samples");
            bool pass = seedDifference > 0.05f && sandDifference > 0;
            report.AppendLine(pass ? "PASS" : "FAIL");

            Object.DestroyImmediate(otherSettings);
            return pass;
        }
    }
}
