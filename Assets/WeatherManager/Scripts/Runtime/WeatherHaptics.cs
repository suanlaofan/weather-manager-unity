using UnityEngine;
using UnityEngine.XR;

namespace WeatherManager
{
    /// <summary>Short event feedback, bounded across simultaneous disaster events.</summary>
    public static class WeatherHaptics
    {
        private static float nextPulse;
        public static void Pulse(float amplitude, float seconds)
        {
            if (!MrRuntimeBridge.IsXrPresentationActive || AudioListener.pause || Time.unscaledTime < nextPulse) return;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!device.isValid || !device.TryGetHapticCapabilities(out var capabilities) || !capabilities.supportsImpulse) return;
            device.SendHapticImpulse(0, Mathf.Clamp(amplitude, 0, .35f), Mathf.Clamp(seconds, .02f, .15f));
            nextPulse = Time.unscaledTime + .35f;
        }
        public static void Reset()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid) device.StopHaptics();
            nextPulse = 0;
        }
    }
}
