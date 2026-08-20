using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace WeatherManager
{
    public sealed class WeatherCastingController : MonoBehaviour
    {
        private const float CastRadius = 5.2f;
        private const float LightningPeriod = 0.52f;

        private Camera worldCamera;
        private DisasterSimulation simulation;
        private WeatherVfxController vfx;
        private WeatherAudioController audioController;
        private UIController ui;
        private WeatherType selectedWeather = WeatherType.Wind;
        private WeatherCastState currentCast;
        private bool casting;
        private float holdSeconds;
        private float lightningTimer;
        private int castId;
        private Vector3 previousCenter;
        private Vector3 currentCenter;
        private bool hasPointerValidity;
        private bool lastPointerValid;
        // Keeps an Input System gamepad/controller usable in the Android/PICO
        // flat fallback.  A real XR provider may expose its controller as a
        // Gamepad; that is useful for review, but is not a replacement for
        // OpenXR tracked-ray interaction.
        private Vector2 controllerCursor;
        private bool controllerCursorInitialized;

        public WeatherType SelectedWeather => selectedWeather;
        public bool IsCasting => casting;
        public WeatherCastState? ActiveCast => casting ? currentCast : null;
        public float Radius => CastRadius;

        public void Initialize(
            Camera camera,
            DisasterSimulation disasterSimulation,
            WeatherVfxController weatherVfx,
            WeatherAudioController weatherAudio,
            UIController uiController)
        {
            worldCamera = camera;
            simulation = disasterSimulation;
            vfx = weatherVfx;
            audioController = weatherAudio;
            ui = uiController;
            hasPointerValidity = false;
            SetWeather(WeatherType.Wind);
        }

        public void Tick(float unscaledDelta, float simulationScale, bool paused)
        {
            ReadWeatherHotkeys();
            if (worldCamera == null || simulation == null)
            {
                hasPointerValidity = false;
                if (casting)
                {
                    Cancel(true);
                }
                return;
            }

            if (!TryReadCastPointer(
                    unscaledDelta,
                    out var screenPosition,
                    out var pressedThisFrame,
                    out var isPressed,
                    out var pointerOverUi))
            {
                hasPointerValidity = false;
                if (casting) Cancel(true);
                return;
            }
            var pointerValid = TryGetWorldPointer(screenPosition, out var point);
            if (!pointerValid)
            {
                hasPointerValidity = false;
                if (casting)
                {
                    Cancel(true);
                }
                return;
            }

            UpdatePointerFeedback(pointerValid, point);
            if (!casting)
            {
                currentCenter = point;
                vfx?.SetCursor(point, pointerValid, selectedWeather, CastRadius);
            }

            if (paused)
            {
                if (casting)
                {
                    Cancel(true);
                }
                return;
            }

            if (pressedThisFrame && !pointerOverUi)
            {
                if (pointerValid)
                {
                    Begin(point);
                }
                else
                {
                    audioController?.PlayInvalidTarget();
                }
            }

            if (casting)
            {
                if (!isPressed)
                {
                    Cancel(false);
                    return;
                }

                var simulationDelta = unscaledDelta * simulationScale;
                holdSeconds += simulationDelta;
                previousCenter = currentCenter;
                if (pointerValid)
                {
                    currentCenter = point;
                }

                var direction = currentCenter - previousCenter;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = currentCast.Direction;
                }
                else
                {
                    direction.Normalize();
                }

                currentCast = new WeatherCastState(
                    selectedWeather,
                    currentCenter,
                    direction,
                    CastRadius,
                    holdSeconds,
                    true,
                    castId);
                vfx?.SetCasting(currentCast);
                audioController?.UpdateWeatherPosition(currentCenter);

                if (selectedWeather == WeatherType.Electric)
                {
                    lightningTimer -= simulationDelta;
                    if (lightningTimer <= 0f)
                    {
                        lightningTimer += LightningPeriod;
                        var jitter = Random.insideUnitCircle * (CastRadius * 0.62f);
                        var strike = currentCenter + new Vector3(jitter.x, 0f, jitter.y);
                        simulation.TriggerLightning(strike, 0.88f + Mathf.Clamp01(holdSeconds * 0.12f) * 0.28f);
                    }
                }
            }

        }

        public void SetWeather(WeatherType type)
        {
            if (casting)
            {
                Cancel(true);
            }

            selectedWeather = type;
            ui?.SetSelectedWeather(type);
            audioController?.PlayUiSelect();
        }

        public void Cancel(bool immediate)
        {
            var wasCasting = casting;
            casting = false;
            holdSeconds = 0f;
            lightningTimer = 0f;
            currentCast.Active = false;
            vfx?.SetCasting(null, immediate);
            audioController?.EndWeather(immediate);
            ui?.SetCasting(false);

            if (wasCasting)
            {
                previousCenter = currentCenter;
            }
        }

        private void Begin(Vector3 point)
        {
            casting = true;
            holdSeconds = 0f;
            lightningTimer = selectedWeather == WeatherType.Electric ? 0.18f : LightningPeriod;
            castId++;
            currentCenter = point;
            previousCenter = point - Vector3.forward * 0.01f;
            currentCast = new WeatherCastState(
                selectedWeather,
                currentCenter,
                Vector3.forward,
                CastRadius,
                0f,
                true,
                castId);
            vfx?.SetCasting(currentCast);
            audioController?.BeginWeather(selectedWeather, point);
            ui?.SetCasting(true);
        }

        private void OnDisable()
        {
            hasPointerValidity = false;
            Cancel(true);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                hasPointerValidity = false;
                return;
            }

            hasPointerValidity = false;
            Cancel(true);
        }

        private void UpdatePointerFeedback(bool pointerValid, Vector3 point)
        {
            if (!hasPointerValidity)
            {
                hasPointerValidity = true;
                lastPointerValid = pointerValid;
                return;
            }

            if (lastPointerValid == pointerValid)
            {
                return;
            }

            lastPointerValid = pointerValid;
            audioController?.PlayTargetFeedback(pointerValid, point);
        }

        private void ReadWeatherHotkeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame) SetWeather(WeatherType.Wind);
            if (keyboard.digit2Key.wasPressedThisFrame) SetWeather(WeatherType.Rain);
            if (keyboard.digit3Key.wasPressedThisFrame) SetWeather(WeatherType.Electric);
            if (keyboard.digit4Key.wasPressedThisFrame) SetWeather(WeatherType.Snow);
            if (keyboard.digit5Key.wasPressedThisFrame) SetWeather(WeatherType.Heat);
        }

        private bool TryReadCastPointer(
            float unscaledDelta,
            out Vector2 screenPosition,
            out bool pressedThisFrame,
            out bool isPressed,
            out bool pointerOverUi)
        {
            // Android touch is intentionally handled before mouse so the
            // review APK works without a desktop cursor.
            var touchScreen = Touchscreen.current;
            if (touchScreen != null && touchScreen.primaryTouch.press.isPressed)
            {
                var touch = touchScreen.primaryTouch;
                screenPosition = touch.position.ReadValue();
                pressedThisFrame = touch.press.wasPressedThisFrame;
                isPressed = true;
                pointerOverUi = IsPointerOverUi(touch.touchId.ReadValue());
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                screenPosition = mouse.position.ReadValue();
                pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
                isPressed = mouse.leftButton.isPressed;
                pointerOverUi = IsPointerOverUi();
                return true;
            }

            // PICO controllers commonly surface through Input System as a
            // Gamepad in the non-XR emulator path.  Right-stick aims a virtual
            // cursor; trigger/A starts or maintains a cast.  It is a safe
            // fallback and does not claim tracked-controller support.
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (!controllerCursorInitialized)
                {
                    controllerCursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    controllerCursorInitialized = true;
                }

                var stick = gamepad.rightStick.ReadValue();
                controllerCursor += stick * (Mathf.Max(Screen.width, Screen.height) * 0.75f * unscaledDelta);
                controllerCursor.x = Mathf.Clamp(controllerCursor.x, 0f, Mathf.Max(0f, Screen.width - 1f));
                controllerCursor.y = Mathf.Clamp(controllerCursor.y, 0f, Mathf.Max(0f, Screen.height - 1f));
                screenPosition = controllerCursor;
                pressedThisFrame = gamepad.rightTrigger.wasPressedThisFrame || gamepad.buttonSouth.wasPressedThisFrame;
                isPressed = gamepad.rightTrigger.isPressed || gamepad.buttonSouth.isPressed;
                pointerOverUi = false;
                return true;
            }

            screenPosition = default;
            pressedThisFrame = false;
            isPressed = false;
            pointerOverUi = false;
            return false;
        }

        private bool TryGetWorldPointer(Vector2 screenPosition, out Vector3 point)
        {
            var ray = worldCamera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out var hit, 320f, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return simulation.ContainsPoint(point);
            }

            var plane = new Plane(Vector3.up, new Vector3(0f, 0.08f, 0f));
            if (plane.Raycast(ray, out var distance))
            {
                point = ray.GetPoint(distance);
                point.y = simulation.SampleGroundHeight(point) + 0.08f;
                return simulation.ContainsPoint(point);
            }

            point = Vector3.zero;
            return false;
        }

        private static bool IsPointerOverUi(int pointerId = -1)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
        }
    }
}
