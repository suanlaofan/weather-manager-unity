using System;
using UnityEngine;
using UnityEngine.XR;

namespace WeatherManager
{
    /// <summary>
    /// Runtime boundary between the desktop game and an XR/PICO presentation.
    /// It deliberately uses Unity's built-in XR signal only: the PICO provider
    /// is selected later after the target headset SDK version is locked.
    /// </summary>
    public static class MrRuntimeBridge
    {
        public static bool IsXrPresentationActive
        {
            get
            {
                // XRSettings is part of UnityEngine.XR. It remains false for
                // desktop/Android fallback builds and becomes true only after
                // an active loader establishes an HMD presentation.
                return XRSettings.isDeviceActive;
            }
        }

        public static bool IsAndroidBuild => Application.platform == RuntimePlatform.Android;

        private static bool? cachedIsPicoEmulator;
        private static string cachedBuildHardware = string.Empty;
        private static string cachedBuildProduct = string.Empty;

        /// <summary>
        /// The PICO emulator reports an Android SDK-style model rather than a
        /// retail headset model.  Keep this narrow: desktop-like mouse input is
        /// useful for simulator review, but must never become a real-headset
        /// casting path.
        /// </summary>
        public static bool IsPicoEmulator
        {
            get
            {
                if (!cachedIsPicoEmulator.HasValue)
                {
                    cachedIsPicoEmulator = DetectPicoEmulator();
                }
                return cachedIsPicoEmulator.Value;
            }
        }

        /// <summary>
        /// android.os.Build.HARDWARE/PRODUCT as read during emulator detection.
        /// Diagnostic only; empty outside Android or if the JNI read failed.
        /// </summary>
        public static string BuildHardware
        {
            get
            {
                _ = IsPicoEmulator;
                return cachedBuildHardware;
            }
        }

        public static string BuildProduct
        {
            get
            {
                _ = IsPicoEmulator;
                return cachedBuildProduct;
            }
        }

        private static bool DetectPicoEmulator()
        {
            if (!IsAndroidBuild)
            {
                return false;
            }

            var model = SystemInfo.deviceModel ?? string.Empty;
            var modelMatches = model.IndexOf("emulator", StringComparison.OrdinalIgnoreCase) >= 0
                || model.IndexOf("sdk_", StringComparison.OrdinalIgnoreCase) >= 0
                || model.IndexOf("built_for", StringComparison.OrdinalIgnoreCase) >= 0
                // PICO Emulator 0.13 currently reports this Android
                // model string. Keep it exact rather than broadly matching
                // "PICO", which would enable host-mouse casts on retail
                // headsets.
                || model.IndexOf("SDK Spaceos", StringComparison.OrdinalIgnoreCase) >= 0;

            // PICO Emulator 6.1 reports the retail-like model
            // "Pico swan_arm64", so the model rules above miss it.  Fall back to
            // the Android build properties of the QEMU image instead
            // (HARDWARE=ranchu, PRODUCT/DEVICE=pico_spaceos_emulator_arm64,
            // FINGERPRINT=.../pico_spaceos_emulator_arm64:...).  Retail headsets
            // never carry these values.  Do not match "swan" or "Pico": those
            // can equal a retail codename and would disable real passthrough.
            var buildMatches = false;
            try
            {
                using (var build = new AndroidJavaClass("android.os.Build"))
                {
                    var hardware = build.GetStatic<string>("HARDWARE") ?? string.Empty;
                    var product = build.GetStatic<string>("PRODUCT") ?? string.Empty;
                    var device = build.GetStatic<string>("DEVICE") ?? string.Empty;
                    var fingerprint = build.GetStatic<string>("FINGERPRINT") ?? string.Empty;
                    cachedBuildHardware = hardware;
                    cachedBuildProduct = product;
                    buildMatches = string.Equals(hardware, "ranchu", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(hardware, "goldfish", StringComparison.OrdinalIgnoreCase)
                        || product.IndexOf("emulator", StringComparison.OrdinalIgnoreCase) >= 0
                        || device.IndexOf("emulator", StringComparison.OrdinalIgnoreCase) >= 0
                        || fingerprint.IndexOf("_emulator", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"WEATHER_MANAGER_PICO_EMULATOR_PROBE_FAILED:{exception.Message}");
                buildMatches = false;
            }

            return modelMatches || buildMatches;
        }

        public static int PreferredFrameRate => IsXrPresentationActive ? -1 : 60;

        public static void ConfigureFramePacing()
        {
            // XR runtimes own display pacing.  Do not force the desktop 120 Hz
            // target while an HMD loader is active.
            if (!IsXrPresentationActive)
            {
                Application.targetFrameRate = 60;
            }
            QualitySettings.vSyncCount = 0;
        }

        /// <summary>
        /// Leave per-eye projection and eye texture sizing to PICO/OpenXR.
        /// This deliberately does not assign Camera.aspect, a target texture,
        /// or a cropped viewport, so the desktop mirror cannot stretch the
        /// headset image.
        /// </summary>
        public static void ConfigureNativeXrView(Camera camera)
        {
            if (camera == null || !IsXrPresentationActive)
            {
                return;
            }

            camera.targetTexture = null;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.ResetStereoProjectionMatrices();
            camera.ResetStereoViewMatrices();
        }
    }
}
