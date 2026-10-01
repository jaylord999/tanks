using UnityEngine;
using UnityEngine.InputSystem;

namespace Tanks.World.Environment
{
    /// <summary>
    /// Temporary development fly camera.
    ///
    /// This exists ONLY so the procedural terrain can be explored and verified before the tank,
    /// player controller and multiplayer systems are built. It is intentionally isolated: delete this
    /// component and assign the real tank transform to ChunkManager.followTarget when the tank lands.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TerrainFlyCamera : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 45f;
        [SerializeField] private float boostMultiplier = 3f;
        [SerializeField] private float lookSensitivity = 0.12f;

        [Header("Height")]
        [SerializeField] private World.ChunkManager chunkManager;
        [SerializeField] private bool followTerrainHeight = true;
        [SerializeField] private float heightAboveTerrain = 6f;
        [SerializeField] private float heightFollowSmoothing = 6f;

        private float yaw;
        private float pitch = 20f;
        private bool cursorCaptured;

        private void Start()
        {
            if (chunkManager == null)
            {
                chunkManager = FindFirstObjectByType<World.ChunkManager>();
            }

            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;

            if (followTerrainHeight && chunkManager != null)
            {
                Vector3 position = transform.position;
                position.y = chunkManager.SampleTerrainHeight(position.x, position.z) + heightAboveTerrain;
                transform.position = position;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    cursorCaptured = true;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    cursorCaptured = false;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                if (cursorCaptured)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * lookSensitivity;
                    pitch -= delta.y * lookSensitivity;
                    pitch = Mathf.Clamp(pitch, -80f, 80f);
                }
            }

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 move = Vector3.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed)
                {
                    move += transform.forward;
                }

                if (keyboard.sKey.isPressed)
                {
                    move -= transform.forward;
                }

                if (keyboard.dKey.isPressed)
                {
                    move += transform.right;
                }

                if (keyboard.aKey.isPressed)
                {
                    move -= transform.right;
                }

                if (keyboard.eKey.isPressed || keyboard.spaceKey.isPressed)
                {
                    move += Vector3.up;
                }

                if (keyboard.qKey.isPressed || keyboard.leftCtrlKey.isPressed)
                {
                    move -= Vector3.up;
                }
            }

            float speed = moveSpeed;
            if (keyboard != null && keyboard.leftShiftKey.isPressed)
            {
                speed *= boostMultiplier;
            }

            if (move.sqrMagnitude > 0.0001f)
            {
                transform.position += move.normalized * (speed * Time.deltaTime);
            }

            ApplyTerrainFollow();
        }

        /// <summary>
        /// Keeps the camera at a readable height above the sand so the developer can always see the
        /// landscape while exploring the streaming world.
        /// </summary>
        private void ApplyTerrainFollow()
        {
            if (!followTerrainHeight || chunkManager == null)
            {
                return;
            }

            Vector3 position = transform.position;
            float targetHeight = chunkManager.SampleTerrainHeight(position.x, position.z) + heightAboveTerrain;
            position.y = Mathf.Lerp(
                position.y,
                targetHeight,
                1f - Mathf.Exp(-heightFollowSmoothing * Time.deltaTime));
            transform.position = position;
        }

        /// <summary>Assigns the chunk manager so the camera can query terrain height.</summary>
        public void SetChunkManager(World.ChunkManager manager)
        {
            chunkManager = manager;
        }

        /// <summary>Assigns the development parameters used by the procedural world bootstrap.</summary>
        public void Configure(float height, float speed)
        {
            heightAboveTerrain = height;
            moveSpeed = speed;
        }
    }
}
