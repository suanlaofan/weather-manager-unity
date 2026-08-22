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
                if (!IsAndroidBuild)
                {
                    return false;
                }

                var model = SystemInfo.deviceModel ?? string.Empty;
                return model.IndexOf("emulator", StringComparison.OrdinalIgnoreCase) >= 0
                    || model.IndexOf("sdk_", StringComparison.OrdinalIgnoreCase) >= 0
                    || model.IndexOf("built_for", StringComparison.OrdinalIgnoreCase) >= 0
                    // PICO Emulator 0.13 currently reports this Android
                    // model string. Keep it exact rather than broadly matching
                    // "PICO", which would enable host-mouse casts on retail
                    // headsets.
                    || model.IndexOf("SDK Spaceos", StringComparison.OrdinalIgnoreCase) >= 0;
            }
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
