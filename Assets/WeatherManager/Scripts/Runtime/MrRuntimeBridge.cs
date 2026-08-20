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
    }
}
