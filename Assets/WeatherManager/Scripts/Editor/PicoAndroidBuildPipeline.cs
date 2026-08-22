using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WeatherManager.Editor
{
    /// <summary>Repeatable Android build entry point for PICO emulator/device review.</summary>
    public static class PicoAndroidBuildPipeline
    {
        public const string PackageName = "com.weathermanager.picomr";
        /// <summary>
        /// Deliberately separate from the legacy flat-window smoke build. This
        /// package includes the PICO loader, passthrough, tabletop plane
        /// placement, tracked-controller interaction and world-space UI path.
        /// </summary>
        public const string TrueMrApkPath = "Builds/PICO/WeatherManager-PicoMR-TrueMR.apk";

        public static void BuildPicoReviewApk()
        {
            PicoMrProjectSetup.ConfigureAndroidPicoLoader();
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Project root is unavailable.");
            var outputPath = Path.Combine(projectRoot, TrueMrApkPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            #pragma warning disable CS0618
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageName);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // PICO's Android build processor rejects minSdk below API 29.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            // PICO XR's Unity 6 validation requires the Activity path. The
            // old GameActivity smoke build cannot initialize this loader.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            #pragma warning restore CS0618

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/WeatherManager/Scenes/WeatherManager.unity" },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                // The embedded PICO 3.4 debugger registers a DEVELOPMENT_BUILD
                // callback that dereferences its optional Resources config
                // before the scene loads. Weather Manager does not use that
                // debugger, so ship the review APK with normal player options;
                // our explicit WEATHER_MANAGER_* markers remain available in
                // logcat without enabling script debugging.
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"PICO Android build failed: {report.summary.result}; errors={report.summary.totalErrors}.");
            }

            Debug.Log(
                $"WEATHER_MANAGER_PICO_TRUE_MR_APK_PASS:package={PackageName}:path={outputPath}:bytes={report.summary.totalSize}");
        }
    }
}
