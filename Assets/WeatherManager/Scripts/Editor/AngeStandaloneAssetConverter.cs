using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherManager.Editor
{
    /// <summary>
    /// Converts glTFast KTX2 sub-assets into ordinary Unity assets before a
    /// Standalone build. The source GLBs stay in the project for authoring, but
    /// player dependencies use PNG textures, native Mesh assets and plain prefabs.
    /// </summary>
    public static class AngeStandaloneAssetConverter
    {
        private const string AngeRoot = "Assets/WeatherManager/Art/Ange";
        private const string MaterialRoot = AngeRoot + "/Materials";
        private const string PrefabRoot = AngeRoot + "/Prefabs";
        private const string NativeRoot = AngeRoot + "/Native";
        private const string TextureRoot = NativeRoot + "/Textures";
        private const string MeshRoot = NativeRoot + "/Meshes";

        private readonly struct TextureSlot
        {
            public TextureSlot(string property, string suffix, int maxSize, bool linear, bool normalMap)
            {
                Property = property;
                Suffix = suffix;
                MaxSize = maxSize;
                Linear = linear;
                NormalMap = normalMap;
            }

            public string Property { get; }
            public string Suffix { get; }
            public int MaxSize { get; }
            public bool Linear { get; }
            public bool NormalMap { get; }
        }

        private static readonly TextureSlot[] TextureSlots =
        {
            new TextureSlot("baseColorTexture", "Color", 1024, false, false),
            new TextureSlot("metallicRoughnessTexture", "ORM", 512, true, false),
            new TextureSlot("normalTexture", "Normal", 1024, true, true)
        };

        [MenuItem("Weather Manager/Prepare Ange Assets for Standalone")]
        public static void PrepareAngeAssetsForStandalone()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new BuildFailedException("Ange asset conversion must run in Edit Mode.");
            }

            EnsureAssetFolder(NativeRoot);
            EnsureAssetFolder(TextureRoot);
            EnsureAssetFolder(MeshRoot);

            var textureCount = ConvertMaterialTextures();
            var prefabCount = ConvertPrefabsToNativeAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var staleDependencies = FindGlbBuildDependencies();
            if (staleDependencies.Count > 0)
            {
                throw new BuildFailedException(
                    "Ange Standalone conversion left GLB build dependencies:\n" +
                    string.Join("\n", staleDependencies));
            }

            Debug.Log(
                $"WEATHER_MANAGER_ANGE_NATIVE_PASS:textures={textureCount}:prefabs={prefabCount}:glbDependencies=0");
        }

        [MenuItem("Weather Manager/Prepare Ange Assets and Build macOS")]
        public static void PrepareAndBuildMac()
        {
            PrepareAngeAssetsForStandalone();
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            EditorApplication.delayCall += BuildAfterImportSettles;
        }

        private static void BuildAfterImportSettles()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            EditorApplication.delayCall += WeatherManagerSceneBuilder.BuildMac;
        }

        private static int ConvertMaterialTextures()
        {
            var converted = 0;
            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot });
            Array.Sort(materialGuids, StringComparer.Ordinal);

            for (var materialIndex = 0; materialIndex < materialGuids.Length; materialIndex++)
            {
                var materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[materialIndex]);
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) continue;

                var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(materialPath));
                for (var slotIndex = 0; slotIndex < TextureSlots.Length; slotIndex++)
                {
                    var slot = TextureSlots[slotIndex];
                    if (!material.HasProperty(slot.Property)) continue;

                    var source = material.GetTexture(slot.Property) as Texture2D;
                    if (source == null) continue;

                    var sourcePath = AssetDatabase.GetAssetPath(source);
                    if (!sourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) continue;

                    var scale = material.GetTextureScale(slot.Property);
                    var offset = material.GetTextureOffset(slot.Property);
                    var targetPath = $"{TextureRoot}/{safeName}_{slot.Suffix}.png";
                    WriteTexturePng(source, targetPath, slot.MaxSize, slot.Linear);
                    ConfigureTextureImporter(targetPath, slot.MaxSize, slot.Linear, slot.NormalMap);

                    var replacement = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                    if (replacement == null)
                    {
                        throw new IOException($"Failed to import converted texture {targetPath}.");
                    }

                    material.SetTexture(slot.Property, replacement);
                    material.SetTextureScale(slot.Property, scale);
                    material.SetTextureOffset(slot.Property, offset);
                    EditorUtility.SetDirty(material);
                    converted++;
                }
            }

            AssetDatabase.SaveAssets();
            return converted;
        }

        private static int ConvertPrefabsToNativeAssets()
        {
            var converted = 0;
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            Array.Sort(prefabGuids, StringComparer.Ordinal);

            for (var prefabIndex = 0; prefabIndex < prefabGuids.Length; prefabIndex++)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[prefabIndex]);
                if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                var previewScene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var instance = PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
                    if (instance == null)
                    {
                        throw new InvalidOperationException($"Failed to instantiate {prefabPath}.");
                    }

                    PrefabUtility.UnpackPrefabInstance(
                        instance,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);

                    var material = FindMatchingMaterial(prefabPath);
                    var nativeMeshCount = ReplaceGlbMeshes(instance, prefabPath);
                    if (nativeMeshCount == 0)
                    {
                        throw new InvalidOperationException($"No GLB mesh was found in {prefabPath}.");
                    }

                    if (material != null)
                    {
                        AssignMaterial(instance, material);
                    }

                    var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out var success);
                    if (!success || saved == null)
                    {
                        throw new IOException($"Failed to save native prefab {prefabPath}.");
                    }
                    converted++;
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(previewScene);
                }
            }

            return converted;
        }

        private static int ReplaceGlbMeshes(GameObject root, string prefabPath)
        {
            var replaced = 0;
            var meshNumber = 0;
            var meshCache = new Dictionary<Mesh, Mesh>();
            var prefabName = SanitizeFileName(Path.GetFileNameWithoutExtension(prefabPath));

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (var index = 0; index < filters.Length; index++)
            {
                var source = filters[index].sharedMesh;
                if (!IsGlbSubAsset(source)) continue;
                filters[index].sharedMesh = GetOrCreateNativeMesh(
                    source, prefabName, meshNumber++, meshCache);
                replaced++;
            }

            var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var index = 0; index < skinnedRenderers.Length; index++)
            {
                var source = skinnedRenderers[index].sharedMesh;
                if (!IsGlbSubAsset(source)) continue;
                skinnedRenderers[index].sharedMesh = GetOrCreateNativeMesh(
                    source, prefabName, meshNumber++, meshCache);
                replaced++;
            }

            return replaced;
        }

        private static Mesh GetOrCreateNativeMesh(
            Mesh source,
            string prefabName,
            int meshNumber,
            Dictionary<Mesh, Mesh> cache)
        {
            if (cache.TryGetValue(source, out var cached)) return cached;

            var suffix = meshNumber == 0 ? string.Empty : $"_{meshNumber}";
            var targetPath = $"{MeshRoot}/{prefabName}{suffix}.asset";
            var native = AssetDatabase.LoadAssetAtPath<Mesh>(targetPath);
            if (native == null)
            {
                native = Object.Instantiate(source);
                native.name = prefabName + suffix;
                native.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(native, targetPath);
            }
            cache.Add(source, native);
            return native;
        }

        private static Material FindMatchingMaterial(string prefabPath)
        {
            var name = Path.GetFileNameWithoutExtension(prefabPath);
            return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialRoot}/{name}.mat");
        }

        private static void AssignMaterial(GameObject root, Material material)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var materials = renderer.sharedMaterials;
                if (materials.Length == 0)
                {
                    renderer.sharedMaterial = material;
                    continue;
                }

                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    materials[materialIndex] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static bool IsGlbSubAsset(Object asset)
        {
            if (asset == null) return false;
            var path = AssetDatabase.GetAssetPath(asset);
            return path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteTexturePng(Texture source, string targetPath, int maxSize, bool linear)
        {
            var scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(source.width, source.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            var readWrite = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var temporary = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGB32, readWrite);
            var previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(false, false);
                File.WriteAllBytes(ToAbsolutePath(targetPath), readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) Object.DestroyImmediate(readable);
            }

            AssetDatabase.ImportAsset(
                targetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureTextureImporter(
            string assetPath,
            int maxSize,
            bool linear,
            bool normalMap)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"No TextureImporter was created for {assetPath}.");
            }

            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !linear;
            importer.maxTextureSize = maxSize;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        private static List<string> FindGlbBuildDependencies()
        {
            var stale = new List<string>();
            var roots = new List<string>();
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            for (var index = 0; index < prefabGuids.Length; index++)
            {
                roots.Add(AssetDatabase.GUIDToAssetPath(prefabGuids[index]));
            }

            var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/WeatherManager/Scenes" });
            for (var index = 0; index < sceneGuids.Length; index++)
            {
                roots.Add(AssetDatabase.GUIDToAssetPath(sceneGuids[index]));
            }

            var dependencies = AssetDatabase.GetDependencies(roots.ToArray(), true);
            for (var index = 0; index < dependencies.Length; index++)
            {
                if (dependencies[index].EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                {
                    stale.Add(dependencies[index]);
                }
            }
            stale.Sort(StringComparer.Ordinal);
            return stale;
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            Directory.CreateDirectory(ToAbsolutePath(assetPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new DirectoryNotFoundException("Unity project root could not be resolved.");
            }
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            for (var index = 0; index < invalid.Length; index++)
            {
                value = value.Replace(invalid[index], '_');
            }
            return value;
        }
    }
}
