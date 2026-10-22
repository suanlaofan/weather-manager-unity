using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WeatherManager
{
    public sealed class UIController : MonoBehaviour
    {
        private enum ToastSeverity
        {
            Info,
            Warning,
            Danger,
            Recovery
        }

        private const int EventLogCapacity = 4;
        private static readonly Color IdleButtonColor = new Color(0.1f, 0.15f, 0.17f, 1f);
        private static readonly Color HoverButtonColor = new Color(0.2f, 0.3f, 0.34f, 1f);
        private static readonly Color DangerEdgeColor = new Color(1f, 0.16f, 0.08f, 0.82f);
        private static readonly Color WarningEdgeColor = new Color(1f, 0.58f, 0.12f, 0.58f);
        private static readonly Color RecoveryEdgeColor = new Color(0.2f, 0.92f, 0.58f, 0.52f);

        private readonly Dictionary<WeatherType, Button> weatherButtons = new Dictionary<WeatherType, Button>();
        private readonly Dictionary<DayPhase, Button> phaseButtons = new Dictionary<DayPhase, Button>();
        private readonly Dictionary<float, Button> speedButtons = new Dictionary<float, Button>();
        private readonly List<Button> spatialHitTargets = new List<Button>(16);
        private readonly LocalizedMessage[] eventMessages = new LocalizedMessage[EventLogCapacity];
        private readonly ToastSeverity[] eventSeverities = new ToastSeverity[EventLogCapacity];

        [SerializeField] private Font font;
        [SerializeField] private SpatialWeatherDashboard spatialPanel;
        private bool spatialTargetValid = true;
        public SpatialWeatherDashboard SpatialPanel => spatialPanel;
        [SerializeField] private Canvas canvas;
        [SerializeField] private Text currentWeatherText;
        [SerializeField] private Text castStateText;
        [SerializeField] private Text populationText;
        [SerializeField] private Text fireText;
        [SerializeField] private Text floodText;
        [SerializeField] private Text damageText;
        [SerializeField] private Text tornadoText;
        [SerializeField] private Text blackoutText;
        [SerializeField] private Text elapsedText;
        [SerializeField] private Text toastText;
        [SerializeField] private Image toastBackground;
        [SerializeField] private Text eventLogText;
        [SerializeField] private Image[] dangerEdges = new Image[4];
        [SerializeField] private RectTransform weatherToolbar;
        [SerializeField] private RectTransform languageControl;
        [SerializeField] private EventSystem sceneEventSystem;
        [SerializeField] private Text titleText;
        [SerializeField] private Text seasonLabelText;
        [SerializeField] private Text phaseLabelText;
        [SerializeField] private Text statsTitleText;
        [SerializeField] private Text currentLabelText;
        [SerializeField] private Button soundButton;
        [SerializeField] private Text soundButtonLabel;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button chineseButton;
        [SerializeField] private Button englishButton;
        [SerializeField] private Button[] weatherButtonRefs = new Button[5];
        [SerializeField] private Button[] seasonButtonRefs = new Button[4];
        [SerializeField] private Button[] phaseButtonRefs = new Button[2];
        [SerializeField] private Button[] speedButtonRefs = new Button[3];
        private bool soundEnabled = true;
        private Coroutine toastRoutine;
        private WaitForSecondsRealtime toastHold;
        private StringBuilder eventLogBuilder;
        private int eventLogCount;
        private Font localizedRuntimeFont;
        private SimulationStats lastStats;
        private WeatherType lastWeather = WeatherType.Wind;
        private float lastTimeScale = 1f;
        private SeasonType lastSeason = SeasonType.Spring;
        private DayPhase lastPhase = DayPhase.Day;
        private bool lastCasting;
        private bool hasStatsSnapshot;
        private int previousActiveFires;
        private int previousFloodedCells;
        private int previousSnowyCells;
        private int previousDestroyedObjects;
        private bool previousBlackout;

        public event Action<WeatherType> WeatherSelected;
        public event Action<float> SpeedChanged;
        public event Action ResetRequested;
        public event Action<bool> SoundChanged;
        public event Action<DayPhase> DayPhaseChanged;

        public AppLanguage Language => LocalizationService.Current;

        private void OnEnable()
        {
            LocalizationService.LanguageChanged -= HandleLanguageChanged;
            LocalizationService.LanguageChanged += HandleLanguageChanged;
            if (Application.isPlaying)
            {
                CacheNamedReferences();
                CaptureButtonReferences();
                RebuildButtonMaps();
                BindButtonActions();
                ApplyLocalizedFont();
                ApplyLocalizedLabels();
            }
        }

        private void OnDisable()
        {
            LocalizationService.LanguageChanged -= HandleLanguageChanged;
            SetDangerEdges(Color.clear);
            if (toastBackground != null) toastBackground.gameObject.SetActive(false);
            toastRoutine = null;
        }

        public void Build(GameManager manager)
        {
            if (canvas != null || GetComponentInChildren<Canvas>(true) != null)
            {
                PrepareBakedScene(transform, manager);
                return;
            }

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Arial", "Helvetica" }, 18);
            }

            var canvasObject = new GameObject("WeatherManagerUI");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.35f;
            canvasObject.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();
            BuildTopBand(canvasObject.transform);
            BuildWorldControls(canvasObject.transform);
            BuildStatsPanel(canvasObject.transform);
            BuildBottomStatus(canvasObject.transform);
            BuildToast(canvasObject.transform);
            BuildFeedbackSurfaces(canvasObject.transform);
            CaptureButtonReferences();
        }

        public void PrepareBakedScene(Transform uiRoot, GameManager manager)
        {
            if (canvas == null) canvas = GetComponentInChildren<Canvas>(true);
            CacheNamedReferences();
            EnsureLanguageControl();
            CacheNamedReferences();
            CaptureButtonReferences();
            RebuildButtonMaps();
            BindButtonActions();
            EnsureFeedbackSurfaces();
            EnsureEventSystem();
            LayoutTopBarControls();
            ApplyLocalizedFont();
            ApplyLocalizedLabels();
        }

        public SpatialWeatherDashboard BuildEditableSpatialPanel(Transform dashboardRoot)
        {
            if (spatialPanel == null) spatialPanel = dashboardRoot.GetComponentInChildren<SpatialWeatherDashboard>(true);
            if (spatialPanel == null)
            {
                var panel = new GameObject("Spatial Weather Controls");
                panel.transform.SetParent(dashboardRoot, false);
                spatialPanel = panel.AddComponent<SpatialWeatherDashboard>();
            }
            var face = Resources.Load<Font>("WeatherManager/Fonts/NotoSansCJKsc-Regular") ?? font;
            spatialPanel.Build(this, face);
            return spatialPanel;
        }

        public void ConfigureSpatialDashboard(Camera xrCamera, Transform dashboardRoot, Transform xrTrackingOrigin, float metersPerPixel)
        {
            if (canvas == null || xrCamera == null || dashboardRoot == null) return;
            EnsureEventSystem();
            ConfigureSpatialInputModule(xrTrackingOrigin);
            var panel = BuildEditableSpatialPanel(dashboardRoot);
            canvas.gameObject.SetActive(false);
            panel.Configure(xrCamera, xrTrackingOrigin);
            panel.RefreshState(lastStats, lastWeather, lastCasting, spatialTargetValid);
            Debug.Log($"WEATHER_MANAGER_SPATIAL_UI_READY:worldAnchored=True:sizeMeters=0.800x{SpatialWeatherDashboard.LogicalSize.y*.001f:F3}");
        }

        public void SetSpatialTargetValid(bool valid)
        {
            if (spatialTargetValid == valid) return;
            spatialTargetValid = valid;
            spatialPanel?.RefreshState(lastStats, lastWeather, lastCasting, valid);
        }

        /// <summary>
        /// Bakes the PICO tracked-device input route into the editable scene
        /// without changing its desktop overlay presentation. The raycaster is
        /// switched on only when ConfigureSpatialDashboard enters MR.
        /// </summary>
        public void PrepareSpatialInput(Transform xrTrackingOrigin)
        {
            if (canvas == null)
            {
                return;
            }

            var trackedRaycaster = canvas.GetComponent<TrackedDeviceRaycaster>();
            if (trackedRaycaster == null)
            {
                trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceRaycaster>();
            }
            trackedRaycaster.maxDistance = 6f;
            trackedRaycaster.checkFor3DOcclusion = false;
            trackedRaycaster.checkFor2DOcclusion = false;
            trackedRaycaster.enabled = canvas.renderMode == RenderMode.WorldSpace;
            ConfigureSpatialInputModule(xrTrackingOrigin);
        }

        /// <summary>
        /// Lightweight gameplay-side UI occlusion guard. This deliberately
        /// uses the actual baked Canvas plane rather than EventSystem state so
        /// a trigger cannot cast weather through a dashboard button while the
        /// Input System is processing that same controller ray.
        /// </summary>
        public bool IsSpatialRayOverDashboard(Ray ray)
        {
            if (spatialPanel != null && spatialPanel.IsVisible) return spatialPanel.Contains(ray);
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            {
                return false;
            }

            var dashboard = canvas.transform as RectTransform;
            if (dashboard == null)
            {
                return false;
            }

            // Test in canvas-local coordinates. WorldToScreenPoint here would
            // make controller UI hit-testing depend on the desktop mirror
            // projection rather than the actual physical world-space canvas.
            var plane = new Plane(dashboard.forward, dashboard.position);
            if (!plane.Raycast(ray, out var distance) || distance < 0f || distance > 6f)
            {
                return false;
            }

            return DashboardContainsInteractiveButton(ray.GetPoint(distance));
        }

        /// <summary>
        /// Reliable screen-space guard for the PICO emulator's host mouse or
        /// Android touch. Unlike EventSystem state, this has no one-frame
        /// dependency on a spatial pointer module, so a dashboard click can
        /// never leak through into a weather cast.
        /// </summary>
        public bool IsScreenPointerOverDashboard(Vector2 screenPosition)
        {
            if (spatialPanel != null && spatialPanel.IsVisible && spatialPanel.Canvas.worldCamera != null)
                return spatialPanel.Contains(spatialPanel.Canvas.worldCamera.ScreenPointToRay(screenPosition));
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            {
                return false;
            }

            if (canvas.worldCamera == null)
            {
                return false;
            }

            // The simulator still supplies a 2D host pointer. Convert it to
            // the same physical canvas-plane test used by tracked controllers,
            // rather than asking EventSystem for whichever pointer happened to
            // be processed last.
            return IsSpatialRayOverDashboard(canvas.worldCamera.ScreenPointToRay(screenPosition));
        }

        private bool DashboardContainsInteractiveButton(Vector3 worldPoint)
        {
            // A dashboard button must block a cast, but a decorative panel or
            // empty part of the Canvas must not.  The old root-Canvas test made
            // an enlarged head-locked UI consume nearly every tabletop click.
            if (spatialHitTargets.Count == 0)
            {
                RefreshSpatialHitTargets();
            }

            for (var index = 0; index < spatialHitTargets.Count; index++)
            {
                var button = spatialHitTargets[index];
                if (button == null || !button.isActiveAndEnabled || !button.interactable)
                {
                    continue;
                }

                var buttonRect = button.transform as RectTransform;
                if (buttonRect == null)
                {
                    continue;
                }

                var localPoint = buttonRect.InverseTransformPoint(worldPoint);
                if (buttonRect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                {
                    return true;
                }
            }

            return false;
        }

        public void Refresh(
            SimulationStats stats,
            WeatherType weather,
            float timeScale,
            SeasonType season,
            DayPhase phase)
        {
            if (stats == null || currentWeatherText == null)
            {
                return;
            }

            lastStats = stats;
            lastWeather = weather;
            lastTimeScale = timeScale;
            lastSeason = season;
            lastPhase = phase;
            currentWeatherText.text = WeatherPalette.Label(weather);
            populationText.text = LocalizationService.Format(
                "stats.population",
                stats.AliveResidents,
                stats.TotalResidents,
                stats.InjuredResidents,
                stats.DeadResidents);
            fireText.text = LocalizationService.Format("stats.fires", stats.ActiveFires);
            floodText.text = LocalizationService.Format("stats.flood", stats.FloodedPercent);
            damageText.text = LocalizationService.Format("stats.destroyed", stats.DestroyedObjects, stats.TotalDamageables);
            tornadoText.text = LocalizationService.Format("stats.tornadoes", stats.Tornadoes);
            blackoutText.text = LocalizationService.Text(stats.Blackout ? "stats.grid_offline" : "stats.grid_stable");
            if (!hasStatsSnapshot || stats.Blackout != previousBlackout)
            {
                blackoutText.color = stats.Blackout ? new Color(1f, 0.58f, 0.22f) : new Color(0.52f, 0.86f, 0.65f);
            }
            var speed = timeScale <= 0f ? LocalizationService.Text("state.paused") : $"{timeScale:0.#}x";
            elapsedText.text = $"{FormatTime(stats.Elapsed)}   {speed}   {LocalizationService.Text(LocalizationService.Season(season).Key)}   {LocalizationService.Text(LocalizationService.Phase(phase).Key)}";
            UpdateMetricFeedback(stats);
            spatialPanel?.RefreshState(stats, weather, lastCasting, spatialTargetValid);
        }

        public void SetSelectedWeather(WeatherType selected)
        {
            lastWeather = selected;
            spatialPanel?.RefreshState(lastStats, selected, lastCasting, spatialTargetValid);
            foreach (var pair in weatherButtons)
            {
                var selectedButton = pair.Key == selected;
                var colors = pair.Value.colors;
                colors.normalColor = selectedButton
                    ? WithAlpha(WeatherPalette.For(pair.Key), 0.95f)
                    : new Color(0.1f, 0.15f, 0.18f, 0.96f);
                colors.highlightedColor = selectedButton
                    ? WeatherPalette.For(pair.Key)
                    : HoverButtonColor;
                colors.pressedColor = selectedButton
                    ? Color.Lerp(WeatherPalette.For(pair.Key), Color.white, 0.22f)
                    : new Color(0.28f, 0.4f, 0.45f, 1f);
                colors.selectedColor = colors.normalColor;
                pair.Value.colors = colors;
                pair.Value.gameObject.SetActive(true);
                pair.Value.transform.localScale = selectedButton ? new Vector3(1.035f, 1.035f, 1f) : Vector3.one;
                var outline = pair.Value.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.enabled = selectedButton;
                    outline.effectColor = WithAlpha(WeatherPalette.For(pair.Key), 0.9f);
                    outline.effectDistance = new Vector2(2f, -2f);
                }
                pair.Value.targetGraphic?.CrossFadeColor(colors.normalColor, 0.08f, true, true);
            }
            if (currentWeatherText != null)
            {
                currentWeatherText.text = WeatherPalette.Label(selected);
                currentWeatherText.color = WeatherPalette.For(selected);
            }
        }

        public void SetCasting(bool casting)
        {
            lastCasting = casting;
            spatialPanel?.RefreshState(lastStats, lastWeather, casting, spatialTargetValid);
            if (castStateText != null)
            {
                castStateText.text = LocalizationService.Text(casting ? "state.casting" : "state.ready");
                castStateText.color = casting
                    ? new Color(1f, 0.86f, 0.34f)
                    : new Color(0.62f, 0.76f, 0.78f);
            }
        }

        public void SetSpeed(float speed)
        {
            foreach (var pair in speedButtons)
            {
                SetButtonActive(pair.Value, Mathf.Approximately(pair.Key, speed), new Color(0.22f, 0.66f, 0.78f));
            }
        }

        public void SetDayPhase(DayPhase selected)
        {
            foreach (var pair in phaseButtons)
            {
                SetButtonActive(pair.Value, pair.Key == selected, new Color(0.48f, 0.57f, 0.88f));
            }
        }

        public void ShowToast(LocalizedMessage message, Color color)
        {
            if (!message.IsValid || toastBackground == null || toastText == null)
            {
                return;
            }
            spatialPanel?.ShowNotice(message.Resolve());
            var severity = ResolveToastSeverity(message.Key, color);
            AppendEvent(message, severity);
            if (toastRoutine != null)
            {
                StopCoroutine(toastRoutine);
            }
            toastRoutine = StartCoroutine(ToastRoutine(message, color, severity));
        }

        public void ShowToast(string key, Color color, params object[] arguments)
        {
            ShowToast(LocalizationService.Message(key, arguments), color);
        }

        public void ResetTransientFeedback()
        {
            spatialPanel?.ClearFeedback();
            if (toastRoutine != null)
            {
                StopCoroutine(toastRoutine);
                toastRoutine = null;
            }

            Array.Clear(eventMessages, 0, eventMessages.Length);
            Array.Clear(eventSeverities, 0, eventSeverities.Length);
            eventLogCount = 0;
            hasStatsSnapshot = false;
            eventLogBuilder?.Clear();
            if (eventLogText != null)
            {
                eventLogText.text = LocalizationService.Text("label.event_log")
                    + "\n"
                    + LocalizationService.Text("label.no_incidents");
            }

            if (toastText != null)
            {
                toastText.text = string.Empty;
                toastText.color = Color.white;
            }
            if (toastBackground != null)
            {
                toastBackground.color = WithAlpha(toastBackground.color, 0f);
                toastBackground.gameObject.SetActive(false);
            }
            SetDangerEdges(Color.clear);
        }

        public bool IsPointerOverUi()
        {
            if (sceneEventSystem == null)
            {
                EnsureEventSystem();
            }
            return sceneEventSystem != null && sceneEventSystem.IsPointerOverGameObject();
        }

        private void BuildTopBand(Transform parent)
        {
            var band = CreatePanel("TopBand", parent, new Color(0.035f, 0.065f, 0.075f, 0.95f));
            SetRect(
                band.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, 88f),
                new Vector2(0f, 1f),
                Vector2.zero);

            titleText = CreateText("Title", band.transform, LocalizationService.Text("app.title"), 17, TextAnchor.MiddleLeft, Color.white);
            SetRect(titleText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(18f, 0f), new Vector2(142f, 0f), Vector2.zero, Vector2.zero);

            var toolbarObject = new GameObject("WeatherToolbar");
            toolbarObject.transform.SetParent(band.transform, false);
            weatherToolbar = toolbarObject.AddComponent<RectTransform>();
            weatherToolbar.anchorMin = Vector2.zero;
            weatherToolbar.anchorMax = Vector2.one;
            weatherToolbar.offsetMin = new Vector2(166f, 17f);
            weatherToolbar.offsetMax = new Vector2(-252f, -17f);
            var layout = toolbarObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.padding = new RectOffset(2, 2, 0, 0);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var weatherOrder = new[]
            {
                WeatherType.Wind,
                WeatherType.Rain,
                WeatherType.Electric,
                WeatherType.Snow,
                WeatherType.Heat
            };
            for (var index = 0; index < weatherOrder.Length; index++)
            {
                var type = weatherOrder[index];
                var button = CreateButton(
                    WeatherPalette.Label(type),
                    weatherToolbar,
                    () => WeatherSelected?.Invoke(type),
                    new Color(0.1f, 0.15f, 0.18f, 0.96f),
                    14);
                button.gameObject.name = type == WeatherType.Electric ? "LightningButton" : type + "Button";
                var layoutElement = button.gameObject.AddComponent<LayoutElement>();
                layoutElement.minWidth = 46f;
                layoutElement.preferredWidth = type == WeatherType.Electric ? 112f : 96f;
                layoutElement.flexibleWidth = 1f;
                var label = button.GetComponentInChildren<Text>(true);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = 14;
                var outline = button.gameObject.AddComponent<Outline>();
                outline.enabled = false;
                outline.useGraphicAlpha = true;
                weatherButtons[type] = button;
            }

            soundButton = CreateButton("Sound", band.transform, ToggleSound, new Color(0.13f, 0.2f, 0.22f, 1f));
            soundButton.gameObject.name = "SoundButton";
            soundButtonLabel = soundButton.GetComponentInChildren<Text>();
            SetRect(soundButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-94f, 0f), new Vector2(72f, 46f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

            resetButton = CreateButton("Reset", band.transform, () => ResetRequested?.Invoke(), new Color(0.42f, 0.15f, 0.13f, 1f));
            resetButton.gameObject.name = "ResetButton";
            EnsureLanguageControl();
            LayoutTopBarControls();
        }

        private void CaptureButtonReferences()
        {
            weatherButtonRefs = new[]
            {
                FindButton("WindButton"), FindButton("RainButton"), FindButton("LightningButton"),
                FindButton("SnowButton"), FindButton("HeatButton")
            };
            seasonButtonRefs = new[]
            {
                FindButton("SpButton"), FindButton("SuButton"), FindButton("AuButton"), FindButton("WiButton")
            };
            phaseButtonRefs = new[] { FindButton("DayButton"), FindButton("NightButton") };
            speedButtonRefs = new[] { FindButton("||Button"), FindButton("1xButton"), FindButton("2xButton") };
            soundButton ??= FindButton("SoundButton");
            resetButton ??= FindButton("ResetButton");
            chineseButton ??= FindButton("ChineseButton");
            englishButton ??= FindButton("EnglishButton");
            weatherToolbar ??= FindComponent<RectTransform>("WeatherToolbar");
            languageControl ??= FindComponent<RectTransform>("LanguageControl");
            if (soundButton != null && soundButtonLabel == null)
            {
                soundButtonLabel = soundButton.GetComponentInChildren<Text>(true);
            }
            RefreshSpatialHitTargets();
            DisableSeasonControls();
            for (var index = 0; index < weatherButtonRefs.Length; index++)
            {
                var button = weatherButtonRefs[index];
                if (button == null) continue;
                button.gameObject.SetActive(true);
                if (button.GetComponent<Outline>() == null)
                {
                    var outline = button.gameObject.AddComponent<Outline>();
                    outline.enabled = false;
                    outline.useGraphicAlpha = true;
                }
            }
        }

        public void DisableSeasonControls()
        {
            if (seasonLabelText != null)
            {
                seasonLabelText.gameObject.SetActive(false);
            }
            for (var index = 0; index < seasonButtonRefs.Length; index++)
            {
                var button = seasonButtonRefs[index];
                if (button == null) continue;
                button.onClick.RemoveAllListeners();
                button.gameObject.SetActive(false);
            }

            var panel = FindComponent<RectTransform>("WorldControls");
            if (panel != null)
            {
                SetRect(panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -108f), new Vector2(242f, 148f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            }
            if (phaseLabelText != null)
            {
                SetRect(phaseLabelText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -12f), new Vector2(-24f, 24f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            }
            if (phaseButtonRefs.Length > 0 && phaseButtonRefs[0] != null)
            {
                SetRect(phaseButtonRefs[0].GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -42f), new Vector2(102f, 36f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            }
            if (phaseButtonRefs.Length > 1 && phaseButtonRefs[1] != null)
            {
                SetRect(phaseButtonRefs[1].GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(122f, -42f), new Vector2(102f, 36f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            }
        }

        private void RebuildButtonMaps()
        {
            weatherButtons.Clear();
            phaseButtons.Clear();
            speedButtons.Clear();
            var weathers = new[] { WeatherType.Wind, WeatherType.Rain, WeatherType.Electric, WeatherType.Snow, WeatherType.Heat };
            var phases = new[] { DayPhase.Day, DayPhase.Night };
            var speeds = new[] { 0f, 1f, 2f };
            for (var index = 0; index < weathers.Length; index++)
            {
                if (index < weatherButtonRefs.Length && weatherButtonRefs[index] != null)
                    weatherButtons[weathers[index]] = weatherButtonRefs[index];
            }
            for (var index = 0; index < phases.Length; index++)
            {
                if (index < phaseButtonRefs.Length && phaseButtonRefs[index] != null)
                    phaseButtons[phases[index]] = phaseButtonRefs[index];
            }
            for (var index = 0; index < speeds.Length; index++)
            {
                if (index < speedButtonRefs.Length && speedButtonRefs[index] != null)
                    speedButtons[speeds[index]] = speedButtonRefs[index];
            }
        }

        private void RefreshSpatialHitTargets()
        {
            spatialHitTargets.Clear();
            AddSpatialHitTargets(weatherButtonRefs);
            AddSpatialHitTargets(phaseButtonRefs);
            AddSpatialHitTargets(speedButtonRefs);
            AddSpatialHitTarget(soundButton);
            AddSpatialHitTarget(resetButton);
            AddSpatialHitTarget(chineseButton);
            AddSpatialHitTarget(englishButton);
        }

        private void AddSpatialHitTargets(Button[] buttons)
        {
            if (buttons == null)
            {
                return;
            }

            for (var index = 0; index < buttons.Length; index++)
            {
                AddSpatialHitTarget(buttons[index]);
            }
        }

        private void AddSpatialHitTarget(Button button)
        {
            if (button != null && !spatialHitTargets.Contains(button))
            {
                spatialHitTargets.Add(button);
            }
        }

        private void BindButtonActions()
        {
            foreach (var pair in weatherButtons)
            {
                var weather = pair.Key;
                pair.Value.onClick.RemoveAllListeners();
                pair.Value.onClick.AddListener(() => WeatherSelected?.Invoke(weather));
            }
            foreach (var pair in phaseButtons)
            {
                var phase = pair.Key;
                pair.Value.onClick.RemoveAllListeners();
                pair.Value.onClick.AddListener(() => DayPhaseChanged?.Invoke(phase));
            }
            foreach (var pair in speedButtons)
            {
                var speed = pair.Key;
                pair.Value.onClick.RemoveAllListeners();
                pair.Value.onClick.AddListener(() => SpeedChanged?.Invoke(speed));
            }
            if (soundButton != null)
            {
                soundButton.onClick.RemoveAllListeners();
                soundButton.onClick.AddListener(ToggleSound);
            }
            if (resetButton != null)
            {
                resetButton.onClick.RemoveAllListeners();
                resetButton.onClick.AddListener(() => ResetRequested?.Invoke());
            }
            if (chineseButton != null)
            {
                chineseButton.onClick.RemoveAllListeners();
            }
            if (englishButton != null)
            {
                englishButton.onClick.RemoveAllListeners();
                englishButton.onClick.AddListener(() => LocalizationService.SetLanguage(AppLanguage.English));
            }
        }

        private void CacheNamedReferences()
        {
            currentWeatherText ??= FindComponent<Text>("CurrentWeather");
            castStateText ??= FindComponent<Text>("CastState");
            populationText ??= FindComponent<Text>("Population");
            fireText ??= FindComponent<Text>("Fires0");
            floodText ??= FindComponent<Text>("Flood0%");
            damageText ??= FindComponent<Text>("Destroyed0");
            tornadoText ??= FindComponent<Text>("Tornadoes0");
            blackoutText ??= FindComponent<Text>("Gridstable");
            elapsedText ??= FindComponent<Text>("Elapsed");
            toastText ??= FindComponent<Text>("ToastText");
            toastBackground ??= FindComponent<Image>("Toast");
            eventLogText ??= FindComponent<Text>("EventLogText");
            weatherToolbar ??= FindComponent<RectTransform>("WeatherToolbar");
            languageControl ??= FindComponent<RectTransform>("LanguageControl");
            titleText ??= FindComponent<Text>("Title");
            seasonLabelText ??= FindComponent<Text>("SeasonLabel");
            phaseLabelText ??= FindComponent<Text>("PhaseLabel");
            statsTitleText ??= FindComponent<Text>("StatsTitle");
            currentLabelText ??= FindComponent<Text>("WeatherLabel");
            chineseButton ??= FindButton("ChineseButton");
            englishButton ??= FindButton("EnglishButton");
            var dangerRoot = FindComponent<RectTransform>("DangerEdges");
            if (dangerRoot != null)
            {
                dangerEdges = dangerRoot.GetComponentsInChildren<Image>(true);
            }
        }

        private Button FindButton(string objectName)
        {
            return FindComponent<Button>(objectName);
        }

        private T FindComponent<T>(string objectName) where T : Component
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName) return transforms[index].GetComponent<T>();
            }
            return null;
        }

        private void EnsureLanguageControl()
        {
            // The game ships English-only, so the legacy language toggle is never shown.
            languageControl ??= FindComponent<RectTransform>("LanguageControl");
            if (languageControl != null) languageControl.gameObject.SetActive(false);
        }

        private void LayoutTopBarControls()
        {
            if (weatherToolbar != null)
            {
                weatherToolbar.offsetMin = new Vector2(166f, 17f);
                weatherToolbar.offsetMax = new Vector2(-252f, -17f);
            }
            if (resetButton != null)
            {
                SetRect(resetButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(68f, 46f), new Vector2(1f, 0.5f), Vector2.zero);
            }
            if (soundButton != null)
            {
                SetRect(soundButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-92f, 0f), new Vector2(76f, 46f), new Vector2(1f, 0.5f), Vector2.zero);
            }
            if (languageControl != null)
            {
                SetRect(languageControl, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-176f, 0f), new Vector2(76f, 46f), new Vector2(1f, 0.5f), Vector2.zero);
            }
        }

        private void HandleLanguageChanged(AppLanguage language)
        {
            ResetTransientFeedback();
            ApplyLocalizedFont();
            ApplyLocalizedLabels();
            if (Application.isPlaying)
            {
                ShowToast(LocalizationService.Message("toast.language_changed"), new Color(0.28f, 0.72f, 0.88f));
            }
        }

        private void ApplyLocalizedFont()
        {
            if (canvas == null || !Application.isPlaying) return;
            if (localizedRuntimeFont == null || !localizedRuntimeFont.HasCharacter('W'))
            {
                localizedRuntimeFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "PingFang SC", "Arial Unicode MS", "Heiti SC", "Songti SC", "Arial" },
                    20);
            }

            var bundledFont = Resources.Load<Font>("WeatherManager/Fonts/NotoSansCJKsc-Regular");
            var targetFont = bundledFont != null ? bundledFont
                : localizedRuntimeFont != null && localizedRuntimeFont.HasCharacter('W') ? localizedRuntimeFont : font;
            if (targetFont == null) return;
            var texts = canvas.GetComponentsInChildren<Text>(true);
            for (var index = 0; index < texts.Length; index++)
            {
                texts[index].font = targetFont;
                texts[index].supportRichText = true;
            }
        }

        private void ApplyLocalizedLabels()
        {
            if (titleText != null) titleText.text = LocalizationService.Text("app.title");
            if (phaseLabelText != null) phaseLabelText.text = LocalizationService.Text("label.light");
            if (statsTitleText != null) statsTitleText.text = LocalizationService.Text("label.city_status");
            if (currentLabelText != null) currentLabelText.text = LocalizationService.Text("label.current");
            foreach (var pair in weatherButtons)
            {
                SetButtonLabel(pair.Value, WeatherPalette.Label(pair.Key));
            }
            foreach (var pair in phaseButtons)
            {
                SetButtonLabel(pair.Value, LocalizationService.Text(LocalizationService.Phase(pair.Key).Key));
            }
            SetButtonLabel(soundButton, LocalizationService.Text(soundEnabled ? "button.sound" : "button.muted"));
            SetButtonLabel(resetButton, LocalizationService.Text("button.reset"));
            EnsureLanguageControl();
            if (lastStats != null)
            {
                Refresh(lastStats, lastWeather, lastTimeScale, lastSeason, lastPhase);
            }
            else if (currentWeatherText != null)
            {
                currentWeatherText.text = WeatherPalette.Label(lastWeather);
            }
            SetCasting(lastCasting);
            RefreshEventLog();
        }

        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = value;
        }

        private void BuildWorldControls(Transform parent)
        {
            var panel = CreatePanel("WorldControls", parent, new Color(0.04f, 0.075f, 0.085f, 0.9f));
            SetRect(panel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -108f), new Vector2(242f, 148f), new Vector2(0f, 1f), new Vector2(0f, 1f));

            phaseLabelText = CreateText("PhaseLabel", panel.transform, LocalizationService.Text("label.light"), 12, TextAnchor.MiddleLeft, new Color(0.62f, 0.74f, 0.76f));
            SetRect(phaseLabelText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -12f), new Vector2(-24f, 24f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            var day = CreateButton("Day", panel.transform, () => DayPhaseChanged?.Invoke(DayPhase.Day), new Color(0.1f, 0.15f, 0.17f, 1f), 13);
            var night = CreateButton("Night", panel.transform, () => DayPhaseChanged?.Invoke(DayPhase.Night), new Color(0.1f, 0.15f, 0.17f, 1f), 13);
            SetRect(day.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -42f), new Vector2(102f, 36f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SetRect(night.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(122f, -42f), new Vector2(102f, 36f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            phaseButtons[DayPhase.Day] = day;
            phaseButtons[DayPhase.Night] = night;

            var pause = CreateButton("||", panel.transform, () => SpeedChanged?.Invoke(0f), new Color(0.1f, 0.15f, 0.17f, 1f), 15);
            var one = CreateButton("1x", panel.transform, () => SpeedChanged?.Invoke(1f), new Color(0.1f, 0.15f, 0.17f, 1f), 15);
            var two = CreateButton("2x", panel.transform, () => SpeedChanged?.Invoke(2f), new Color(0.1f, 0.15f, 0.17f, 1f), 15);
            SetRect(pause.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 12f), new Vector2(62f, 38f), Vector2.zero, Vector2.zero);
            SetRect(one.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(78f, 12f), new Vector2(70f, 38f), Vector2.zero, Vector2.zero);
            SetRect(two.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(152f, 12f), new Vector2(72f, 38f), Vector2.zero, Vector2.zero);
            speedButtons[0f] = pause;
            speedButtons[1f] = one;
            speedButtons[2f] = two;
        }

        private void BuildStatsPanel(Transform parent)
        {
            var panel = CreatePanel("Stats", parent, new Color(0.035f, 0.065f, 0.075f, 0.92f));
            SetRect(panel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -108f), new Vector2(286f, 324f), Vector2.one, Vector2.one);

            statsTitleText = CreateText("StatsTitle", panel.transform, LocalizationService.Text("label.city_status"), 15, TextAnchor.MiddleLeft, Color.white);
            SetRect(statsTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -14f), new Vector2(-32f, 28f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            populationText = CreateStatText(panel.transform, "Population", 50f, 58f);
            populationText.resizeTextForBestFit = true;
            populationText.resizeTextMinSize = 11;
            populationText.resizeTextMaxSize = 14;
            populationText.verticalOverflow = VerticalWrapMode.Overflow;
            fireText = CreateStatText(panel.transform, "Fires  0", 116f);
            floodText = CreateStatText(panel.transform, "Flood  0%", 153f);
            damageText = CreateStatText(panel.transform, "Destroyed  0", 190f);
            tornadoText = CreateStatText(panel.transform, "Tornadoes  0", 227f);
            blackoutText = CreateStatText(panel.transform, "Grid stable", 264f);
        }

        private void BuildBottomStatus(Transform parent)
        {
            var panel = CreatePanel("BottomStatus", parent, new Color(0.035f, 0.065f, 0.075f, 0.96f));
            SetRect(panel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 68f), Vector2.zero, Vector2.zero);

            currentLabelText = CreateText("WeatherLabel", panel.transform, LocalizationService.Text("label.current"), 11, TextAnchor.MiddleLeft, new Color(0.58f, 0.72f, 0.74f));
            SetRect(currentLabelText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(24f, 0f), new Vector2(72f, 0f), Vector2.zero, Vector2.zero);
            currentWeatherText = CreateText("CurrentWeather", panel.transform, "Wind", 22, TextAnchor.MiddleLeft, WeatherPalette.Wind);
            SetRect(currentWeatherText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(98f, 0f), new Vector2(170f, 0f), Vector2.zero, Vector2.zero);
            castStateText = CreateText("CastState", panel.transform, "READY", 13, TextAnchor.MiddleLeft, new Color(0.62f, 0.76f, 0.78f));
            SetRect(castStateText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(274f, 0f), new Vector2(120f, 0f), Vector2.zero, Vector2.zero);
            elapsedText = CreateText("Elapsed", panel.transform, "00:00   1x", 13, TextAnchor.MiddleRight, new Color(0.68f, 0.78f, 0.8f));
            SetRect(elapsedText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-24f, 0f), new Vector2(430f, 0f), Vector2.one, Vector2.one);
        }

        private void BuildToast(Transform parent)
        {
            toastBackground = CreatePanel("Toast", parent, new Color(0.12f, 0.15f, 0.16f, 0f));
            SetRect(toastBackground.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(440f, 46f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            toastText = CreateText("ToastText", toastBackground.transform, string.Empty, 15, TextAnchor.MiddleCenter, Color.white);
            Stretch(toastText.rectTransform, 10f);
            toastBackground.gameObject.SetActive(false);
        }

        private void BuildFeedbackSurfaces(Transform parent)
        {
            if (eventLogText == null)
            {
                var eventPanel = CreatePanel("EventLog", parent, new Color(0.025f, 0.045f, 0.052f, 0.82f));
                SetRect(
                    eventPanel.rectTransform,
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(-20f, 82f),
                    new Vector2(360f, 112f),
                    new Vector2(1f, 0f),
                    Vector2.one);
                eventLogText = CreateText(
                    "EventLogText",
                    eventPanel.transform,
                    "EVENT LOG\nNo active incidents",
                    12,
                    TextAnchor.UpperLeft,
                    new Color(0.76f, 0.84f, 0.86f));
                eventLogText.lineSpacing = 1.08f;
                Stretch(eventLogText.rectTransform, 12f);
            }

            var dangerRoot = FindComponent<RectTransform>("DangerEdges");
            if (dangerRoot == null)
            {
                var dangerRootObject = new GameObject("DangerEdges");
                dangerRootObject.transform.SetParent(parent, false);
                dangerRoot = dangerRootObject.AddComponent<RectTransform>();
                dangerRoot.anchorMin = Vector2.zero;
                dangerRoot.anchorMax = Vector2.one;
                dangerRoot.offsetMin = Vector2.zero;
                dangerRoot.offsetMax = Vector2.zero;
            }
            dangerRoot.SetAsLastSibling();
            dangerEdges = new[]
            {
                FindComponent<Image>("DangerTop")
                    ?? CreateDangerEdge("DangerTop", dangerRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -10f), Vector2.zero),
                FindComponent<Image>("DangerBottom")
                    ?? CreateDangerEdge("DangerBottom", dangerRoot, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 10f)),
                FindComponent<Image>("DangerLeft")
                    ?? CreateDangerEdge("DangerLeft", dangerRoot, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, 0f)),
                FindComponent<Image>("DangerRight")
                    ?? CreateDangerEdge("DangerRight", dangerRoot, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-10f, 0f), Vector2.zero)
            };
            SetDangerEdges(Color.clear);
        }

        private void EnsureFeedbackSurfaces()
        {
            if (canvas == null) return;
            if (eventLogText == null || !HasValidDangerEdges())
            {
                BuildFeedbackSurfaces(canvas.transform);
            }
        }

        private bool HasValidDangerEdges()
        {
            if (dangerEdges == null || dangerEdges.Length != 4) return false;
            for (var index = 0; index < dangerEdges.Length; index++)
            {
                if (dangerEdges[index] == null) return false;
            }
            return true;
        }

        private static Image CreateDangerEdge(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var edgeObject = new GameObject(name);
            edgeObject.transform.SetParent(parent, false);
            var edge = edgeObject.AddComponent<Image>();
            edge.raycastTarget = false;
            edge.color = Color.clear;
            edge.rectTransform.anchorMin = anchorMin;
            edge.rectTransform.anchorMax = anchorMax;
            edge.rectTransform.offsetMin = offsetMin;
            edge.rectTransform.offsetMax = offsetMax;
            return edge;
        }

        private Text CreateStatText(Transform parent, string value, float top, float height = 34f)
        {
            var text = CreateText(value.Replace(" ", string.Empty), parent, value, 14, TextAnchor.MiddleLeft, new Color(0.82f, 0.88f, 0.89f));
            SetRect(text.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -top), new Vector2(-32f, height), new Vector2(0f, 1f), new Vector2(0f, 1f));
            return text;
        }

        private Button CreateButton(string label, Transform parent, UnityEngine.Events.UnityAction action, Color baseColor, int fontSize = 14)
        {
            var objectRoot = new GameObject(label + "Button");
            objectRoot.transform.SetParent(parent, false);
            var image = objectRoot.AddComponent<Image>();
            image.color = Color.white;
            var button = objectRoot.AddComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(baseColor, Color.white, 0.34f);
            colors.selectedColor = baseColor;
            colors.disabledColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.38f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.onClick.AddListener(action);
            var text = CreateText("Label", objectRoot.transform, label, fontSize, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform, 4f);
            return button;
        }

        private Text CreateText(string objectName, Transform parent, string value, int fontSize, TextAnchor alignment, Color color)
        {
            var objectRoot = new GameObject(objectName);
            objectRoot.transform.SetParent(parent, false);
            var text = objectRoot.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Image CreatePanel(string objectName, Transform parent, Color color)
        {
            var objectRoot = new GameObject(objectName);
            objectRoot.transform.SetParent(parent, false);
            var image = objectRoot.AddComponent<Image>();
            image.color = color;
            // Only Selectable button graphics are intended to receive spatial
            // rays. Background panels should never steal a controller/mouse
            // cast from the world.
            image.raycastTarget = false;
            return image;
        }

        private void EnsureEventSystem()
        {
            var ownerScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
            var allSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            EventSystem keeper = null;
            if (sceneEventSystem != null && sceneEventSystem.gameObject.scene == ownerScene)
            {
                keeper = sceneEventSystem;
            }
            if (keeper == null)
            {
                for (var index = 0; index < allSystems.Length; index++)
                {
                    var candidate = allSystems[index];
                    if (candidate == null || candidate.gameObject.scene != ownerScene) continue;
                    if (keeper == null || (!keeper.isActiveAndEnabled && candidate.isActiveAndEnabled))
                    {
                        keeper = candidate;
                    }
                }
            }

            if (keeper == null)
            {
                var objectRoot = new GameObject("EventSystem");
                SceneManager.MoveGameObjectToScene(objectRoot, ownerScene);
                keeper = objectRoot.AddComponent<EventSystem>();
            }
            sceneEventSystem = keeper;
            keeper.gameObject.SetActive(true);
            keeper.enabled = true;

            for (var index = 0; index < allSystems.Length; index++)
            {
                var duplicate = allSystems[index];
                if (duplicate == null || duplicate == keeper || duplicate.gameObject.scene != ownerScene) continue;
                duplicate.enabled = false;
                duplicate.gameObject.SetActive(false);
                DestroyUiObject(duplicate.gameObject);
            }

            var inputModules = keeper.GetComponents<BaseInputModule>();
            InputSystemUIInputModule inputSystemModule = null;
            for (var index = 0; index < inputModules.Length; index++)
            {
                if (inputModules[index] is InputSystemUIInputModule module && inputSystemModule == null)
                {
                    inputSystemModule = module;
                    continue;
                }
                inputModules[index].enabled = false;
                DestroyUiObject(inputModules[index]);
            }
            if (inputSystemModule == null)
            {
                inputSystemModule = keeper.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            inputSystemModule.enabled = true;
        }

        private void ConfigureSpatialInputModule(Transform xrTrackingOrigin)
        {
            if (sceneEventSystem == null)
            {
                return;
            }

            var module = sceneEventSystem.GetComponent<InputSystemUIInputModule>();
            if (module != null)
            {
                module.xrTrackingOrigin = xrTrackingOrigin;
            }
        }

        private static void DestroyUiObject(UnityEngine.Object target)
        {
            if (target == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(target);
                return;
            }
#endif
            Destroy(target);
        }

        private void AppendEvent(LocalizedMessage message, ToastSeverity severity)
        {
            if (!message.IsValid) return;
            var count = Mathf.Min(eventLogCount, EventLogCapacity - 1);
            for (var index = count; index > 0; index--)
            {
                eventMessages[index] = eventMessages[index - 1];
                eventSeverities[index] = eventSeverities[index - 1];
            }
            eventMessages[0] = message;
            eventSeverities[0] = severity;
            eventLogCount = Mathf.Min(eventLogCount + 1, EventLogCapacity);
            RefreshEventLog();
        }

        private void RefreshEventLog()
        {
            if (eventLogText == null) return;

            eventLogBuilder ??= new StringBuilder(256);
            eventLogBuilder.Clear();
            eventLogBuilder.Append(LocalizationService.Text("label.event_log"));
            if (eventLogCount == 0)
            {
                eventLogBuilder.Append('\n');
                eventLogBuilder.Append(LocalizationService.Text("label.no_incidents"));
                eventLogText.text = eventLogBuilder.ToString();
                return;
            }
            for (var index = 0; index < eventLogCount; index++)
            {
                eventLogBuilder.Append('\n');
                var severityKey = eventSeverities[index] switch
                {
                    ToastSeverity.Danger => "severity.danger",
                    ToastSeverity.Warning => "severity.warning",
                    _ => "severity.info"
                };
                eventLogBuilder.Append('[');
                eventLogBuilder.Append(LocalizationService.Text(severityKey));
                eventLogBuilder.Append("] ");
                eventLogBuilder.Append(eventMessages[index].Resolve());
            }
            eventLogText.text = eventLogBuilder.ToString();
        }

        private static ToastSeverity ResolveToastSeverity(string key, Color color)
        {
            if (Contains(key, "extinguished")
                || Contains(key, "suppressed")
                || Contains(key, "restored"))
            {
                return ToastSeverity.Recovery;
            }
            if (Contains(key, "language_changed"))
            {
                return ToastSeverity.Info;
            }
            if (Contains(key, "collapse")
                || Contains(key, "ignited")
                || Contains(key, "fire_tornado_formed")
                || Contains(key, "blackout"))
            {
                return ToastSeverity.Danger;
            }
            if (Contains(key, "tornado")
                || Contains(key, "fire")
                || Contains(key, "flood")
                || Contains(key, "landslide")
                || Contains(key, "rockfall")
                || Contains(key, "snow_load")
                || Contains(key, "lightning")
                || (color.r > 0.9f && color.g < 0.7f))
            {
                return ToastSeverity.Warning;
            }
            return ToastSeverity.Info;
        }

        private static Color ResolveToastColor(Color requested, ToastSeverity severity)
        {
            return severity switch
            {
                ToastSeverity.Danger => Color.Lerp(requested, new Color(0.72f, 0.08f, 0.035f), 0.52f),
                ToastSeverity.Warning => Color.Lerp(requested, new Color(0.82f, 0.38f, 0.06f), 0.32f),
                ToastSeverity.Recovery => Color.Lerp(requested, new Color(0.1f, 0.7f, 0.43f), 0.45f),
                _ => requested
            };
        }

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrEmpty(source)
                && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetDangerEdges(Color color)
        {
            if (dangerEdges == null) return;
            for (var index = 0; index < dangerEdges.Length; index++)
            {
                if (dangerEdges[index] != null) dangerEdges[index].color = color;
            }
        }

        private void ToggleSound()
        {
            soundEnabled = !soundEnabled;
            if (soundButtonLabel != null)
            {
                soundButtonLabel.text = LocalizationService.Text(soundEnabled ? "button.sound" : "button.muted");
            }
            SetButtonActive(soundButton, soundEnabled, new Color(0.26f, 0.68f, 0.58f));
            SoundChanged?.Invoke(soundEnabled);
        }

        private IEnumerator ToastRoutine(LocalizedMessage message, Color color, ToastSeverity severity)
        {
            toastBackground.gameObject.SetActive(true);
            toastText.text = message.Resolve();
            toastText.color = Color.white;
            var visible = WithAlpha(ResolveToastColor(color, severity), 0.94f);
            var edgeColor = severity switch
            {
                ToastSeverity.Danger => DangerEdgeColor,
                ToastSeverity.Warning => WarningEdgeColor,
                ToastSeverity.Recovery => RecoveryEdgeColor,
                _ => Color.clear
            };
            for (var time = 0f; time < 0.16f; time += Time.unscaledDeltaTime)
            {
                var progress = Mathf.Clamp01(time / 0.16f);
                toastBackground.color = Color.Lerp(WithAlpha(visible, 0f), visible, progress);
                SetDangerEdges(Color.Lerp(Color.clear, edgeColor, progress));
                yield return null;
            }
            toastBackground.color = visible;
            SetDangerEdges(edgeColor);
            toastHold ??= new WaitForSecondsRealtime(1.8f);
            yield return toastHold;
            for (var time = 0f; time < 0.28f; time += Time.unscaledDeltaTime)
            {
                var progress = Mathf.Clamp01(time / 0.28f);
                toastBackground.color = Color.Lerp(visible, WithAlpha(visible, 0f), progress);
                SetDangerEdges(Color.Lerp(edgeColor, Color.clear, progress));
                yield return null;
            }
            SetDangerEdges(Color.clear);
            toastBackground.gameObject.SetActive(false);
            toastRoutine = null;
        }

        private void UpdateMetricFeedback(SimulationStats stats)
        {
            if (hasStatsSnapshot)
            {
                if (previousActiveFires == 0 && stats.ActiveFires > 0)
                {
                    PulseMetric(fireText, new Color(1f, 0.28f, 0.08f));
                }
                else if (previousActiveFires > 0 && stats.ActiveFires == 0)
                {
                    PulseMetric(fireText, RecoveryEdgeColor);
                }
                if (previousFloodedCells < 10 && stats.FloodedCells >= 10)
                {
                    PulseMetric(floodText, new Color(0.28f, 0.72f, 1f));
                }
                else if (previousFloodedCells >= 4 && stats.FloodedCells < 4)
                {
                    PulseMetric(floodText, RecoveryEdgeColor);
                }
                if (previousSnowyCells < 10 && stats.SnowyCells >= 10)
                {
                    PulseMetric(castStateText, new Color(0.75f, 0.92f, 1f));
                }
                else if (previousSnowyCells >= 4 && stats.SnowyCells < 4)
                {
                    PulseMetric(castStateText, RecoveryEdgeColor);
                }
                if (stats.DestroyedObjects > previousDestroyedObjects)
                {
                    PulseMetric(damageText, new Color(1f, 0.34f, 0.12f));
                }
                if (!previousBlackout && stats.Blackout)
                {
                    PulseMetric(blackoutText, DangerEdgeColor);
                }
                else if (previousBlackout && !stats.Blackout)
                {
                    PulseMetric(blackoutText, RecoveryEdgeColor);
                }
            }

            previousActiveFires = stats.ActiveFires;
            previousFloodedCells = stats.FloodedCells;
            previousSnowyCells = stats.SnowyCells;
            previousDestroyedObjects = stats.DestroyedObjects;
            previousBlackout = stats.Blackout;
            hasStatsSnapshot = true;
        }

        private static void PulseMetric(Text target, Color color)
        {
            if (target == null) return;
            target.color = color;
            target.CrossFadeColor(Color.white, 0.72f, true, true);
        }

        private static void SetButtonActive(Button button, bool active, Color activeColor)
        {
            if (button == null) return;
            var colors = button.colors;
            colors.normalColor = active ? activeColor : IdleButtonColor;
            colors.highlightedColor = active ? Color.Lerp(activeColor, Color.white, 0.16f) : HoverButtonColor;
            colors.pressedColor = active
                ? Color.Lerp(activeColor, Color.white, 0.3f)
                : new Color(0.28f, 0.4f, 0.45f, 1f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            button.targetGraphic?.CrossFadeColor(colors.normalColor, 0.08f, true, true);
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Vector2 pivot,
            Vector2 unused)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }

        private static string FormatTime(float seconds)
        {
            var total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
