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

        [Header("Emulator initial framing (meters)")]
        [SerializeField, Range(1.25f, 2f)] private float emulatorTabletopDistance = 1.42f;
        [SerializeField, Range(0.35f, 0.85f)] private float emulatorTabletopEyeDrop = 0.62f;

        [Header("World anchored dashboard (meters)")]
        [SerializeField, Range(0.9f, 1.6f)] private float dashboardDistance = 1.2f;
        [SerializeField, Range(-0.5f, -0.1f)] private float dashboardHeightOffset = -0.12f;
        private bool dashboardPlaced;
        private bool recallWasPressed;
        private bool editorPreviewActive;

        private InputDevice rightController;
        private InputDevice leftController;
        private int weatherIndex;
        private bool leftAxisReleased = true;
        private bool planeProviderStarted;
        private bool planeProviderRequested;
        private bool tabletopPlacedOnPhysicalPlane;
        private bool emulatorFallbackFollowsHead;
        private RaycastHit[] tabletopRaycastHits;
        private Vector3 viewHomePosition, viewHomeScale, viewDepthDirection, viewTargetPosition;
        private Quaternion viewHomeRotation, viewTargetRotation;
        private float viewScale = 1f, viewYaw, viewDepthOffset, viewHomeDistance;
        private bool viewHomeCaptured, viewAnimating, viewManuallyAdjusted;

        public bool IsMrActive { get; private set; }
        public Transform TabletopIsland => tabletopIsland;
        public bool IsPlacedOnPhysicalPlane => tabletopPlacedOnPhysicalPlane;
        public bool AllowsEmulatorMouseCasting => IsMrActive && (MrRuntimeBridge.IsPicoEmulator || editorPreviewActive);
        public int ViewScalePercent => Mathf.RoundToInt(viewScale * 100);
        public float ViewYaw => viewYaw;
        public float ViewDistance => tabletopIsland != null && hmdCamera != null
            ? Vector3.ProjectOnPlane(tabletopIsland.position - hmdCamera.transform.position, Vector3.up).magnitude : 0f;

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

            // The loader becomes active before the session is focused. On a
            // fresh emulator the permission dialog can keep the camera at
            // (0,0,0) and the eye buffers at 0x0 for several seconds. Anchoring
            // there leaves the island on the floor once tracking starts.
            // Wait for a rendered, settled pose before placing either anchor.
            yield return WaitForInitialXrPose();
            ActivateMixedReality();
        }

        private IEnumerator WaitForInitialXrPose()
        {
            var readySeconds = 0f;
            var stableSeconds = 0f;
            var previousPosition = hmdCamera != null ? hmdCamera.transform.position : Vector3.zero;
            var previousRotation = hmdCamera != null ? hmdCamera.transform.rotation : Quaternion.identity;
            while (true)
            {
                yield return new WaitForEndOfFrame();
                var rendered = XRSettings.isDeviceActive && XRSettings.eyeTextureWidth > 0
                    && XRSettings.eyeTextureHeight > 0 && hmdCamera != null && Application.isFocused;
                if (!rendered)
                {
                    readySeconds = stableSeconds = 0f;
                    continue;
                }
                readySeconds += Mathf.Min(Time.unscaledDeltaTime, .1f);
                var position = hmdCamera.transform.position;
                var rotation = hmdCamera.transform.rotation;
                var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                var tracked = device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && isTracked;
                var stable = Vector3.Distance(previousPosition, position) < .015f
                    && Quaternion.Angle(previousRotation, rotation) < 1.5f;
                stableSeconds = stable ? stableSeconds + Mathf.Min(Time.unscaledDeltaTime, .1f) : 0f;
                previousPosition = position;
                previousRotation = rotation;
                // Allow natural head movement, and providers that publish a
                // valid rendered camera without exposing the isTracked bit.
                if ((tracked && (stableSeconds >= .35f || readySeconds >= 2f)) || readySeconds >= 3f)
                {
                    Debug.Log($"WEATHER_MANAGER_INITIAL_POSE_READY:tracked={tracked}:head={position:F2}:"
                        + $"eyeTexture={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}:wait={readySeconds:F2}");
                    yield break;
                }
            }
        }

        private void Update()
        {
            if (!IsMrActive)
            {
                return;
            }

            ReadDashboardRecall();
            ReadWeatherSelector();
            AnimateTabletopView();
        }

        private void CaptureViewHome()
        {
            if (tabletopIsland == null || hmdCamera == null) return;
            viewHomePosition = tabletopIsland.position;
            viewHomeRotation = tabletopIsland.rotation;
            viewHomeScale = tabletopIsland.localScale;
            var depth = Vector3.ProjectOnPlane(viewHomePosition - hmdCamera.transform.position, Vector3.up);
            viewHomeDistance = depth.magnitude;
            viewDepthDirection = depth.sqrMagnitude > .0001f ? depth.normalized : Vector3.forward;
            viewScale = 1f; viewYaw = viewDepthOffset = 0f;
            viewHomeCaptured = true; viewAnimating = viewManuallyAdjusted = false;
        }

        /// <summary>Adjust only the island. Tracking and the independently anchored dashboard stay untouched.</summary>
        public void AdjustTabletopView(DashboardAction action)
        {
            if (!IsMrActive || !viewHomeCaptured) return;
            switch (action)
            {
                case DashboardAction.Shrink: viewScale = Mathf.Clamp(viewScale - .1f, .5f, 1.6f); break;
                case DashboardAction.Enlarge: viewScale = Mathf.Clamp(viewScale + .1f, .5f, 1.6f); break;
                case DashboardAction.TurnLeft: viewYaw -= 15f; break;
                case DashboardAction.TurnRight: viewYaw += 15f; break;
                case DashboardAction.BringCloser: viewDepthOffset = Mathf.Clamp(viewDepthOffset - .1f, .85f - viewHomeDistance, 3f - viewHomeDistance); break;
                case DashboardAction.MoveFarther: viewDepthOffset = Mathf.Clamp(viewDepthOffset + .1f, .85f - viewHomeDistance, 3f - viewHomeDistance); break;
                case DashboardAction.ResetView: viewScale = 1f; viewYaw = viewDepthOffset = 0f; break;
                default: return;
            }
            FindFirstObjectByType<WeatherCastingController>()?.SuspendInput();
            viewYaw = Mathf.Repeat(viewYaw + 180f, 360f) - 180f;
            viewTargetPosition = viewHomePosition + viewDepthDirection * viewDepthOffset;
            viewTargetRotation = viewHomeRotation * Quaternion.Euler(0, viewYaw, 0);
            viewAnimating = viewManuallyAdjusted = true;
            Debug.Log($"WEATHER_MANAGER_TABLETOP_VIEW:{action}:size={ViewScalePercent}:yaw={viewYaw:0}:distance={viewHomeDistance + viewDepthOffset:F2}");
        }

        private void AnimateTabletopView()
        {
            if (!viewAnimating || tabletopIsland == null) return;
            // Unscaled time keeps the controls available while the simulation is paused.
            var blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            var targetScale = viewHomeScale * viewScale;
            tabletopIsland.position = Vector3.Lerp(tabletopIsland.position, viewTargetPosition, blend);
            tabletopIsland.rotation = Quaternion.Slerp(tabletopIsland.rotation, viewTargetRotation, blend);
            tabletopIsland.localScale = Vector3.Lerp(tabletopIsland.localScale, targetScale, blend);
            if (Vector3.Distance(tabletopIsland.position, viewTargetPosition) < .0001f
                && Quaternion.Angle(tabletopIsland.rotation, viewTargetRotation) < .02f
                && Vector3.Distance(tabletopIsland.localScale, targetScale) < .00001f)
            {
                tabletopIsland.SetPositionAndRotation(viewTargetPosition, viewTargetRotation);
                tabletopIsland.localScale = targetScale;
                viewAnimating = false;
            }
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
                || !rightController.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || !tracked
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
                    ? new Color(0.38f, 0.51f, 0.54f, 1f)
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
            PlaceSpatialDashboardIfNeeded();
            ui?.ConfigureSpatialDashboard(hmdCamera, spatialDashboard, transform, SpatialWeatherDashboard.MetersPerUnit);
            if (usePassthrough)
            {
                BeginPlaneDetection();
            }
            else
            {
                Debug.Log(
                    $"WEATHER_MANAGER_PICO_EMULATOR_VIEW_READY:tabletop=fallback:"
                    + $"passthrough=False:anchor={tabletopAnchor?.position:F2}:"
                    + $"uiScale={SpatialWeatherDashboard.MetersPerUnit:0.00000}");
            }
            Debug.Log(
                $"WEATHER_MANAGER_PICO_INPUT_MODE:model={SystemInfo.deviceModel}:"
                + $"emulator={MrRuntimeBridge.IsPicoEmulator}:xr={MrRuntimeBridge.IsXrPresentationActive}:"
                + $"mr={IsMrActive}:mouseFallback={AllowsEmulatorMouseCasting}");
            Debug.Log(
                $"WEATHER_MANAGER_PICO_MR_ACTIVE:passthrough={usePassthrough}:"
                + $"planeDetection={(usePassthrough ? "requested" : "emulator-disabled")}"
                + $":headLockedUi=False:emulator={MrRuntimeBridge.IsPicoEmulator}"
                + $":eyeTexture={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}");
        }

        private void PlaceFallbackTabletop()
        {
            tabletopPlacedOnPhysicalPlane = false;

            // Initial tracking has settled before reaching this branch. Move
            // the scene closer while retaining enough margin for the complete
            // 104 x 72 island in the emulator's wide eye projection. Neither
            // the scene nor UI follows subsequent head movement.
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
                    + horizontalForward * emulatorTabletopDistance
                    + Vector3.down * emulatorTabletopEyeDrop,
                    Quaternion.LookRotation(horizontalForward, Vector3.up));
                emulatorFallbackFollowsHead = false;

                if (tabletopIsland != null)
                {
                    tabletopIsland.localPosition = Vector3.up * tabletopSurfaceClearance;
                    tabletopIsland.localRotation = Quaternion.identity;
                    tabletopIsland.localScale = Vector3.one * tabletopScale;
                }

                PlaceSpatialDashboardIfNeeded();
                CaptureViewHome();
                Debug.Log(
                    $"WEATHER_MANAGER_PICO_EMULATOR_TABLETOP_WORLD_LOCKED:"
                    + $"position={tabletopAnchor.position:F2}:forward={horizontalForward:F2}:"
                    + $"scale={tabletopScale:0.000}:head={hmdCamera.transform.position:F2}:"
                    + $"distance={emulatorTabletopDistance:F2}:eyeDrop={emulatorTabletopEyeDrop:F2}");
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

            PlaceSpatialDashboardIfNeeded();
            CaptureViewHome();
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
            if (!IsMrActive || tabletopAnchor == null || planes == null || tabletopPlacedOnPhysicalPlane || viewManuallyAdjusted)
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

            PlaceSpatialDashboardIfNeeded();
            CaptureViewHome();
            Debug.Log(
                $"WEATHER_MANAGER_MR_TABLE_PLACED:label={plane.label}:area={score:0.00}:"
                + $"position={plane.position:F2}");
        }

        private void PlaceSpatialDashboardIfNeeded()
        {
            if (dashboardPlaced || spatialDashboard == null || hmdCamera == null) return;
            PlaceSpatialDashboard();
        }

        private void PlaceSpatialDashboard()
        {
            var forward = Vector3.ProjectOnPlane(hmdCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            var position = hmdCamera.transform.position + forward * dashboardDistance + Vector3.up * dashboardHeightOffset;
            Physics.SyncTransforms();
            // Placement is the only time occlusion avoidance runs. Head motion never drives this anchor.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var pose = Quaternion.LookRotation(position - hmdCamera.transform.position, Vector3.up);
                if (!DashboardObscured(position, pose)) break;
                position += Vector3.up * .06f;
            }
            spatialDashboard.SetParent(transform, true);
            spatialDashboard.SetPositionAndRotation(position, Quaternion.LookRotation(position - hmdCamera.transform.position, Vector3.up));
            var parentScale = transform.lossyScale;
            spatialDashboard.localScale = new Vector3(1f / Mathf.Max(.0001f, parentScale.x),
                1f / Mathf.Max(.0001f, parentScale.y), 1f / Mathf.Max(.0001f, parentScale.z));
            dashboardPlaced = true;
        }

        private bool DashboardObscured(Vector3 center, Quaternion rotation)
        {
            if (tabletopIsland == null || hmdCamera == null) return false;
            tabletopRaycastHits ??= new RaycastHit[32];
            var visibleGeometry = tabletopIsland.GetComponentsInChildren<MeshRenderer>();
            for (var x = -1; x <= 1; x++) for (var y = -1; y <= 1; y++)
            {
                var target = center + rotation * new Vector3(x * .39f, y * SpatialWeatherDashboard.LogicalSize.y * .0005f, 0);
                var direction = target - hmdCamera.transform.position;
                var count = Physics.RaycastNonAlloc(new Ray(hmdCamera.transform.position, direction.normalized),
                    tabletopRaycastHits, direction.magnitude - .015f, ~(1 << 5), QueryTriggerInteraction.Ignore);
                for (var i = 0; i < count; i++)
                    if (tabletopRaycastHits[i].transform.IsChildOf(tabletopIsland)) return true;
                // Some authored scenery has no collider. Its bounds still prevent an initial UI overlap.
                var ray = new Ray(hmdCamera.transform.position, direction.normalized);
                foreach (var mesh in visibleGeometry)
                    if (mesh.enabled && mesh.bounds.IntersectRay(ray, out var distance) && distance > 0 && distance < direction.magnitude - .015f) return true;
            }
            return false;
        }

        public void RecallDashboard()
        {
            if (spatialDashboard == null || hmdCamera == null || (ui != null && ui.SpatialPanel != null && ui.SpatialPanel.IsDragging)) return;
            FindFirstObjectByType<WeatherCastingController>()?.SuspendInput();
            PlaceSpatialDashboard();
        }

        private void ReadDashboardRecall()
        {
            if (!leftController.isValid) leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var pressed = leftController.isValid && leftController.TryGetFeatureValue(CommonUsages.primaryButton, out var down) && down;
            if (pressed && !recallWasPressed) RecallDashboard();
            recallWasPressed = pressed;
        }

#if UNITY_EDITOR
        public void BeginEditorPreview()
        {
            editorPreviewActive = true;
            IsMrActive = true;
            dashboardPlaced = false;
            var desktop = FindFirstObjectByType<CameraController>();
            if (desktop != null) desktop.enabled = false;
            PlaceFallbackTabletop();
            ui?.ConfigureSpatialDashboard(hmdCamera, spatialDashboard, transform, SpatialWeatherDashboard.MetersPerUnit);
        }
#endif

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
