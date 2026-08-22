using System.Collections;
using System.Collections.Generic;
using Unity.XR.PXR;
using UnityEngine;
using UnityEngine.XR;

namespace WeatherManager
{
    /// <summary>
    /// PICO-specific MR presentation for the editable Weather Manager scene.
    /// The simulation continues to use its authored game-space coordinates;
    /// the tabletop root converts those coordinates to a physical, meter-scale
    /// island only after a real HMD session has started.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MrTabletopExperience : MonoBehaviour
    {
        [Header("Baked scene references")]
        [SerializeField] private Transform tabletopAnchor;
        [SerializeField] private Transform tabletopIsland;
        [SerializeField] private Transform spatialDashboard;
        [SerializeField] private Camera hmdCamera;
        [SerializeField] private UIController ui;

        [Header("Physical placement (meters)")]
        [SerializeField, Range(0.01f, 0.04f)] private float tabletopScale = 0.018f;
        [SerializeField] private Vector3 tabletopPosition = new Vector3(0f, 0.72f, 1.35f);
        [SerializeField, Min(0.005f)] private float tabletopSurfaceClearance = 0.015f;
        [SerializeField, Min(0.1f)] private float minimumTableArea = 0.16f;

        [Header("Head-locked dashboard (meters)")]
        [SerializeField] private Vector3 headLockedDashboardLocalPosition = new Vector3(0f, -0.16f, 1.12f);
        // This is a physical meters-per-logical-pixel value, not a mirror-window
        // scale.  Keeping the authored dashboard at 1440x900 means 0.00124 is
        // an exact 2x increase from the previous 0.00062 XR presentation.
        [SerializeField, Range(0.0004f, 0.0025f)] private float headLockedDashboardScale = 0.00124f;

        private InputDevice rightController;
        private InputDevice leftController;
        private int weatherIndex;
        private bool leftAxisReleased = true;
        private bool planeProviderStarted;
        private bool planeProviderRequested;
        private bool tabletopPlacedOnPhysicalPlane;
        private bool emulatorFallbackFollowsHead;
        private RaycastHit[] tabletopRaycastHits;

        public bool IsMrActive { get; private set; }
        public Transform TabletopIsland => tabletopIsland;
        public bool IsPlacedOnPhysicalPlane => tabletopPlacedOnPhysicalPlane;
        public bool AllowsEmulatorMouseCasting => IsMrActive && MrRuntimeBridge.IsPicoEmulator;

        private IEnumerator Start()
        {
            // A normal desktop/editor run must retain the existing orbit camera
            // and Screen Space overlay.  PICO MR begins only after its loader
            // reports an active device, never merely because Android is used.
            if (Application.platform != RuntimePlatform.Android)
            {
                yield break;
            }

            for (var frame = 0; frame < 240 && !XRSettings.isDeviceActive; frame++)
            {
                yield return null;
            }

            if (!XRSettings.isDeviceActive)
            {
                Debug.Log("WEATHER_MANAGER_MR_FALLBACK:no active PICO XR session");
                // The scene authors a neutral HMD origin. Only a confirmed
                // no-XR Android run is allowed to recover the desktop camera.
                FindFirstObjectByType<CameraController>()?.EnableDesktopFallback();
                yield break;
            }

            // Let the loader and TrackedPoseDriver publish at least one HMD
            // pose before deriving the fallback tabletop position. We never
            // overwrite the tracked camera transform here: that would fight
            // the real headset pose and was the source of the off-screen
            // default island in the emulator.
            yield return new WaitForEndOfFrame();
            yield return null;
            ActivateMixedReality();
        }

        private void Update()
        {
            if (!IsMrActive)
            {
                return;
            }

            ReadWeatherSelector();
        }

        public void Configure(Transform island, Transform dashboard, Camera camera, UIController uiController)
        {
            tabletopAnchor = island != null ? island.parent : tabletopAnchor;
            tabletopIsland = island;
            spatialDashboard = dashboard;
            hmdCamera = camera;
            ui = uiController;
        }

        /// <summary>
        /// Returns true when the physical dashboard consumes this controller
        /// ray. Weather casting must never pass through a spatial UI panel.
        /// </summary>
        public bool IsSpatialRayOverDashboard(Ray ray)
        {
            return IsMrActive && ui != null && ui.IsSpatialRayOverDashboard(ray);
        }

        public bool TryGetRightHandCastRay(out Ray ray, out bool pressedThisFrame, out bool isPressed)
        {
            ray = default;
            pressedThisFrame = false;
            isPressed = false;
            if (!IsMrActive)
            {
                return false;
            }

            if (!rightController.isValid)
            {
                rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            }

            if (!rightController.isValid
                || !rightController.TryGetFeatureValue(CommonUsages.devicePosition, out var localPosition)
                || !rightController.TryGetFeatureValue(CommonUsages.deviceRotation, out var localRotation))
            {
                return false;
            }

            var origin = transform.TransformPoint(localPosition);
            var direction = transform.TransformDirection(localRotation * Vector3.forward).normalized;
            ray = new Ray(origin, direction);
            rightController.TryGetFeatureValue(CommonUsages.triggerButton, out isPressed);
            rightController.TryGetFeatureValue(CommonUsages.primaryButton, out var primaryPressed);
            isPressed |= primaryPressed;
            rightController.TryGetFeatureValue(CommonUsages.trigger, out var triggerValue);

            // Pico Emulator can expose the host left button as only the
            // analogue trigger value.  Do not require triggerButton in that
            // route: WeatherCastingController owns the edge calculation, so
            // this value deliberately represents the current held state.
            isPressed |= triggerValue >= 0.55f;
            pressedThisFrame = isPressed;
            return true;
        }

        public bool TryRaycastGameplay(Ray handRay, DisasterSimulation simulation, out Vector3 gameplayPoint)
        {
            gameplayPoint = default;
            if (tabletopIsland == null || simulation == null)
            {
                return false;
            }

            tabletopRaycastHits ??= new RaycastHit[16];
            var hitCount = Physics.RaycastNonAlloc(
                handRay,
                tabletopRaycastHits,
                8f,
                ~0,
                QueryTriggerInteraction.Ignore);
            var closestTabletopHit = -1;
            var closestDistance = float.PositiveInfinity;
            for (var index = 0; index < hitCount; index++)
            {
                var hit = tabletopRaycastHits[index];
                var hitTransform = hit.transform;
                if (hitTransform == null
                    || (hitTransform != tabletopIsland && !hitTransform.IsChildOf(tabletopIsland))
                    || hit.distance >= closestDistance)
                {
                    continue;
                }

                closestTabletopHit = index;
                closestDistance = hit.distance;
            }

            if (closestTabletopHit >= 0)
            {
                gameplayPoint = tabletopIsland.InverseTransformPoint(tabletopRaycastHits[closestTabletopHit].point);
                return simulation.ContainsPoint(gameplayPoint);
            }

            // A tabletop plane is used only if its baked colliders are missed.
            var tabletopPlane = new Plane(tabletopIsland.up, tabletopIsland.position);
            if (!tabletopPlane.Raycast(handRay, out var distance))
            {
                return false;
            }

            gameplayPoint = tabletopIsland.InverseTransformPoint(handRay.GetPoint(distance));
            gameplayPoint.y = simulation.SampleGroundHeight(gameplayPoint) + 0.08f;
            return simulation.ContainsPoint(gameplayPoint);
        }

        /// <summary>
        /// Last-resort review path for the PICO emulator only.  Its desktop
        /// mirror can occasionally report coordinates outside the stereo eye
        /// projection even though the host left button is a valid action.  A
        /// real headset must always use its controller ray, so this method is
        /// intentionally unavailable anywhere except the identified emulator.
        /// </summary>
        public bool TryGetEmulatorFallbackGameplayPoint(
            DisasterSimulation simulation,
            out Vector3 gameplayPoint)
        {
            gameplayPoint = default;
            if (!IsMrActive || !MrRuntimeBridge.IsPicoEmulator || simulation == null)
            {
                return false;
            }

            var center = Vector3.zero;
            center.y = simulation.SampleGroundHeight(center) + 0.08f;
            if (!simulation.ContainsPoint(center))
            {
                return false;
            }

            gameplayPoint = center;
            return true;
        }

        private void ActivateMixedReality()
        {
            IsMrActive = true;
            if (hmdCamera != null)
            {
                MrRuntimeBridge.ConfigureNativeXrView(hmdCamera);
                hmdCamera.clearFlags = CameraClearFlags.SolidColor;
                // The emulator's virtual passthrough compositor can return a
                // blank layer.  It is not evidence of real video see-through,
                // so preserve an opaque Unity sky there and reserve VST for a
                // retail PICO session.
                hmdCamera.backgroundColor = MrRuntimeBridge.IsPicoEmulator
                    ? new Color(0.48f, 0.76f, 0.86f, 1f)
                    : new Color(0f, 0f, 0f, 0f);
            }

            // On real hardware this asks PICO's compositor for video
            // see-through.  The emulator receives the stable, opaque tabletop
            // fallback above so the default view remains inspectable.
            var usePassthrough = !MrRuntimeBridge.IsPicoEmulator;
            PXR_Manager.EnableVideoSeeThrough = usePassthrough;
            PXR_MixedReality.EnableVideoSeeThroughEffect(usePassthrough);

            // A predictable head-front placement makes the experience usable
            // while the headset is still discovering planes. It is replaced as
            // soon as a suitable physical tabletop arrives from PICO Sense.
            PlaceFallbackTabletop();
            PositionHeadLockedDashboard();
            ui?.ConfigureSpatialDashboard(hmdCamera, spatialDashboard, transform, headLockedDashboardScale);
            if (usePassthrough)
            {
                BeginPlaneDetection();
            }
            else
            {
                Debug.Log(
                    $"WEATHER_MANAGER_PICO_EMULATOR_VIEW_READY:tabletop=fallback:"
                    + $"passthrough=False:anchor={tabletopAnchor?.position:F2}:"
                    + $"uiScale={headLockedDashboardScale:0.00000}");
            }
            Debug.Log(
                $"WEATHER_MANAGER_PICO_INPUT_MODE:model={SystemInfo.deviceModel}:"
                + $"emulator={MrRuntimeBridge.IsPicoEmulator}:xr={MrRuntimeBridge.IsXrPresentationActive}:"
                + $"mr={IsMrActive}:mouseFallback={AllowsEmulatorMouseCasting}");
            Debug.Log(
                $"WEATHER_MANAGER_PICO_MR_ACTIVE:passthrough={usePassthrough}:"
                + $"planeDetection={(usePassthrough ? "requested" : "emulator-disabled")}"
                + $":headLockedUi=True:emulator={MrRuntimeBridge.IsPicoEmulator}"
                + $":eyeTexture={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}");
        }

        private void PlaceFallbackTabletop()
        {
            tabletopPlacedOnPhysicalPlane = false;

            // The PICO emulator continues to revise its synthetic HMD pose for
            // several frames after XRSettings becomes active.  We still use
            // the first published pose to choose a comfortable initial view,
            // but the tabletop must remain world-locked afterwards.  Parenting
            // it to the HMD made the complete island rotate and translate with
            // the user's head, which prevented a natural look-around review.
            // Only the dashboard is allowed to follow the HMD.
            if (MrRuntimeBridge.IsPicoEmulator && hmdCamera != null && tabletopAnchor != null)
            {
                var horizontalForward = Vector3.ProjectOnPlane(hmdCamera.transform.forward, Vector3.up);
                if (horizontalForward.sqrMagnitude < 0.0001f)
                {
                    horizontalForward = transform.forward;
                }
                horizontalForward.Normalize();

                // The baked hierarchy owns the tabletop below the MR rig.
                // Explicitly restore that ownership in case a prior runtime
                // session or a hot reload left it under the tracked camera.
                tabletopAnchor.SetParent(transform, true);
                tabletopAnchor.SetPositionAndRotation(
                    hmdCamera.transform.position
                    + horizontalForward * 1.72f
                    + Vector3.down * 0.24f,
                    Quaternion.LookRotation(horizontalForward, Vector3.up));
                emulatorFallbackFollowsHead = false;

                if (tabletopIsland != null)
                {
                    tabletopIsland.localPosition = Vector3.up * tabletopSurfaceClearance;
                    tabletopIsland.localRotation = Quaternion.identity;
                    tabletopIsland.localScale = Vector3.one * tabletopScale;
                }

                PositionHeadLockedDashboard();
                Debug.Log(
                    $"WEATHER_MANAGER_PICO_EMULATOR_TABLETOP_WORLD_LOCKED:"
                    + $"position={tabletopAnchor.position:F2}:forward={horizontalForward:F2}:"
                    + $"scale={tabletopScale:0.000}");
                return;
            }

            emulatorFallbackFollowsHead = false;
            var fallbackPosition = tabletopPosition;
            var fallbackRotation = Quaternion.identity;
            if (hmdCamera != null)
            {
                var headForward = Vector3.ProjectOnPlane(hmdCamera.transform.forward, Vector3.up);
                if (headForward.sqrMagnitude > 0.0001f)
                {
                    headForward.Normalize();
                    var isEmulator = MrRuntimeBridge.IsPicoEmulator;
                    fallbackPosition = hmdCamera.transform.position + headForward * (isEmulator ? 1.45f : 1.35f);
                    // A 45-degree per-eye vertical view could not see the old
                    // 0.84m drop while looking forward.  The simulator fallback
                    // stays 0.38m below eye level (about 15 degrees) so its
                    // first frame always contains the tabletop; true hardware
                    // still uses the natural table-height fallback until PICO
                    // plane detection takes over.
                    fallbackPosition.y = isEmulator
                        ? hmdCamera.transform.position.y - 0.38f
                        : Mathf.Clamp(hmdCamera.transform.position.y - 0.84f, 0.45f, 0.9f);
                    fallbackRotation = Quaternion.LookRotation(headForward, Vector3.up);
                }
            }
            if (tabletopAnchor != null)
            {
                tabletopAnchor.SetPositionAndRotation(fallbackPosition, fallbackRotation);
            }

            if (tabletopIsland != null)
            {
                tabletopIsland.localPosition = Vector3.up * tabletopSurfaceClearance;
                tabletopIsland.localRotation = Quaternion.identity;
                tabletopIsland.localScale = Vector3.one * tabletopScale;
            }

            PositionHeadLockedDashboard();
        }

        private async void BeginPlaneDetection()
        {
            if (planeProviderRequested)
            {
                return;
            }

            planeProviderRequested = true;
            PXR_Manager.PlaneDetectionDataUpdated -= OnPlaneDetectionDataUpdated;
            PXR_Manager.PlaneDetectionDataUpdated += OnPlaneDetectionDataUpdated;
            var result = await PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.PlaneDetection);
            planeProviderStarted = result == PxrResult.SUCCESS;
            Debug.Log($"WEATHER_MANAGER_MR_PLANE_PROVIDER:{result}");
        }

        private void OnPlaneDetectionDataUpdated(List<PxrPlaneData> planes)
        {
            if (!IsMrActive || tabletopAnchor == null || planes == null)
            {
                return;
            }

            // Never let a simulated Sense plane teleport the visible fallback
            // island outside the emulator's initial forward view.  Real PICO
            // hardware continues through the physical-table placement path.
            if (MrRuntimeBridge.IsPicoEmulator)
            {
                return;
            }

            var foundCandidate = false;
            var bestPlane = default(PxrPlaneData);
            var bestScore = float.NegativeInfinity;
            for (var index = 0; index < planes.Count; index++)
            {
                var plane = planes[index];
                if (plane.state == MeshChangeState.Removed)
                {
                    continue;
                }

                // The Integration SDK currently does not consistently fill
                // orientationMode, so inspect the actual reported transform.
                if (Vector3.Dot(plane.rotation * Vector3.up, Vector3.up) < 0.9f)
                {
                    continue;
                }

                var area = Mathf.Max(0f, plane.box2D.extent.width) * Mathf.Max(0f, plane.box2D.extent.height);
                if (area < minimumTableArea)
                {
                    continue;
                }

                // A semantically identified table wins over a larger floor,
                // otherwise use the largest safe upward-facing surface.
                var score = area + (plane.label == PxrSemanticLabel.Table ? 1000f : 0f);
                if (score <= bestScore)
                {
                    continue;
                }

                foundCandidate = true;
                bestPlane = plane;
                bestScore = score;
            }

            if (!foundCandidate)
            {
                return;
            }

            PlaceOnPhysicalPlane(bestPlane, bestScore);
        }

        private void PlaceOnPhysicalPlane(PxrPlaneData plane, float score)
        {
            tabletopPlacedOnPhysicalPlane = true;
            if (emulatorFallbackFollowsHead)
            {
                tabletopAnchor.SetParent(transform, true);
                emulatorFallbackFollowsHead = false;
            }
            tabletopAnchor.SetPositionAndRotation(plane.position, plane.rotation);
            if (tabletopIsland != null)
            {
                tabletopIsland.localPosition = Vector3.up * tabletopSurfaceClearance;
                tabletopIsland.localRotation = Quaternion.identity;
                tabletopIsland.localScale = Vector3.one * tabletopScale;
            }

            PositionHeadLockedDashboard();
            Debug.Log(
                $"WEATHER_MANAGER_MR_TABLE_PLACED:label={plane.label}:area={score:0.00}:"
                + $"position={plane.position:F2}");
        }

        private void PositionHeadLockedDashboard()
        {
            if (spatialDashboard == null || hmdCamera == null)
            {
                return;
            }

            // Parenting to the tracked HMD is intentional: this dashboard is
            // navigation/status UI, not a world prop. Its physical size stays
            // fixed in meters and no mirror-window aspect is involved.
            spatialDashboard.SetParent(hmdCamera.transform, false);
            spatialDashboard.localPosition = headLockedDashboardLocalPosition;
            spatialDashboard.localRotation = Quaternion.identity;
            spatialDashboard.localScale = Vector3.one;
        }

        private void OnDisable()
        {
            PXR_Manager.PlaneDetectionDataUpdated -= OnPlaneDetectionDataUpdated;
            if (planeProviderStarted)
            {
                PXR_MixedReality.StopSenseDataProvider(PxrSenseDataProviderType.PlaneDetection);
                planeProviderStarted = false;
            }
            planeProviderRequested = false;
        }

        private void ReadWeatherSelector()
        {
            if (!leftController.isValid)
            {
                leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            }
            if (!leftController.isValid
                || !leftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis))
            {
                return;
            }

            if (Mathf.Abs(axis.x) < 0.45f)
            {
                leftAxisReleased = true;
                return;
            }
            if (!leftAxisReleased)
            {
                return;
            }

            leftAxisReleased = false;
            weatherIndex = (weatherIndex + (axis.x > 0f ? 1 : 4)) % 5;
            var manager = FindFirstObjectByType<GameManager>();
            manager?.SetWeather((WeatherType)weatherIndex);
        }
    }
}
