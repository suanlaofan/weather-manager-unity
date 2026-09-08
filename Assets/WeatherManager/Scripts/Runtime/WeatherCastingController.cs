using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace WeatherManager
{
    public sealed class WeatherCastingController : MonoBehaviour
    {
        private enum EmulatorPointerSource
        {
            None,
            Mouse,
            Touch,
            GenericPointer,
            Gamepad
        }

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
        private MrTabletopExperience mrTabletop;
        private bool mrTriggerWasPressed;
        private bool emulatorPointerWasPressed;
        private EmulatorPointerSource emulatorPointerSource;
        private Vector2 lastEmulatorPointerPosition;
        private string activeCastInput = string.Empty;

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
            mrTabletop = FindFirstObjectByType<MrTabletopExperience>();
            hasPointerValidity = false;
            SetWeather(WeatherType.Wind);
        }

        private readonly WeatherPressGate pressGate = new WeatherPressGate();
        private string pointerRoute;

        public void SuspendInput()
        {
            pressGate.RequireRelease();
            Cancel(false);
        }

#if UNITY_EDITOR
        private bool reviewOverride;
        private bool reviewPressed;
        private Vector3 reviewPoint;
        public void SetReviewCast(WeatherType? weather, Vector3 point)
        {
            reviewOverride = weather.HasValue;
            reviewPressed = weather.HasValue;
            reviewPoint = point;
            if (!weather.HasValue) { Cancel(false); return; }
            if (selectedWeather != weather.Value) SetWeather(weather.Value);
        }
#endif

        public void Tick(float unscaledDelta, float simulationScale, bool paused)
        {
#if UNITY_EDITOR
            if (reviewOverride)
            {
                activeCastInput = "EditorReview";
                TickWorldCast(unscaledDelta, simulationScale, paused, reviewPoint, reviewPressed && !casting, reviewPressed);
                return;
            }
#endif
            ReadWeatherHotkeys();
            if (worldCamera == null || simulation == null) { SuspendInput(); return; }
            var pressed = false;
            var overUi = false;
            var valid = false;
            var point = currentCenter;
            string route;
            if (mrTabletop != null && mrTabletop.IsMrActive)
            {
                Ray ray;
                if (mrTabletop.AllowsEmulatorMouseCasting && TryReadEmulatorPointer(unscaledDelta,
                    out var position, out _, out pressed, out var source))
                {
                    route = "emulator-" + source;
                    ray = worldCamera.ScreenPointToRay(position);
                    overUi = ui != null && ui.IsScreenPointerOverDashboard(position);
                }
                else if (mrTabletop.TryGetRightHandCastRay(out ray, out _, out pressed))
                {
                    route = "right-hand-trigger";
                    overUi = mrTabletop.IsSpatialRayOverDashboard(ray);
                }
                else { pointerRoute = null; SuspendInput(); ui?.SetSpatialTargetValid(false); return; }
                valid = !overUi && mrTabletop.TryRaycastGameplay(ray, simulation, out point);
            }
            else
            {
                if (!TryReadCastPointer(unscaledDelta, out var position, out _, out pressed, out overUi))
                { pointerRoute = null; SuspendInput(); return; }
                route = "desktop-pointer";
                valid = !overUi && TryGetWorldPointer(position, out point);
            }
            if (pointerRoute != null && pointerRoute != route) pressGate.RequireRelease();
            pointerRoute = route;
            var allowed = pressGate.Read(pressed, overUi, valid, paused, out var begin);
            ui?.SetSpatialTargetValid(valid);
            if (!allowed)
            {
                if (casting) Cancel(false);
                if (valid && !overUi) vfx?.SetCursor(point, true, selectedWeather, CastRadius);
                else vfx?.SetCursor(currentCenter, false, selectedWeather, CastRadius);
                return;
            }
            if (begin) activeCastInput = route;
            TickWorldCast(unscaledDelta, simulationScale, paused, point, begin, pressed);
        }

        public void SetWeather(WeatherType type)
        {
            if (casting) SuspendInput();

            selectedWeather = type;
            ui?.SetSelectedWeather(type);
            audioController?.PlayUiSelect();
        }

        public void Cancel(bool immediate)
        {
            if (immediate) pressGate.RequireRelease();
            var wasCasting = casting;
            var completedHoldSeconds = holdSeconds;
            var completedInput = activeCastInput;
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
                if (!string.IsNullOrEmpty(completedInput))
                {
                    Debug.Log(
                        $"{CastLogMarker("END", completedInput)}:source={completedInput}:"
                        + $"duration={completedHoldSeconds:0.00}:weather={selectedWeather}");
                }
            }
            activeCastInput = string.Empty;
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
            if (!string.IsNullOrEmpty(activeCastInput))
            {
                Debug.Log(
                    $"{CastLogMarker("BEGIN", activeCastInput)}:source={activeCastInput}:weather={selectedWeather}:"
                    + $"point={point.x:0.00},{point.y:0.00},{point.z:0.00}");
            }
        }

        private void TickWorldCast(
            float unscaledDelta,
            float simulationScale,
            bool paused,
            Vector3 point,
            bool pressedThisFrame,
            bool isPressed)
        {
            UpdatePointerFeedback(true, point);
            if (!casting)
            {
                currentCenter = point;
                vfx?.SetCursor(point, true, selectedWeather, CastRadius);
            }

            if (paused)
            {
                if (casting) Cancel(true);
                return;
            }
            if (pressedThisFrame) Begin(point);
            if (!casting) return;
            if (!isPressed)
            {
                Cancel(false);
                return;
            }

            var simulationDelta = unscaledDelta * simulationScale;
            holdSeconds += simulationDelta;
            previousCenter = currentCenter;
            currentCenter = point;
            var direction = currentCenter - previousCenter;
            direction.y = 0f;
            direction = direction.sqrMagnitude < 0.001f ? currentCast.Direction : direction.normalized;
            currentCast = new WeatherCastState(selectedWeather, currentCenter, direction, CastRadius, holdSeconds, true, castId);
            vfx?.SetCasting(currentCast);
            audioController?.UpdateWeatherPosition(currentCenter);
            if (selectedWeather != WeatherType.Electric) return;
            lightningTimer -= simulationDelta;
            if (lightningTimer > 0f) return;
            lightningTimer += LightningPeriod;
            var jitter = Random.insideUnitCircle * (CastRadius * 0.62f);
            var strike = currentCenter + new Vector3(jitter.x, 0f, jitter.y);
            simulation.TriggerLightning(strike, 0.88f + Mathf.Clamp01(holdSeconds * 0.12f) * 0.28f);
        }

        private void OnDisable()
        {
            pressGate.RequireRelease();
            hasPointerValidity = false;
            Cancel(true);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            pressGate.RequireRelease();
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

        private bool TryReadEmulatorPointer(
            float unscaledDelta,
            out Vector2 screenPosition,
            out bool pressedThisFrame,
            out bool isPressed,
            out string inputSource)
        {
            var mouse = Mouse.current;
            var touchScreen = Touchscreen.current;

            // PICO Emulator can surface a host left click either as a real
            // mouse or as Android's primary touch. Prefer whichever device has
            // an active press; this keeps press-and-hold casting reliable
            // regardless of the emulator's current input routing mode.
            if (mouse != null
                && (mouse.leftButton.isPressed
                    || mouse.leftButton.wasPressedThisFrame
                    || mouse.leftButton.wasReleasedThisFrame
                    || (emulatorPointerWasPressed && emulatorPointerSource == EmulatorPointerSource.Mouse)))
            {
                screenPosition = mouse.position.ReadValue();
                pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
                isPressed = mouse.leftButton.isPressed;
                emulatorPointerSource = EmulatorPointerSource.Mouse;
                lastEmulatorPointerPosition = screenPosition;
                inputSource = "mouse";
                return true;
            }

            if (touchScreen != null)
            {
                var touch = touchScreen.primaryTouch;
                if (touch.press.isPressed
                    || touch.press.wasPressedThisFrame
                    || touch.press.wasReleasedThisFrame
                    || (emulatorPointerWasPressed && emulatorPointerSource == EmulatorPointerSource.Touch))
                {
                    screenPosition = touch.position.ReadValue();
                    pressedThisFrame = touch.press.wasPressedThisFrame;
                    isPressed = touch.press.isPressed;
                    emulatorPointerSource = EmulatorPointerSource.Touch;
                    lastEmulatorPointerPosition = screenPosition;
                    inputSource = "touch";
                    return true;
                }
            }

            // The PICO mirror can expose a host click as a generic Pointer
            // (for example Pen) rather than Mouse or Touchscreen. Mouse and
            // touch are checked first because both derive from Pointer and
            // expose more specific button state.
            var pointer = Pointer.current;
            if (pointer != null && pointer != mouse && pointer != touchScreen
                && (pointer.press.isPressed
                    || pointer.press.wasPressedThisFrame
                    || pointer.press.wasReleasedThisFrame
                    || (emulatorPointerWasPressed && emulatorPointerSource == EmulatorPointerSource.GenericPointer)))
            {
                screenPosition = pointer.position.ReadValue();
                pressedThisFrame = pointer.press.wasPressedThisFrame;
                isPressed = pointer.press.isPressed;
                emulatorPointerSource = EmulatorPointerSource.GenericPointer;
                lastEmulatorPointerPosition = screenPosition;
                inputSource = "generic-pointer";
                return true;
            }

            // Some PICO Emulator configurations route the host left mouse
            // button into the virtual controller trigger rather than a mouse
            // device. Keep it in the MR projection path (screen ray then
            // tabletop / safe centre fallback), not the old desktop pointer
            // path whose coordinates are invalid after the island is scaled.
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
                var gamepadPressed = gamepad.rightTrigger.isPressed || gamepad.buttonSouth.isPressed;
                var gamepadPressedThisFrame = gamepad.rightTrigger.wasPressedThisFrame || gamepad.buttonSouth.wasPressedThisFrame;
                var gamepadReleasedThisFrame = gamepad.rightTrigger.wasReleasedThisFrame || gamepad.buttonSouth.wasReleasedThisFrame;
                if (gamepadPressed
                    || gamepadPressedThisFrame
                    || gamepadReleasedThisFrame
                    || (emulatorPointerWasPressed && emulatorPointerSource == EmulatorPointerSource.Gamepad))
                {
                    screenPosition = controllerCursor;
                    pressedThisFrame = gamepadPressedThisFrame;
                    isPressed = gamepadPressed;
                    emulatorPointerSource = EmulatorPointerSource.Gamepad;
                    lastEmulatorPointerPosition = screenPosition;
                    inputSource = "gamepad-trigger";
                    return true;
                }
            }

            // If an input device disappears while held, pass one synthetic
            // release through TickWorldCast so loops/audio cannot remain live.
            if (emulatorPointerWasPressed)
            {
                screenPosition = lastEmulatorPointerPosition;
                pressedThisFrame = false;
                isPressed = false;
                inputSource = emulatorPointerSource.ToString().ToLowerInvariant();
                return true;
            }

            screenPosition = default;
            pressedThisFrame = false;
            isPressed = false;
            inputSource = string.Empty;
            return false;
        }

        private static string CastLogMarker(string phase, string inputSource)
        {
            return inputSource.StartsWith("emulator-")
                ? $"WEATHER_MANAGER_EMULATOR_CAST_{phase}"
                : $"WEATHER_MANAGER_XR_CAST_{phase}";
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
