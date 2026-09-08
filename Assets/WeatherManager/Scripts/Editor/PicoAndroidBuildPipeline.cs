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
        private const string RevisionQueueKey = "WeatherManager.RevisionBuildQueued";
        private static bool switchingBuildTarget;
        [InitializeOnLoadMethod]
        private static void RestoreRevisionBuildQueue()
        {
            if (SessionState.GetBool(RevisionQueueKey, false)) EditorApplication.update += DispatchRevisionBuild;
        }
        public const string PackageName = "com.weathermanager.picomr";
        /// <summary>
        /// Deliberately separate from the legacy flat-window smoke build. This
        /// package includes the PICO loader, passthrough, tabletop plane
        /// placement, tracked-controller interaction and world-space UI path.
        /// </summary>
        public const string TrueMrApkPath = "Builds/PICO/WeatherManager-PicoMR-TrueMR.apk";

        public static void BuildPicoReviewApk()
        {
            BuildApk(TrueMrApkPath);
        }

        public static void BuildRevisionApk()
        {
            Directory.CreateDirectory("Evidence/Revision");
            File.WriteAllText("Evidence/Revision/build-status.json", "{\"status\":\"building\"}");
            try
            {
                PlayerSettings.bundleVersion = "0.5.0";
                PlayerSettings.Android.bundleVersionCode = Math.Max(5,PlayerSettings.Android.bundleVersionCode);
                var path = "Builds/PICO/WeatherManager-0.5.0-LowPoly-20260908.apk";
                BuildApk(path);
                File.WriteAllText("Evidence/Revision/build-status.json", "{\"status\":\"passed\",\"path\":\""+path+"\"}");
            }
            catch(Exception error)
            {
                File.WriteAllText("Evidence/Revision/build-status.json", UnityEngine.JsonUtility.ToJson(new BuildFailure { status="failed", error=error.ToString() },true));
                Debug.LogException(error);
            }
        }
        public static void QueueRevisionBuild()
        {
            SessionState.SetBool(RevisionQueueKey, true);
            Directory.CreateDirectory("Evidence/Revision");
            File.WriteAllText("Evidence/Revision/build-status.json", "{\"status\":\"queued\"}");
            EditorApplication.update -= DispatchRevisionBuild;
            EditorApplication.update += DispatchRevisionBuild;
        }
        private static void DispatchRevisionBuild()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!switchingBuildTarget)
                {
                    switchingBuildTarget = true;
                    File.WriteAllText("Evidence/Revision/build-status.json", "{\"status\":\"switching_android\"}");
                    if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android))
                    {
                        SessionState.SetBool(RevisionQueueKey, false);
                        EditorApplication.update -= DispatchRevisionBuild;
                        throw new InvalidOperationException("Cannot switch the active build target to Android.");
                    }
                }
                return;
            }
            SessionState.SetBool(RevisionQueueKey, false);
            EditorApplication.update -= DispatchRevisionBuild;
            BuildRevisionApk();
        }
        [Serializable] private sealed class BuildFailure {public string status,error;}

        private static void BuildApk(string apkPath)
        {
            WeatherApplicationIcon.Configure();
            PicoMrProjectSetup.ConfigureAndroidPicoLoader();
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Project root is unavailable.");
            var outputPath = Path.Combine(projectRoot, apkPath);
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
