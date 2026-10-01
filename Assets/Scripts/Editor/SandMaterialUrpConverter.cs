using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tanks.EditorTools
{
    /// <summary>
    /// Converts the imported sand materials from the built-in Standard shader to URP Lit.
    ///
    /// Why this is needed: the sand asset pack ships separate HDRP and URP unitypackages, and the
    /// extracted project folder contains the built-in Standard variants. URP cannot render the
    /// built-in Standard shader, so every terrain triangle showed up magenta.
    ///
    /// The conversion is non-destructive to the original art:
    ///  - the same .mat assets are updated in place, keeping their texture references
    ///  - Standard properties are remapped onto their URP equivalents
    ///  - for sand types whose albedo texture was never shipped, a readable albedo is derived from
    ///    the pack's own specular texture and tint, so nothing renders as a flat untextured plane
    ///
    /// Run "Tanks/Fix Sand Materials For URP" once after importing the pack.
    /// </summary>
    public static class SandMaterialUrpConverter
    {
        private const string SandMaterialFolder = "Assets/YughuesFreeSandMaterials/Materials";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>Where derived albedo textures are written. Never overwrites the pack's own art.</summary>
        private const string RepairedAlbedoFolder = "Assets/YughuesFreeSandMaterials/Generated";

        /// <summary>Cap for generated albedo textures so terrain stays mobile friendly.</summary>
        private const int RepairedAlbedoMaxSize = 1024;

        /// <summary>The pack's own texture folder, searched for a real diffuse when one was shipped.</summary>
        private const string SandTextureFolder = "Assets/YughuesFreeSandMaterials/Textures";

        /// <summary>Brightness a derived albedo aims for, so it sits with the pack's own sand art.</summary>
        private const float TargetAlbedoBrightness = 0.78f;

        /// <summary>Below this mean brightness a derived albedo is rejected instead of written.</summary>
        private const float MinimumAlbedoBrightness = 0.25f;

        [MenuItem("Tanks/Fix Sand Materials For URP")]
        public static void ConvertAll()
        {
            Shader urpLit = Shader.Find(UrpLitShaderName);
            if (urpLit == null)
            {
                Debug.LogError(
                    $"[SandMaterialUrpConverter] '{UrpLitShaderName}' was not found. Is the URP package installed?");
                return;
            }

            List<Material> materials = CollectSandMaterials();
            if (materials.Count == 0)
            {
                Debug.LogError($"[SandMaterialUrpConverter] No materials found in {SandMaterialFolder}.");
                return;
            }

            int converted = 0;
            int albedoRepaired = 0;

            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];
                if (Convert(material, urpLit))
                {
                    converted++;
                }

                if (RepairMissingAlbedo(material))
                {
                    albedoRepaired++;
                }

                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SandMaterialUrpConverter] Converted {converted}/{materials.Count} sand materials to URP Lit. " +
                      $"Repaired {albedoRepaired} missing albedo texture(s) from the pack's specular maps.");
        }

        private static List<Material> CollectSandMaterials()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { SandMaterialFolder });
            var materials = new List<Material>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".mat"))
                {
                    continue;
                }

                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null)
                {
                    materials.Add(material);
                }
            }

            return materials;
        }

        /// <summary>Retargets one material to URP Lit and remaps its Standard properties.</summary>
        private static bool Convert(Material material, Shader urpLit)
        {
            bool changed = false;

            if (material.shader != urpLit)
            {
                material.shader = urpLit;
                changed = true;
            }

            // ---- Textures ------------------------------------------------------------------------
            // Standard _MainTex / _SpecGlossMap map onto URP _BaseMap / _MetallicGlossMap. URP reads
            // metallic from R and smoothness from A of _MetallicGlossMap - the same layout the pack's
            // specular maps already use, so the reference carries over directly.
            TransferTexture(material, "_MainTex", "_BaseMap");
            TransferTexture(material, "_BumpMap", "_BumpMap");
            TransferTexture(material, "_SpecGlossMap", "_MetallicGlossMap");
            TransferTexture(material, "_OcclusionMap", "_OcclusionMap");
            TransferTexture(material, "_EmissionMap", "_EmissionMap");

            // ---- Floats --------------------------------------------------------------------------
            TransferFloat(material, "_Glossiness", "_Smoothness", 0.5f);
            TransferFloat(material, "_Metallic", "_Metallic", 0f);
            TransferFloat(material, "_BumpScale", "_BumpScale", 1f);
            TransferFloat(material, "_OcclusionStrength", "_OcclusionStrength", 1f);

            // ---- Colours -------------------------------------------------------------------------
            TransferColor(material, "_Color", "_BaseColor", Color.white);

            // Sand is a rough dielectric. Metallic must stay 0 or the terrain reads as polished metal.
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            // Keep only a slight surface sheen: fully rough sand looks flat and lifeless.
            if (material.HasProperty("_Smoothness"))
            {
                float smoothness = material.GetFloat("_Smoothness");
                material.SetFloat("_Smoothness", Mathf.Clamp(smoothness * 0.35f, 0.05f, 0.35f));
            }

            // ---- Keywords / workflow -------------------------------------------------------------
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICGLOSSMAP");

            if (material.HasProperty("_WorkflowMode"))
            {
                material.SetFloat("_WorkflowMode", 1f);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0f);
            }

            material.renderQueue = -1;
            return changed;
        }

        private static void TransferTexture(Material material, string from, string to)
        {
            if (!material.HasProperty(to) || !material.HasProperty(from))
            {
                return;
            }

            Texture source = material.GetTexture(from);
            if (source == null || material.GetTexture(to) != null)
            {
                return;
            }

            material.SetTexture(to, source);

            // Carry tiling / offset across so the sand keeps its intended scale.
            material.SetTextureScale(to, material.GetTextureScale(from));
            material.SetTextureOffset(to, material.GetTextureOffset(from));
        }

        private static void TransferFloat(Material material, string from, string to, float fallback)
        {
            if (!material.HasProperty(to))
            {
                return;
            }

            if (material.HasProperty(from))
            {
                material.SetFloat(to, material.GetFloat(from));
            }
            else
            {
                material.SetFloat(to, fallback);
            }
        }

        private static void TransferColor(Material material, string from, string to, Color fallback)
        {
            if (!material.HasProperty(to))
            {
                return;
            }

            material.SetColor(to, material.HasProperty(from) ? material.GetColor(from) : fallback);
        }

        /// <summary>
        /// Gives a material an albedo texture when the pack never shipped one.
        ///
        /// 4 of the 12 sand types (01, 02, 04, 05) ship a normal map and a specular map but no diffuse
        /// texture, so those materials were authored with no _MainTex at all. Left alone they render as
        /// a flat untextured surface, which reads as a bug next to the other sands.
        ///
        /// The specular map is used instead: it is a real capture of the same sand surface, so it
        /// carries the correct grain and dune detail. It is converted to luminance and tinted with the
        /// material's own authored colour, keeping the sand in its intended colour family while giving
        /// it genuine surface texture.
        ///
        /// The result is written as a new texture asset next to the originals; the source specular
        /// texture is never modified.
        /// </summary>
        private static bool RepairMissingAlbedo(Material material)
        {
            if (!material.HasProperty("_BaseMap"))
            {
                return false;
            }

            Texture current = material.GetTexture("_BaseMap");
            bool missing = current == null;
            bool derivedOnly = current != null && IsGeneratedAlbedo(current);

            // 1. Whenever the albedo is missing, or is only a placeholder this tool derived, use the
            //    pack's own diffuse art if this sand type ships one. This is what stops a real diffuse
            //    texture being shadowed by a derived one: an albedo generated earlier must never win
            //    over the artwork it was standing in for.
            if (missing || derivedOnly)
            {
                Texture2D authored = FindAuthoredDiffuse(material.name);
                if (authored != null && authored != current)
                {
                    AssignAlbedo(material, authored);
                    return true;
                }
            }

            // A usable albedo is already in place; nothing to derive.
            if (!missing)
            {
                return false;
            }

            // 2. No albedo was shipped at all: derive one from the pack's own specular capture.
            //    Prefer an existing repair so this stays repeatable and cheap on repeat runs.
            string repairedPath = GetRepairedAlbedoPath(material.name);
            Texture2D repaired = AssetDatabase.LoadAssetAtPath<Texture2D>(repairedPath);

            if (repaired == null)
            {
                Texture specular = material.HasProperty("_MetallicGlossMap")
                    ? material.GetTexture("_MetallicGlossMap")
                    : null;

                if (specular == null)
                {
                    return false;
                }

                Texture2D created = BuildAlbedoFromSpecular(specular, GetAuthoredTint(material));
                if (created == null)
                {
                    // A near-black albedo is indistinguishable from an untextured surface, so it is
                    // never written: better to leave the material clearly unassigned than to hide a
                    // broken derivation behind a texture that looks like a hole in the world.
                    Debug.LogWarning(
                        $"[SandMaterialUrpConverter] Could not derive a usable albedo for {material.name}. " +
                        "It will render untextured. Assign an albedo manually or check the source maps.");
                    return false;
                }

                EnsureFolder(RepairedAlbedoFolder);
                File.WriteAllBytes(repairedPath, created.EncodeToPNG());
                Object.DestroyImmediate(created);

                AssetDatabase.ImportAsset(repairedPath, ImportAssetOptions.ForceUpdate);
                ConfigureRepairedAlbedoImport(repairedPath);
                repaired = AssetDatabase.LoadAssetAtPath<Texture2D>(repairedPath);
            }

            if (repaired == null)
            {
                return false;
            }

            AssignAlbedo(material, repaired);
            return true;
        }

        /// <summary>
        /// Points a material at an albedo texture. The albedo carries its own colour, so the tint is
        /// reset to white to avoid multiplying the sand colour in twice.
        /// </summary>
        private static void AssignAlbedo(Material material, Texture albedo)
        {
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetColor("_BaseColor", Color.white);
        }

        /// <summary>True when the texture is one of the albedos this tool derived itself.</summary>
        private static bool IsGeneratedAlbedo(Texture texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.Replace('\\', '/')
                .StartsWith(RepairedAlbedoFolder + "/", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Finds the pack's own diffuse texture for a material (M_YFSM_Sand03 -> T_YFSM_03_d).
        /// 4 of the 12 sand types never shipped one, in which case null is returned and the caller
        /// falls back to deriving an albedo.
        /// </summary>
        private static Texture2D FindAuthoredDiffuse(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return null;
            }

            int marker = materialName.LastIndexOf("Sand", System.StringComparison.Ordinal);
            if (marker < 0)
            {
                return null;
            }

            string number = materialName.Substring(marker + "Sand".Length).Trim();
            if (number.Length == 0)
            {
                return null;
            }

            string[] guids = AssetDatabase.FindAssets(
                $"T_YFSM_{number}_d t:Texture2D",
                new[] { SandTextureFolder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                Texture2D candidate = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Reads the material's authored Standard tint, falling back to a dry sand tone.</summary>
        private static Color GetAuthoredTint(Material material)
        {
            Color tint = material.HasProperty("_Color")
                ? material.GetColor("_Color")
                : new Color(0.65f, 0.6f, 0.49f);

            tint.a = 1f;
            return tint;
        }

        /// <summary>
        /// Turns a specular map into an albedo: take luminance to preserve the real grain while
        /// discarding the specular highlight, then tint with the authored sand colour.
        /// </summary>
        private static Texture2D BuildAlbedoFromSpecular(Texture specular, Color tint)
        {
            Texture2D source = ToReadableTexture(specular);
            if (source == null)
            {
                return null;
            }

            Color[] pixels = source.GetPixels();
            int count = pixels.Length;

            // Measure the luminance range first: the pack's specular maps are low contrast captures,
            // so the albedo has to be built from the range that is actually present rather than from
            // an assumed 0..1 spread (which is what leaves the sand looking flat and washed out).
            var luminance = new float[count];
            float min = 1f;
            float max = 0f;

            for (int i = 0; i < count; i++)
            {
                Color pixel = pixels[i];
                float value = pixel.r * 0.299f + pixel.g * 0.587f + pixel.b * 0.114f;
                luminance[i] = value;

                if (value < min) min = value;
                if (value > max) max = value;
            }

            float span = Mathf.Max(0.0001f, max - min);

            // Normalise the authored tint so its hue is kept but the sand lands at a sunlit
            // brightness. Multiplying the raw tint instead would carry over however dark the
            // original Standard-era colour happened to be.
            float tintMean = Mathf.Max(0.0001f, (tint.r + tint.g + tint.b) / 3f);
            float tintScale = TargetAlbedoBrightness / tintMean;
            Color baseColor = new Color(
                Mathf.Min(1f, tint.r * tintScale),
                Mathf.Min(1f, tint.g * tintScale),
                Mathf.Min(1f, tint.b * tintScale),
                1f);

            var output = new Color[count];
            float total = 0f;

            for (int i = 0; i < count; i++)
            {
                // Auto-level: stretching the measured band to 0..1 is what makes the real grain
                // visible on the terrain even when the source map is nearly uniform.
                float normalised = Mathf.Clamp01((luminance[i] - min) / span);
                float detail = Mathf.Lerp(0.82f, 1.18f, normalised);

                Color value = new Color(
                    Mathf.Clamp01(baseColor.r * detail),
                    Mathf.Clamp01(baseColor.g * detail),
                    Mathf.Clamp01(baseColor.b * detail),
                    1f);

                output[i] = value;
                total += (value.r + value.g + value.b) / 3f;
            }

            // Refuse to hand back a near-black result: on the terrain that reads as an untextured
            // patch, which is exactly the failure this derivation exists to prevent.
            float mean = count > 0 ? total / count : 0f;
            if (mean < MinimumAlbedoBrightness)
            {
                Object.DestroyImmediate(source);
                Debug.LogWarning(
                    $"[SandMaterialUrpConverter] Derived albedo was too dark (mean {mean:F3}); refusing to write it.");
                return null;
            }

            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true);
            readable.SetPixels(output);
            readable.Apply(true);

            Object.DestroyImmediate(source);
            return readable;
        }

        /// <summary>
        /// Produces a CPU readable copy of a texture. Imported textures are usually not readable, so
        /// pixels are pulled back through a temporary RenderTexture instead of enabling read/write on
        /// the original asset. The copy is capped so terrain textures stay mobile friendly.
        /// </summary>
        private static Texture2D ToReadableTexture(Texture source)
        {
            if (source == null)
            {
                return null;
            }

            int width = Mathf.Min(source.width, RepairedAlbedoMaxSize);
            int height = Mathf.Min(source.height, RepairedAlbedoMaxSize);

            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, temporary);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temporary;

            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readable.Apply(false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);

            return readable;
        }

        /// <summary>Configures the repaired albedo's import settings for tiled terrain use.</summary>
        private static void ConfigureRepairedAlbedoImport(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = RepairedAlbedoMaxSize;
            importer.SaveAndReimport();
        }

        private static string GetRepairedAlbedoPath(string materialName)
        {
            return $"{RepairedAlbedoFolder}/{materialName}_Albedo.png";
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(assetFolder);

            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
