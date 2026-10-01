using UnityEngine;
using UnityEngine.Rendering;

namespace Tanks.World.Environment
{
    /// <summary>
    /// Applies the wasteland atmosphere: dust fog, sun and ambient tint.
    ///
    /// The fog serves two purposes:
    ///  1. atmosphere - a dry, dusty haze over the desert
    ///  2. streaming - it hides the boundary where chunks are still generating, so terrain never
    ///     visibly "pops in" at the edge of the loaded area
    ///
    /// It is deliberately kept thin at close range so the player can always see where they drive.
    /// In URP the fog settings on RenderSettings plus the camera background colour are what matter,
    /// so no custom shader or volume profile is required for the prototype.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WastelandFogController : MonoBehaviour
    {
        [SerializeField] private WorldSettings settings;

        [Tooltip("Camera that receives the atmospheric setup. Found automatically when empty.")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("When enabled the camera background is tinted to match the fog for a seamless horizon.")]
        [SerializeField] private bool tintCameraBackground = true;

        [Tooltip("When enabled the scene sun and ambient lighting are driven from the world settings.")]
        [SerializeField] private bool applyLighting = true;

        private Light sunLight;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        /// <summary>Pushes the configured atmosphere into Unity's render settings.</summary>
        public void Apply()
        {
            if (settings == null)
            {
                return;
            }

            ApplyFog();
            ApplyLighting();
            ApplyCameraBackground();
        }

        private void ApplyFog()
        {
            RenderSettings.fog = settings.EnableFog;
            RenderSettings.fogMode = settings.FogDensityPower <= 1.01f
                ? FogMode.Linear
                : FogMode.ExponentialSquared;
            RenderSettings.fogColor = settings.FogColor;
            RenderSettings.fogStartDistance = settings.FogStartDistance;
            RenderSettings.fogEndDistance = settings.FogEndDistance;

            if (RenderSettings.fogMode == FogMode.ExponentialSquared)
            {
                float span = Mathf.Max(1f, settings.FogEndDistance - settings.FogStartDistance);
                RenderSettings.fogDensity = Mathf.Clamp01(settings.FogDensityPower / span);
            }
        }

        private void ApplyLighting()
        {
            if (!applyLighting)
            {
                return;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = settings.AmbientSkyColor;
            RenderSettings.ambientEquatorColor = Color.Lerp(
                settings.AmbientSkyColor,
                settings.AmbientGroundColor,
                0.5f);
            RenderSettings.ambientGroundColor = settings.AmbientGroundColor;
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fogColor = settings.FogColor;

            if (sunLight == null)
            {
                sunLight = FindSunLight();
            }

            if (sunLight == null)
            {
                return;
            }

            sunLight.color = settings.SunColor;
            sunLight.intensity = settings.SunIntensity;
            sunLight.transform.rotation = Quaternion.Euler(
                settings.SunAngles.x,
                settings.SunAngles.y,
                0f);
        }

        private void ApplyCameraBackground()
        {
            if (!tintCameraBackground || !settings.EnableFog)
            {
                return;
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                return;
            }

            // The sky is hidden by fog at the horizon, so matching the clear colour to the fog
            // removes any hard line where the terrain ends and the skybox begins.
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = settings.FogColor;
            targetCamera.farClipPlane = Mathf.Max(
                targetCamera.farClipPlane,
                settings.FogEndDistance + 100f);
        }

        /// <summary>Searches the scene for the directional light used as the wasteland sun.</summary>
        private Light FindSunLight()
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }

            return null;
        }

        /// <summary>Assigns the world settings used by this atmosphere controller.</summary>
        public void SetSettings(WorldSettings worldSettings)
        {
            settings = worldSettings;
            Apply();
        }
    }
}
