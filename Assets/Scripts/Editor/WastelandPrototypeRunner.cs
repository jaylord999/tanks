using UnityEditor;
using UnityEngine;
using Tanks.World;

namespace Tanks.EditorTools
{
    /// <summary>
    /// Batch mode entry points used to drive the prototype from the command line. This is how the
    /// terrain system is compiled, generated and verified without opening the editor by hand.
    /// </summary>
    public static class WastelandPrototypeRunner
    {
        /// <summary>Creates the assets and saves the configured scene. Run with -executeMethod.</summary>
        public static void SetupAndSaveScene()
        {
            SandMaterialUrpConverter.ConvertAll();
            WastelandPrototypeSetup.CreateAssets();
            WastelandPrototypeSetup.SetupScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Runner] Setup complete.");
            Exit(0);
        }

        /// <summary>
        /// Converts the sand materials to URP Lit. Run this when the terrain renders magenta, which
        /// means the materials are still on the built-in Standard shader.
        /// </summary>
        public static void FixSandMaterials()
        {
            SandMaterialUrpConverter.ConvertAll();
            AssetDatabase.SaveAssets();
            Exit(0);
        }

        /// <summary>
        /// Repairs a WorldSettings asset saved before newer fields existed. Run this when the terrain
        /// looks flat, which usually means the new procedural dials loaded as 0.
        /// </summary>
        public static void RepairWorldSettings()
        {
            WorldSettingsRepair.Repair();
            AssetDatabase.SaveAssets();
            Exit(0);
        }

        /// <summary>Runs the pure (non Play Mode) verification checks.</summary>
        public static void Verify()
        {
            WastelandPrototypeVerifier.Verify();
            AssetDatabase.SaveAssets();
            Exit(0);
        }

        /// <summary>Runs the pure checks and the chunk streaming verification together.</summary>
        public static void VerifyAll()
        {
            WastelandPrototypeSetup.CreateAssets();
            WastelandPrototypeVerifier.Verify();

            bool streamingPass = WastelandStreamingVerifier.Run();
            Debug.Log(streamingPass
                ? "[Runner] Chunk streaming verification PASSED."
                : "[Runner] Chunk streaming verification FAILED.");

            AssetDatabase.SaveAssets();
            Exit(streamingPass ? 0 : 1);
        }

        /// <summary>Streams chunks headlessly to prove generation, unloading and pooling work.</summary>
        public static void StreamChunks()
        {
            WastelandPrototypeSetup.CreateAssets();
            bool pass = WastelandStreamingVerifier.Run();
            AssetDatabase.SaveAssets();
            Exit(pass ? 0 : 1);
        }


        private static void Exit(int code)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
            }
        }
    }
}
