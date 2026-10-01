using UnityEngine;
using Tanks.World;
using Tanks.World.Chunks;

namespace Tanks.Debugging
{
    /// <summary>
    /// Temporary development overlay showing the procedural world state.
    ///
    /// Deliberately isolated and trivial to remove: everything is behind one enabled flag, the
    /// component is self contained, and it only drives IMGUI (no prefabs, no scene dependencies).
    /// Delete this file or disable the component when the prototype phase is over.
    /// </summary>
    [DisallowMultipleComponent]
    public class TerrainDebugOverlay : MonoBehaviour
    {
        [SerializeField] private ChunkManager chunkManager;
        [SerializeField] private bool visible = true;
        [SerializeField] private bool showRuntimeStats = true;
        [SerializeField] private int fontSize = 14;
        [SerializeField] private Vector2 screenOffset = new Vector2(12f, 12f);

        private GUIStyle style;
        private float smoothedFps;
        private int peakLoadedChunks;

        private void Awake()
        {
            if (chunkManager == null)
            {
                chunkManager = FindFirstObjectByType<ChunkManager>();
            }
        }

        private void Update()
        {
            if (!showRuntimeStats)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;
            if (delta > 0f)
            {
                float instant = 1f / delta;
                smoothedFps = smoothedFps <= 0f ? instant : Mathf.Lerp(smoothedFps, instant, 0.08f);
            }

            if (chunkManager != null && chunkManager.LoadedChunkCount > peakLoadedChunks)
            {
                peakLoadedChunks = chunkManager.LoadedChunkCount;
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            if (chunkManager == null)
            {
                chunkManager = FindFirstObjectByType<ChunkManager>();
                if (chunkManager == null)
                {
                    return;
                }
            }

            EnsureStyle();

            WorldSettings settings = chunkManager.Settings;
            if (settings == null)
            {
                return;
            }

            ChunkCoordinate current = chunkManager.CurrentPlayerChunk;

            string text =
                "WASTELAND PROTOTYPE\n" +
                $"World Seed: {settings.WorldSeed}\n" +
                $"Current Chunk X: {current.X}\n" +
                $"Current Chunk Z: {current.Z}\n" +
                $"Loaded Chunk Count: {chunkManager.LoadedChunkCount}\n" +
                $"Generated Chunk Count: {chunkManager.GeneratedChunkCount}";

            if (showRuntimeStats)
            {
                text +=
                    $"\nPooled Chunk Count: {chunkManager.PooledChunkCount}" +
                    $"\nPeak Loaded Chunks: {peakLoadedChunks}" +
                    $"\nView Distance: {settings.ViewDistanceInChunks} ({GetVisibleChunkTotal(settings)} chunks)" +
                    $"\nChunk Size: {settings.ChunkSize} m   Resolution: {settings.TerrainResolution}" +
                    $"\nMax Hill Height: {settings.MaxHillHeight} m" +
                    $"\nFPS: {smoothedFps:F0}";

                Vector3 cameraPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                text += $"\nPosition: {cameraPosition.x:F0}, {cameraPosition.y:F0}, {cameraPosition.z:F0}";
            }

            GUI.Label(new Rect(screenOffset.x, screenOffset.y, Screen.width, Screen.height), text, style);
        }

        private static int GetVisibleChunkTotal(WorldSettings settings)
        {
            int side = settings.ViewDistanceInChunks * 2 + 1;
            return side * side;
        }

        private void EnsureStyle()
        {
            if (style != null && style.fontSize == fontSize)
            {
                return;
            }

            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.UpperLeft,
                richText = false
            };

            style.normal.textColor = Color.white;
        }

        /// <summary>Shows or hides the overlay.</summary>
        public void SetVisible(bool value)
        {
            visible = value;
        }

        /// <summary>Assigns the manager whose state is displayed.</summary>
        public void SetChunkManager(ChunkManager manager)
        {
            chunkManager = manager;
        }
    }
}
