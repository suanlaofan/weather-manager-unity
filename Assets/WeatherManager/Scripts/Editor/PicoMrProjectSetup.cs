using Unity.XR.PXR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace WeatherManager.Editor
{
    /// <summary>Creates the Android PICO loader configuration used by the MR scene.</summary>
    public static class PicoMrProjectSetup
    {
        private const string GeneralSettingsPath = "Assets/XRGeneralSettingsPerBuildTarget.asset";
        private const string PicoSettingsPath = "Assets/XR/Settings/PXR_Settings.asset";
        private const string PicoSettingsKey = "Unity.XR.PXR.Settings";

        [MenuItem("Weather Manager/PICO MR/Configure Android PICO Loader")]
        public static void ConfigureAndroidPicoLoader()
        {
            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(GeneralSettingsPath);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, GeneralSettingsPath);
            }

            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (general == null)
            {
                general = ScriptableObject.CreateInstance<XRGeneralSettings>();
                AssetDatabase.AddObjectToAsset(general, perTarget);
                perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, general);
            }
            if (general.Manager == null)
            {
                general.Manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                AssetDatabase.AddObjectToAsset(general.Manager, perTarget);
            }

            var hasPicoLoader = false;
            foreach (var loader in general.Manager.activeLoaders)
            {
                if (loader is PXR_Loader)
                {
                    hasPicoLoader = true;
                    break;
                }
            }
            if (!hasPicoLoader && !XRPackageMetadataStore.AssignLoader(
                    general.Manager, "PXR_Loader", BuildTargetGroup.Android))
            {
                throw new BuildFailedException("PICO XR loader could not be assigned for Android.");
            }

            var pxrSettings = AssetDatabase.LoadAssetAtPath<PXR_Settings>(PicoSettingsPath);
            if (pxrSettings == null)
            {
                pxrSettings = ScriptableObject.CreateInstance<PXR_Settings>();
                System.IO.Directory.CreateDirectory("Assets/XR/Settings");
                AssetDatabase.CreateAsset(pxrSettings, PicoSettingsPath);
            }
            pxrSettings.stereoRenderingModeAndroid = PXR_Settings.StereoRenderingModeAndroid.Multiview;
            pxrSettings.systemDisplayFrequency = PXR_Settings.SystemDisplayFrequency.RefreshRate72;
            EditorBuildSettings.AddConfigObject(PicoSettingsKey, pxrSettings, true);

            var projectSettings = PXR_ProjectSetting.GetProjectConfig();
            projectSettings.videoSeeThrough = true;
            projectSettings.spatialAnchor = true;
            projectSettings.planeDetection = true;
            projectSettings.openMRC = false;
            PXR_ProjectSetting.SaveAssets();

            EditorUtility.SetDirty(general.Manager);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            Debug.Log("WEATHER_MANAGER_PICO_MR_LOADER_READY:loader=PXR_Loader:passthrough=True:anchors=True:planes=True");
        }
    }
}
