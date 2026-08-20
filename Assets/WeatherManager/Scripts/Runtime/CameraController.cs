using UnityEngine;
using UnityEngine.InputSystem;

namespace WeatherManager
{
    public sealed class CameraController : MonoBehaviour
    {
        private Camera controlledCamera;
        private DisasterSimulation simulation;
        private UIController ui;
        private Vector3 target;
        private float yaw;
        private float pitch = 43f;
        private float distance = 78f;
        private float shakeTime;
        private float shakeStrength;

        public void Initialize(Camera camera, DisasterSimulation disasterSimulation, UIController uiController)
        {
            controlledCamera = camera;
            simulation = disasterSimulation;
            ui = uiController;
            ResetView();
        }

        public void Tick(float unscaledDelta)
        {
            var mouse = Mouse.current;
            if (mouse == null || controlledCamera == null)
            {
                return;
            }

            var overUi = ui != null && ui.IsPointerOverUi();
            var delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed && !overUi)
            {
                yaw += delta.x * 0.15f;
                pitch = Mathf.Clamp(pitch - delta.y * 0.13f, 24f, 68f);
            }
            if (mouse.middleButton.isPressed && !overUi)
            {
                var right = controlledCamera.transform.right;
                right.y = 0f;
                right.Normalize();
                var forward = Vector3.Cross(right, Vector3.up);
                var panScale = distance * 0.0016f;
                target += (-right * delta.x - forward * delta.y) * panScale;
                ClampTarget();
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !overUi)
            {
                distance = Mathf.Clamp(distance - scroll * 0.026f, 32f, 112f);
            }

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var position = target + rotation * (Vector3.back * distance);
            if (shakeTime > 0f)
            {
                shakeTime = Mathf.Max(0f, shakeTime - unscaledDelta);
                position += Random.insideUnitSphere * shakeStrength * (shakeTime / 0.35f);
            }
            controlledCamera.transform.position = Vector3.Lerp(
                controlledCamera.transform.position,
                position,
                1f - Mathf.Exp(-unscaledDelta * 14f));
            var desiredRotation = Quaternion.LookRotation(target - controlledCamera.transform.position, Vector3.up);
            controlledCamera.transform.rotation = Quaternion.Slerp(
                controlledCamera.transform.rotation,
                desiredRotation,
                1f - Mathf.Exp(-unscaledDelta * 18f));
        }

        public void ResetView()
        {
            target = Vector3.zero;
            if (simulation != null) target.y = simulation.SampleGroundHeight(target) + 1.2f;
            yaw = 0f;
            pitch = 43f;
            distance = 78f;
            shakeTime = 0f;
            shakeStrength = 0f;
            if (controlledCamera != null)
            {
                var rotation = Quaternion.Euler(pitch, yaw, 0f);
                controlledCamera.transform.position = target + rotation * (Vector3.back * distance);
                controlledCamera.transform.LookAt(target, Vector3.up);
            }
        }

        public void Shake(float strength, float duration)
        {
            shakeStrength = Mathf.Max(shakeStrength, strength);
            shakeTime = Mathf.Max(shakeTime, duration);
        }

        public void FocusOn(Vector3 worldTarget, float viewDistance)
        {
            target = worldTarget;
            target.y = Mathf.Max(0f, worldTarget.y);
            distance = Mathf.Clamp(viewDistance, 32f, 112f);
            yaw = 0f;
            pitch = 48f;
            ClampTarget();
            if (controlledCamera == null) return;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            controlledCamera.transform.position = target + rotation * (Vector3.back * distance);
            controlledCamera.transform.LookAt(target, Vector3.up);
        }

        private void ClampTarget()
        {
            var halfWidth = simulation != null ? simulation.HalfWorldWidth : 52f;
            var halfDepth = simulation != null ? simulation.HalfWorldDepth : 36f;
            target.x = Mathf.Clamp(target.x, -halfWidth * 0.72f, halfWidth * 0.72f);
            target.z = Mathf.Clamp(target.z, -halfDepth * 0.72f, halfDepth * 0.72f);
            target.y = simulation != null ? simulation.SampleGroundHeight(target) + 0.8f : 0f;
        }
    }
}
