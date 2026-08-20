using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherManager.Editor
{
    public static class AngeDesktopBuildPipeline
    {
        private const string AngeRoot = "Assets/WeatherManager/Art/Ange";
        private const string MaterialRoot = AngeRoot + "/Materials";
        private const string PrefabRoot = AngeRoot + "/Prefabs";
        private const string DesktopTextureRoot = AngeRoot + "/DesktopTextures";
        private const string DesktopMeshRoot = AngeRoot + "/DesktopMeshes";
        private const string ScenePath = "Assets/WeatherManager/Scenes/WeatherManager.unity";

        public static void PrepareDesktopAssets()
        {
            EnsureFolder(DesktopTextureRoot);
            EnsureFolder(DesktopMeshRoot);

            var textureCount = ExportMaterialTextures();
            var optimizedTextureCount = OptimizeExistingDesktopTextures();
            var meshCount = DetachPrefabMeshes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(
                $"WEATHER_MANAGER_DESKTOP_ASSETS_PASS:textures={textureCount}"
                + $":optimizedTextures={optimizedTextureCount}:meshes={meshCount}"
                + $":prefabs={AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }).Length}");
        }

        private static int ExportMaterialTextures()
        {
            var textureCount = 0;
            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot });
            for (var materialIndex = 0; materialIndex < materialGuids.Length; materialIndex++)
            {
                var materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[materialIndex]);
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) continue;

                var propertyNames = material.GetTexturePropertyNames();
                for (var propertyIndex = 0; propertyIndex < propertyNames.Length; propertyIndex++)
                {
                    var propertyName = propertyNames[propertyIndex];
                    if (material.GetTexture(propertyName) is not Texture2D source) continue;
                    var sourcePath = AssetDatabase.GetAssetPath(source);
                    if (!sourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) continue;

                    var linear = IsLinearTexture(propertyName);
                    var targetPath = $"{DesktopTextureRoot}/{SafeName(material.name)}-{SafeName(propertyName)}.png";
                    var desktopTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                    if (desktopTexture == null)
                    {
                        WritePng(source, targetPath, linear);
                        AssetDatabase.ImportAsset(
                            targetPath,
                            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        desktopTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                        if (desktopTexture == null)
                        {
                            throw new InvalidOperationException($"Failed to import desktop texture {targetPath}.");
                        }
                    }
                    ConfigureTextureImporter(targetPath, desktopTexture, linear);
                    material.SetTexture(propertyName, desktopTexture);
                    EditorUtility.SetDirty(material);
                    textureCount++;
                }
            }
            AssetDatabase.SaveAssets();
            return textureCount;
        }

        private static int DetachPrefabMeshes()
        {
            var meshCount = 0;
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            for (var prefabIndex = 0; prefabIndex < prefabGuids.Length; prefabIndex++)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[prefabIndex]);
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    UnpackNestedPrefabInstances(root);
                    var meshOrdinal = 0;
                    var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
                    for (var index = 0; index < meshFilters.Length; index++)
                    {
                        var mesh = meshFilters[index].sharedMesh;
                        if (!IsGlbSubAsset(mesh)) continue;
                        meshFilters[index].sharedMesh = CloneMesh(mesh, root.name, meshOrdinal++);
                        meshCount++;
                    }

                    var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    for (var index = 0; index < skinnedRenderers.Length; index++)
                    {
                        var mesh = skinnedRenderers[index].sharedMesh;
                        if (!IsGlbSubAsset(mesh)) continue;
                        skinnedRenderers[index].sharedMesh = CloneMesh(mesh, root.name, meshOrdinal++);
                        meshCount++;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                    if (!success)
                    {
                        throw new IOException($"Failed to save desktop-safe prefab {prefabPath}.");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            return meshCount;
        }

        private static int OptimizeExistingDesktopTextures()
        {
            var count = 0;
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { DesktopTextureRoot });
            for (var index = 0; index < textureGuids.Length; index++)
            {
                var texturePath = AssetDatabase.GUIDToAssetPath(textureGuids[index]);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) continue;
                ConfigureTextureImporter(texturePath, texture, IsLinearTexture(texturePath));
                count++;
            }
            return count;
        }

        private static void UnpackNestedPrefabInstances(GameObject root)
        {
            var nestedRoots = new List<GameObject>();
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index].gameObject;
                if (candidate == root || !PrefabUtility.IsAnyPrefabInstanceRoot(candidate)) continue;
                nestedRoots.Add(candidate);
            }
            nestedRoots.Sort((left, right) => Depth(right.transform).CompareTo(Depth(left.transform)));
            for (var index = 0; index < nestedRoots.Count; index++)
            {
                if (nestedRoots[index] != null && PrefabUtility.IsAnyPrefabInstanceRoot(nestedRoots[index]))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        nestedRoots[index],
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                }
            }
        }

        private static Mesh CloneMesh(Mesh source, string prefabName, int ordinal)
        {
            var targetPath = $"{DesktopMeshRoot}/{SafeName(prefabName)}-{ordinal:00}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(targetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(source, existing);
                existing.name = $"{prefabName} Desktop Mesh {ordinal:00}";
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var clone = Object.Instantiate(source);
            clone.name = $"{prefabName} Desktop Mesh {ordinal:00}";
            AssetDatabase.CreateAsset(clone, targetPath);
            return clone;
        }

        private static bool IsGlbSubAsset(Object asset)
        {
            return asset != null
                && AssetDatabase.GetAssetPath(asset).EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }

        private static void WritePng(Texture2D source, string assetPath, bool linear)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unity project root is unavailable.");
            var absolutePath = Path.Combine(projectRoot, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? projectRoot);

            var previous = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(
                Mathf.Max(1, source.width),
                Mathf.Max(1, source.height),
                0,
                RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var readable = new Texture2D(
                renderTexture.width,
                renderTexture.height,
                TextureFormat.RGBA32,
                false,
                linear);
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                readable.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0, false);
                readable.Apply(false, false);
                File.WriteAllBytes(absolutePath, readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                Object.DestroyImmediate(readable);
            }
        }

        private static void ConfigureTextureImporter(string assetPath, Texture2D source, bool linear)
        {
            if (AssetImporter.GetAtPath(assetPath) is not UnityEditor.TextureImporter importer) return;
            importer.sRGBTexture = !linear;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            importer.crunchedCompression = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = source.wrapMode;
            importer.filterMode = source.filterMode;
            importer.anisoLevel = source.anisoLevel;
            importer.alphaIsTransparency = !linear;
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = 1024;
            standalone.format = TextureImporterFormat.BC7;
            standalone.compressionQuality = 100;
            importer.SetPlatformTextureSettings(standalone);
            importer.SaveAndReimport();
        }

        private static bool IsLinearTexture(string propertyName)
        {
            return propertyName.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("rough", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("occlusion", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int Depth(Transform transform)
        {
            var depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        private static string SafeName(string value)
        {
            foreach (var character in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(character, '_');
            }
            return value.Replace(' ', '-');
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException($"Invalid asset folder {path}.");
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
