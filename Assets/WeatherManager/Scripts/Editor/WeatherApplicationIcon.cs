using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace WeatherManager.Editor
{
    /// <summary>Keep the approved game artwork bound in every Android launcher icon slot.</summary>
    public static class WeatherApplicationIcon
    {
        private const string Folder = "Assets/WeatherManager/Art/Branding/";
        public const string IconPath = Folder + "WeatherManagerIcon.png";
        private const string ForegroundPath = Folder + "AdaptiveForeground.png";
        private const string BackgroundPath = Folder + "AdaptiveBackground.png";

        [MenuItem("Weather Manager/Release/Configure Application Icon")]
        public static void Configure()
        {
            if (!File.Exists(IconPath)) throw new FileNotFoundException("Restore the approved Weather Manager icon.", IconPath);
            // Padding is packaging only: retain the original artwork and its alpha exactly.
            if (!File.Exists(ForegroundPath) || !File.Exists(BackgroundPath))
            {
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(IconPath))) throw new InvalidOperationException("Cannot decode application icon.");
                var padded = new Texture2D(1536, 1536, TextureFormat.RGBA32, false);
                padded.SetPixels(new Color[1536 * 1536]);
                padded.SetPixels(256, 256, source.width, source.height, source.GetPixels());
                padded.Apply();
                File.WriteAllBytes(ForegroundPath, padded.EncodeToPNG());
                var background = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                background.SetPixels(Enumerable.Repeat(new Color(.075f, .25f, .34f, 1f), 16).ToArray());
                background.Apply();
                File.WriteAllBytes(BackgroundPath, background.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(padded);
                UnityEngine.Object.DestroyImmediate(background);
            }
            var original = Import(IconPath);
            var foreground = Import(ForegroundPath);
            var backgroundTexture = Import(BackgroundPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] {original}, IconKind.Any);
            var slots = new List<string>();
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var icon in icons)
                {
                    if (icon.minLayerCount > 1)
                    {
                        icon.SetTexture(backgroundTexture, 0);
                        icon.SetTexture(foreground, 1);
                    }
                    else icon.SetTexture(original);
                    slots.Add($"{kind}:{icon.width}x{icon.height}:layers={icon.layerCount}");
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            AssetDatabase.SaveAssets();
            Validate();
            Directory.CreateDirectory("Evidence/Revision");
            File.WriteAllText("Evidence/Revision/application-icon.json", JsonUtility.ToJson(new IconReport {source=IconPath, slots=slots.ToArray()}, true));
            Debug.Log("WEATHER_MANAGER_APPLICATION_ICON_PASS:slots=" + slots.Count + ":source=" + IconPath);
        }

        public static void Validate()
        {
            var original = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (original == null || original.width != 1024 || original.height != 1024)
                throw new InvalidOperationException("Approved 1024px application icon is missing.");
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind))
            {
                if (icon.layerCount < icon.minLayerCount) throw new InvalidOperationException("Missing icon layers: " + kind);
                for (var layer=0;layer<icon.minLayerCount;layer++)
                    if (icon.GetTexture(layer) == null || !AssetDatabase.GetAssetPath(icon.GetTexture(layer)).StartsWith(Folder))
                        throw new InvalidOperationException("Android icon slot does not use game artwork: " + kind);
            }
        }

        private static Texture2D Import(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=2048;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        [Serializable] private sealed class IconReport {public string source; public string[] slots;}
    }
}
