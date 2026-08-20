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
        public const string ApkPath = "Builds/PICO/WeatherManager-PicoMR.apk";

        public static void BuildPicoReviewApk()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Project root is unavailable.");
            var outputPath = Path.Combine(projectRoot, ApkPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            #pragma warning disable CS0618
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageName);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
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
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"PICO Android build failed: {report.summary.result}; errors={report.summary.totalErrors}.");
            }

            Debug.Log(
                $"WEATHER_MANAGER_PICO_APK_PASS:package={PackageName}:path={outputPath}:bytes={report.summary.totalSize}");
        }
    }
}
