using UnityEngine;
using UnityEngine.InputSystem;

namespace WeatherManager
{
    [DefaultExecutionOrder(-100)]
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Editable scene references")]
        [SerializeField] private WorldBuilder world;
        [SerializeField] private DisasterSimulation simulation;
        [SerializeField] private WeatherVfxController vfx;
        [SerializeField] private WeatherAudioController audioController;
        [SerializeField] private UIController ui;
        [SerializeField] private WeatherCastingController casting;
        [SerializeField] private CameraController cameraController;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Light sun;
        [SerializeField] private Light moonFill;
        [SerializeField] private Transform runtimeRoot;
        private float timeScale = 1f;
        private SeasonType season = SeasonType.Spring;
        private DayPhase dayPhase = DayPhase.Day;
        private float dashboardTimer;
        private bool initialized;

        public float TimeScale => timeScale;
        public SeasonType Season => season;
        public DayPhase Phase => dayPhase;
        public SimulationStats Stats => simulation != null ? simulation.Stats : null;

        private void Awake()
        {
            MrRuntimeBridge.ConfigureFramePacing();
            if (HasEditableSceneReferences())
            {
                InitializeBoundScene();
            }
            else
            {
                BuildRuntime();
            }
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            var unscaledDelta = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            HandleGlobalHotkeys();
            // The XR runtime owns the HMD camera pose.  Desktop orbit/pan/zoom
            // remains available in the emulator and desktop fallback only.
            if (!MrRuntimeBridge.IsXrPresentationActive)
            {
                cameraController.Tick(unscaledDelta);
            }
            casting.Tick(unscaledDelta, timeScale, timeScale <= 0f);
            var simulationDelta = unscaledDelta * timeScale;
            simulation.Step(simulationDelta, casting.ActiveCast);
            world.TickVisuals(unscaledDelta, simulationDelta, dayPhase);
            vfx.Tick(unscaledDelta, simulationDelta);
            audioController.Tick(unscaledDelta);
            UpdateLighting(unscaledDelta);

            dashboardTimer -= unscaledDelta;
            if (dashboardTimer <= 0f)
            {
                dashboardTimer = 0.1f;
                ui.Refresh(simulation.Stats, casting.SelectedWeather, timeScale, season, dayPhase);
            }
        }

        public void SetTimeScale(float scale)
        {
            var clamped = Mathf.Clamp(scale, 0f, 2f);
            if (clamped <= 0f && casting != null && casting.IsCasting)
            {
                casting.Cancel(true);
            }
            timeScale = clamped;
            ui?.SetSpeed(timeScale);
            audioController?.PlayUiToggle();
        }

        public void SetDayPhase(DayPhase phase)
        {
            dayPhase = phase;
            world?.SetDayPhase(phase);
            ui?.SetDayPhase(phase);
            audioController?.PlayUiToggle();
        }

        public void SetWeather(WeatherType type)
        {
            casting?.SetWeather(type);
        }

        public void SetSoundEnabled(bool enabled)
        {
            audioController?.SetEnabled(enabled);
        }

        public void ResetWorld()
        {
            casting?.Cancel(true);
            ui?.ResetTransientFeedback();
            simulation?.ResetSimulation();
            cameraController?.ResetView();
            timeScale = 1f;
            dashboardTimer = 0f;
            ui?.SetSpeed(1f);
            if (simulation != null && casting != null)
            {
                ui?.Refresh(simulation.Stats, casting.SelectedWeather, timeScale, season, dayPhase);
            }
            audioController?.PlayReset();
            ui?.ShowToast(LocalizationService.Message("toast.world_restored"), new Color(0.35f, 0.9f, 0.62f));
        }

        public void ConfigureSceneReferences(
            WorldBuilder worldBuilder,
            DisasterSimulation disasterSimulation,
            WeatherCastingController castingController,
            WeatherVfxController weatherVfx,
            WeatherAudioController weatherAudio,
            UIController uiController,
            CameraController sceneCameraController,
            Camera sceneCamera,
            Light sceneSun,
            Light sceneMoonFill,
            Transform sceneRuntimeRoot)
        {
            world = worldBuilder;
            simulation = disasterSimulation;
            casting = castingController;
            vfx = weatherVfx;
            audioController = weatherAudio;
            ui = uiController;
            cameraController = sceneCameraController;
            worldCamera = sceneCamera;
            sun = sceneSun;
            moonFill = sceneMoonFill;
            runtimeRoot = sceneRuntimeRoot;
        }

        private void BuildRuntime()
        {
            runtimeRoot = new GameObject("RuntimeWorld").transform;
            runtimeRoot.SetParent(transform, false);

            worldCamera = CreateCamera(runtimeRoot);
            CreateLighting(runtimeRoot);

            world = runtimeRoot.gameObject.AddComponent<WorldBuilder>();
            world.Build(runtimeRoot);

            vfx = runtimeRoot.gameObject.AddComponent<WeatherVfxController>();
            vfx.Initialize(runtimeRoot, worldCamera);

            audioController = runtimeRoot.gameObject.AddComponent<WeatherAudioController>();
            audioController.Initialize(runtimeRoot, worldCamera.transform);

            simulation = runtimeRoot.gameObject.AddComponent<DisasterSimulation>();
            simulation.Initialize(world, vfx, audioController);

            ui = runtimeRoot.gameObject.AddComponent<UIController>();
            ui.Build(this);

            cameraController = worldCamera.gameObject.AddComponent<CameraController>();
            cameraController.Initialize(worldCamera, simulation, ui);

            casting = runtimeRoot.gameObject.AddComponent<WeatherCastingController>();
            casting.Initialize(worldCamera, simulation, vfx, audioController, ui);

            InitializeBoundScene();
        }

        private bool HasEditableSceneReferences()
        {
            return world != null
                && simulation != null
                && vfx != null
                && audioController != null
                && ui != null
                && casting != null
                && cameraController != null
                && worldCamera != null;
        }

        private void InitializeBoundScene()
        {
            runtimeRoot ??= transform;
            vfx.PrepareBakedScene(vfx.transform, worldCamera);
            audioController.PrepareBakedScene(runtimeRoot, worldCamera.transform);
            ui.PrepareBakedScene(ui.transform, this);
            simulation.Initialize(world, vfx, audioController);
            cameraController.Initialize(worldCamera, simulation, ui);
            casting.Initialize(worldCamera, simulation, vfx, audioController, ui);

            ui.WeatherSelected -= SetWeather;
            ui.SpeedChanged -= SetTimeScale;
            ui.ResetRequested -= ResetWorld;
            ui.SoundChanged -= SetSoundEnabled;
            ui.DayPhaseChanged -= SetDayPhase;
            simulation.DisasterEvent -= OnDisasterEvent;
            ui.WeatherSelected += SetWeather;
            ui.SpeedChanged += SetTimeScale;
            ui.ResetRequested += ResetWorld;
            ui.SoundChanged += SetSoundEnabled;
            ui.DayPhaseChanged += SetDayPhase;
            simulation.DisasterEvent += OnDisasterEvent;

            world.SetDayPhase(dayPhase);
            ui.SetSelectedWeather(WeatherType.Wind);
            ui.SetSpeed(1f);
            ui.Refresh(simulation.Stats, WeatherType.Wind, timeScale, season, dayPhase);
            initialized = true;
        }

        private Camera CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("WorldCamera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 56f, -67f);
            cameraObject.transform.rotation = Quaternion.Euler(43f, 0f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 280f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.48f, 0.76f, 0.86f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private void CreateLighting(Transform parent)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = Quaternion.Euler(46f, -32f, 0f);
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.78f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            RenderSettings.sun = sun;

            var moonObject = new GameObject("MoonFill");
            moonObject.transform.SetParent(parent, false);
            moonObject.transform.rotation = Quaternion.Euler(58f, 145f, 0f);
            moonFill = moonObject.AddComponent<Light>();
            moonFill.type = LightType.Directional;
            moonFill.color = new Color(0.32f, 0.48f, 0.85f);
            moonFill.intensity = 0f;
            moonFill.shadows = LightShadows.None;
        }

        private void UpdateLighting(float delta)
        {
            if (sun == null || moonFill == null || worldCamera == null) return;
            var night = dayPhase == DayPhase.Night;
            var targetSun = night ? 0.09f : 1.25f;
            var targetMoon = night ? 0.52f : 0f;
            sun.intensity = Mathf.Lerp(sun.intensity, targetSun, 1f - Mathf.Exp(-delta * 2.8f));
            moonFill.intensity = Mathf.Lerp(moonFill.intensity, targetMoon, 1f - Mathf.Exp(-delta * 2.8f));
            var targetBackground = night
                ? new Color(0.035f, 0.055f, 0.12f)
                : new Color(0.48f, 0.76f, 0.86f);
            worldCamera.backgroundColor = Color.Lerp(
                worldCamera.backgroundColor,
                targetBackground,
                1f - Mathf.Exp(-delta * 2.4f));
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = night
                ? new Color(0.12f, 0.16f, 0.28f)
                : new Color(0.58f, 0.62f, 0.58f);
        }

        private void HandleGlobalHotkeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                SetTimeScale(timeScale > 0f ? 0f : 1f);
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                ResetWorld();
            }
        }

        private void OnDisasterEvent(LocalizedMessage message, Color color)
        {
            ui?.ShowToast(message, color);
            cameraController?.Shake(0.16f, 0.25f);
        }

        private void OnDestroy()
        {
            if (ui != null)
            {
                ui.WeatherSelected -= SetWeather;
                ui.SpeedChanged -= SetTimeScale;
                ui.ResetRequested -= ResetWorld;
                ui.SoundChanged -= SetSoundEnabled;
                ui.DayPhaseChanged -= SetDayPhase;
            }
            if (simulation != null) simulation.DisasterEvent -= OnDisasterEvent;
        }
    }
}
