using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WeatherManager
{
    public enum DashboardAction
    {
        Wind, Rain, Electric, Snow, Heat, Pause, Details, Settings, Home,
        Day, Night, NormalSpeed, DoubleSpeed, Sound, Language, Reset,
        ConfirmReset, Recall, Events, PreviousEvent, NextEvent, ResetView,
        Shrink, Enlarge, TurnLeft, TurnRight, BringCloser, MoveFarther
    }

    /// <summary>Meter-sized, editable spatial UI, independent of the tabletop model's scale.</summary>
    [DisallowMultipleComponent]
    public sealed class SpatialWeatherDashboard : MonoBehaviour
    {
        public static readonly Vector2 LogicalSize = new Vector2(800f, 416f);
        public const float MetersPerUnit = 0.001f;
        [SerializeField] private Canvas spatialCanvas;
        [SerializeField] private Font readableFont;
        [SerializeField] private UIController owner;
        [SerializeField] private RectTransform mainPage;
        [SerializeField] private RectTransform detailsPage;
        [SerializeField] private RectTransform settingsPage;
        [SerializeField] private RectTransform resetPage;
        [SerializeField] private RectTransform eventPage;
        [SerializeField] private Text heading;
        [SerializeField] private Text metrics;
        [SerializeField] private Text current;
        [SerializeField] private Text notice;
        [SerializeField] private Text details;
        [SerializeField] private Text eventText;
        [SerializeField] private Text resetText;
        [SerializeField] private Text sizeReadout;
        [SerializeField] private Text rotationReadout;
        [SerializeField] private Text distanceReadout;
        [SerializeField] private SpatialDashboardButton[] actions = Array.Empty<SpatialDashboardButton>();
        private readonly string[] recentEvents = new string[8];
        private int eventCount;
        private int eventIndex;
        private int page;
        private float noticeUntil;
        private WeatherType selected;
        private bool isCasting;
        private bool targetValid = true;
        private bool soundOn = true;
        private GameManager manager;
        private SimulationStats lastStats;
        private Sprite[] iconSprites;
        private Material foreground;
        private MrTabletopExperience tabletop;
        public Canvas Canvas => spatialCanvas;
        public bool IsVisible => spatialCanvas != null && spatialCanvas.enabled && gameObject.activeInHierarchy;
        public bool IsDragging { get; internal set; }
        public UIController Owner => owner;

        private bool Chinese => LocalizationService.Current == AppLanguage.Chinese;
        private string L(string zh, string en) => Chinese ? zh : en;

        private void OnEnable()
        {
            LocalizationService.LanguageChanged += LanguageChanged;
            BindButtons();
        }
        private void OnDisable()
        {
            LocalizationService.LanguageChanged -= LanguageChanged;
            IsDragging = false;
        }
        private void LanguageChanged(AppLanguage _)
        {
            eventCount = 0;
            noticeUntil = 0;
            RefreshLabels();
            if (lastStats != null) RefreshState(lastStats, selected, isCasting, targetValid);
        }

        public void Build(UIController ui, Font font)
        {
            owner = ui;
            readableFont = font;
            if (spatialCanvas != null) { BindButtons(); return; }
            iconSprites=Resources.LoadAll<Sprite>("WeatherManager/UI/NanoBananaIcons").OrderBy(s=>s.name).ToArray();
            foreground=Resources.Load<Material>("WeatherManager/UI/SpatialUiForeground");
            var canvasObject = new GameObject("Spatial Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(transform, false);
            spatialCanvas = canvasObject.GetComponent<Canvas>();
            spatialCanvas.renderMode = RenderMode.WorldSpace;
            spatialCanvas.sortingOrder = 0;
            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = LogicalSize;
            rect.localScale = Vector3.one * MetersPerUnit;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 2f;
            canvasObject.AddComponent<GraphicRaycaster>();
            var tracked = canvasObject.AddComponent<TrackedDeviceRaycaster>();
            tracked.maxDistance = 6f;
            tracked.checkFor3DOcclusion = false;
            tracked.checkFor2DOcclusion = false;
            Panel("Soft frame", rect, new Vector2(0,-4), LogicalSize+new Vector2(8,8), new Color(.08f,.22f,.20f,1));
            Panel("Backplate", rect, Vector2.zero, LogicalSize, new Color(.949f,.941f,.898f,1));
            var handle = Panel("Move Handle", rect, new Vector2(-126, 171), new Vector2(500, 50), new Color(.949f,.941f,.898f,1));
            handle.gameObject.AddComponent<SpatialDashboardDragHandle>();
            heading = Label("Heading", handle, Vector2.zero, new Vector2(472, 44), 26, TextAnchor.MiddleLeft);
            var resetView = Button(rect, DashboardAction.ResetView, new Vector2(225, 171), new Vector2(186, 50));
            Icon(resetView.transform,10,new Vector2(-66,0),new Vector2(36,36));
            Layout(resetView.GetComponentInChildren<Text>().rectTransform,new Vector2(18,0),new Vector2(132,44));
            Button(rect, DashboardAction.Home, new Vector2(351, 171), new Vector2(62, 50));

            mainPage = Page("Main", rect);
            metrics = Label("Metrics", mainPage, new Vector2(0, 122), new Vector2(728, 32), 22);
            for (var i = 0; i < 5; i++)
            {
                var b = Button(mainPage, (DashboardAction)i, new Vector2(-296 + i * 148, 42), new Vector2(136, 118));
                var text = b.GetComponentInChildren<Text>();
                Layout(text.rectTransform, new Vector2(0, -39), new Vector2(128, 34));
                Icon(b.transform,i,new Vector2(0,17),new Vector2(66,66));
            }
            current = Label("Current", mainPage, new Vector2(-240, -50), new Vector2(266, 42), 21, TextAnchor.MiddleLeft);
            Button(mainPage, DashboardAction.Pause, new Vector2(64, -50), new Vector2(104, 42));
            Button(mainPage, DashboardAction.Details, new Vector2(180, -50), new Vector2(104, 42));
            Button(mainPage, DashboardAction.Settings, new Vector2(296, -50), new Vector2(104, 42));
            sizeReadout = ViewControls("Size", -252, DashboardAction.Shrink, DashboardAction.Enlarge);
            rotationReadout = ViewControls("Rotation", 0, DashboardAction.TurnLeft, DashboardAction.TurnRight);
            distanceReadout = ViewControls("Distance", 252, DashboardAction.BringCloser, DashboardAction.MoveFarther);
            notice = Label("Notice", mainPage, new Vector2(0, -186), new Vector2(744, 36), 19);
            notice.color = new Color(.31f,.43f,.37f);

            detailsPage = Page("Details", rect);
            details = Label("City Details", detailsPage, new Vector2(0, 4), new Vector2(728, 168), 24, TextAnchor.UpperLeft);
            Button(detailsPage, DashboardAction.Events, new Vector2(0, -112), new Vector2(260, 52));
            settingsPage = Page("Settings", rect);
            Button(settingsPage, DashboardAction.Day, new Vector2(-252, 64), new Vector2(224, 50));
            Button(settingsPage, DashboardAction.Night, new Vector2(0, 64), new Vector2(224, 50));
            Button(settingsPage, DashboardAction.Sound, new Vector2(252, 64), new Vector2(224, 50));
            Button(settingsPage, DashboardAction.NormalSpeed, new Vector2(-252, -4), new Vector2(224, 50));
            Button(settingsPage, DashboardAction.DoubleSpeed, new Vector2(0, -4), new Vector2(224, 50));
            Button(settingsPage, DashboardAction.Language, new Vector2(252, -4), new Vector2(224, 50));
            Button(settingsPage, DashboardAction.Reset, new Vector2(-190, -83), new Vector2(348, 62));
            Button(settingsPage, DashboardAction.Recall, new Vector2(190, -83), new Vector2(348, 62));
            resetPage = Page("Confirm Reset", rect);
            resetText = Label("Reset Message", resetPage, new Vector2(0, 22), new Vector2(704, 114), 26);
            Button(resetPage, DashboardAction.ConfirmReset, new Vector2(-190, -85), new Vector2(320, 60));
            Button(resetPage, DashboardAction.Home, new Vector2(190, -85), new Vector2(320, 60));
            eventPage = Page("Events", rect);
            eventText = Label("Event", eventPage, new Vector2(0, 13), new Vector2(704, 154), 24, TextAnchor.MiddleLeft);
            Button(eventPage, DashboardAction.PreviousEvent, new Vector2(-190, -110), new Vector2(320, 50));
            Button(eventPage, DashboardAction.NextEvent, new Vector2(190, -110), new Vector2(320, 50));
            actions = GetComponentsInChildren<SpatialDashboardButton>(true);
            BindButtons();
            RefreshLabels();
            SetPage(0);
            spatialCanvas.enabled = false;
        }

#if UNITY_EDITOR
        public void Rebuild(UIController ui, Font font)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Rebuild UI in Edit Mode.");
            if(spatialCanvas!=null)DestroyImmediate(spatialCanvas.gameObject);
            spatialCanvas=null;Build(ui,font);
        }
#endif

        private void Icon(Transform parent,int index,Vector2 position,Vector2 size)
        {
            if(iconSprites==null||index>=iconSprites.Length)return;
            var go=new GameObject("Nano Banana Icon "+index,typeof(RectTransform),typeof(Image));go.layer=5;go.transform.SetParent(parent,false);
            var graphic=go.GetComponent<Image>();graphic.sprite=iconSprites[index];graphic.material=Resources.Load<Material>("WeatherManager/UI/SpatialUiIcons");graphic.raycastTarget=false;
            Layout(graphic.rectTransform,position,size);
        }

        private Text ViewControls(string name, float x, DashboardAction left, DashboardAction right)
        {
            var group = Panel(name + " controls", mainPage, new Vector2(x, -121), new Vector2(240, 80), new Color(.89f,.91f,.86f));
            Button(group, left, new Vector2(-58, -13), new Vector2(108, 43));
            Button(group, right, new Vector2(58, -13), new Vector2(108, 43));
            return Label(name + " readout", group, new Vector2(0, 23), new Vector2(228, 28), 19);
        }

        public void Configure(Camera camera, Transform trackingOrigin)
        {
            gameObject.SetActive(true);
            spatialCanvas.worldCamera = camera;
            spatialCanvas.enabled = true;
            var mouseCaster = spatialCanvas.GetComponent<GraphicRaycaster>();
            mouseCaster.enabled = !MrRuntimeBridge.IsXrPresentationActive || MrRuntimeBridge.IsPicoEmulator;
            var system = FindFirstObjectByType<EventSystem>();
            var module = system != null ? system.GetComponent<InputSystemUIInputModule>() : null;
            if (module != null) module.xrTrackingOrigin = trackingOrigin;
            BindButtons();
            RefreshLabels();
        }

        public bool Contains(Ray ray)
        {
            if (!IsVisible) return false;
            var rect = (RectTransform)spatialCanvas.transform;
            var plane = new Plane(rect.forward, rect.position);
            return plane.Raycast(ray, out var distance) && distance >= 0f && distance <= 6f
                && rect.rect.Contains((Vector2)rect.InverseTransformPoint(ray.GetPoint(distance)));
        }

        public void RefreshState(SimulationStats stats, WeatherType weather, bool casting, bool valid)
        {
            lastStats = stats;
            selected = weather;
            isCasting = casting;
            targetValid = valid;
            if (spatialCanvas == null) return;
            manager ??= FindFirstObjectByType<GameManager>();
            tabletop ??= FindFirstObjectByType<MrTabletopExperience>();
            SetText(sizeReadout, L($"大小  {tabletop?.ViewScalePercent ?? 100}%", $"Size  {tabletop?.ViewScalePercent ?? 100}%"));
            SetText(rotationReadout, L($"旋转  {tabletop?.ViewYaw ?? 0:0}°", $"Rotation  {tabletop?.ViewYaw ?? 0:0}°"));
            SetText(distanceReadout, L($"距离  {tabletop?.ViewDistance ?? 0:0.00} m", $"Distance  {tabletop?.ViewDistance ?? 0:0.00} m"));
            if (stats != null)
            {
                SetText(metrics, L($"居民 {stats.AliveResidents}/{stats.TotalResidents}     火灾 {stats.ActiveFires}     损毁 {stats.DestroyedObjects}",
                    $"Residents {stats.AliveResidents}/{stats.TotalResidents}     Fires {stats.ActiveFires}     Lost {stats.DestroyedObjects}"));
                if (page == 1) SetText(details, L(
                    $"居民  {stats.AliveResidents} / {stats.TotalResidents}    受伤  {stats.InjuredResidents}\n火灾  {stats.ActiveFires}    受淹  {stats.FloodedPercent:0.0}%\n损毁  {stats.DestroyedObjects} / {stats.TotalDamageables}    龙卷风  {stats.Tornadoes}\n电网  {(stats.Blackout ? "中断" : "正常")}",
                    $"Residents  {stats.AliveResidents} / {stats.TotalResidents}    Injured  {stats.InjuredResidents}\nFires  {stats.ActiveFires}    Flooded  {stats.FloodedPercent:0.0}%\nLost  {stats.DestroyedObjects} / {stats.TotalDamageables}    Tornadoes  {stats.Tornadoes}\nPower  {(stats.Blackout ? "Offline" : "Stable")}"));
            }
            var state = manager != null && manager.TimeScale <= 0f ? L("已暂停", "Paused")
                : casting ? L("施法中", "Casting") : valid ? L("待施法", "Ready") : L("目标无效", "No target");
            SetText(current, WeatherPalette.Label(weather) + "  ·  " + state);
            if (Time.unscaledTime >= noticeUntil)
                SetText(notice, L("按钮调整沙盘  ·  扳机施法  ·  左手 X 召回面板", "Adjust the table  ·  Trigger to cast  ·  Left X to recall"));
            foreach (var a in actions)
            {
                if (a == null) continue;
                var selectedAction = (int)a.Action < 5 && (int)a.Action == (int)weather;
                a.SetSelected(selectedAction);
                if (a.Action == DashboardAction.Pause)
                    a.SetLabel(manager != null && manager.TimeScale <= 0f ? L("继续", "Resume") : L("暂停", "Pause"));
            }
        }

        public void ShowNotice(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            for (var i = Math.Min(eventCount, recentEvents.Length - 1); i > 0; --i) recentEvents[i] = recentEvents[i - 1];
            recentEvents[0] = message;
            eventCount = Math.Min(eventCount + 1, recentEvents.Length);
            noticeUntil = Time.unscaledTime + 4f;
            // Main panel reserves a single line; the complete text is retained in Events.
            SetText(notice, message.Length > (Chinese ? 31 : 65) ? message.Substring(0, Chinese ? 30 : 64) + "…" : message);
            if (page == 4) RefreshEvent();
        }
        public void ClearFeedback()
        {
            Array.Clear(recentEvents, 0, recentEvents.Length);
            eventCount = eventIndex = 0;
            noticeUntil = 0;
            SetPage(0);
        }
        public void Invoke(DashboardAction action)
        {
            manager ??= FindFirstObjectByType<GameManager>();
            if ((int)action < 5) { manager?.SetWeather((WeatherType)action); return; }
            switch (action)
            {
                case DashboardAction.Pause: manager?.SetTimeScale(manager.TimeScale <= 0f ? 1f : 0f); break;
                case DashboardAction.Details: SetPage(1); break;
                case DashboardAction.Settings: SetPage(2); break;
                case DashboardAction.Home: SetPage(0); break;
                case DashboardAction.Day: manager?.SetDayPhase(DayPhase.Day); break;
                case DashboardAction.Night: manager?.SetDayPhase(DayPhase.Night); break;
                case DashboardAction.NormalSpeed: manager?.SetTimeScale(1f); break;
                case DashboardAction.DoubleSpeed: manager?.SetTimeScale(2f); break;
                case DashboardAction.Sound: soundOn = !soundOn; manager?.SetSoundEnabled(soundOn); RefreshLabels(); break;
                case DashboardAction.Language: LocalizationService.SetLanguage(Chinese ? AppLanguage.English : AppLanguage.Chinese); break;
                case DashboardAction.Reset: SetPage(3); break;
                case DashboardAction.ConfirmReset: manager?.ResetWorld(); SetPage(0); break;
                case DashboardAction.Recall: FindFirstObjectByType<MrTabletopExperience>()?.RecallDashboard(); break;
                case DashboardAction.Events: eventIndex = 0; SetPage(4); break;
                case DashboardAction.PreviousEvent: eventIndex = Mathf.Max(0, eventIndex - 1); RefreshEvent(); break;
                case DashboardAction.NextEvent: eventIndex = Mathf.Min(Mathf.Max(0, eventCount - 1), eventIndex + 1); RefreshEvent(); break;
                case DashboardAction.ResetView:
                case DashboardAction.Shrink:
                case DashboardAction.Enlarge:
                case DashboardAction.TurnLeft:
                case DashboardAction.TurnRight:
                case DashboardAction.BringCloser:
                case DashboardAction.MoveFarther:
                    tabletop ??= FindFirstObjectByType<MrTabletopExperience>();
                    tabletop?.AdjustTabletopView(action);
                    break;
            }
            RefreshState(lastStats, selected, isCasting, targetValid);
        }

        private void SetPage(int value)
        {
            page = value;
            if (mainPage == null) return;
            mainPage.gameObject.SetActive(page == 0);
            detailsPage.gameObject.SetActive(page == 1);
            settingsPage.gameObject.SetActive(page == 2);
            resetPage.gameObject.SetActive(page == 3);
            eventPage.gameObject.SetActive(page == 4);
            RefreshLabels();
            RefreshEvent();
        }
        private void RefreshEvent()
        {
            if (eventText == null) return;
            SetText(eventText, eventCount == 0 ? L("暂无事件", "No incidents yet")
                : $"{eventIndex + 1} / {eventCount}\n\n{recentEvents[eventIndex]}");
        }
        private void RefreshLabels()
        {
            SetText(heading, page switch { 1 => L("城市详情", "CITY DETAILS"), 2 => L("设置", "SETTINGS"),
                3 => L("重置城镇", "RESET TOWN"), 4 => L("事件记录", "EVENT HISTORY"), _ => L("天气管理员", "WEATHER MANAGER") });
            SetText(resetText, L("将恢复城镇并清除当前灾害。\n确定重置吗？", "Restore the town and clear all disasters?\nThis resets the current simulation."));
            foreach (var a in actions)
            {
                if (a == null) continue;
                a.SetLabel(a.Action switch {
                    DashboardAction.Wind => L("大风", "Wind"), DashboardAction.Rain => L("降雨", "Rain"),
                    DashboardAction.Electric => L("雷电", "Lightning"), DashboardAction.Snow => L("降雪", "Snow"),
                    DashboardAction.Heat => L("高温", "Heat"), DashboardAction.Pause => L("暂停", "Pause"),
                    DashboardAction.Details => L("详情", "Details"), DashboardAction.Settings => L("设置", "Settings"),
                    DashboardAction.Home => L("返回", "Back"), DashboardAction.Day => L("白天", "Day"),
                    DashboardAction.Night => L("夜晚", "Night"), DashboardAction.NormalSpeed => "1×",
                    DashboardAction.DoubleSpeed => "2×", DashboardAction.Sound => soundOn ? L("声音：开", "Sound: on") : L("声音：关", "Sound: off"),
                    DashboardAction.Language => Chinese ? "English" : "中文", DashboardAction.Reset => L("重置城镇", "Reset town"),
                    DashboardAction.ConfirmReset => L("确认重置", "Confirm reset"), DashboardAction.Recall => L("召回面板", "Recall panel"),
                    DashboardAction.Events => L("查看事件记录", "Event history"), DashboardAction.PreviousEvent => L("上一条", "Previous"),
                    DashboardAction.ResetView => L("视角复位", "Reset view"),
                    DashboardAction.Shrink => L("缩小 −", "Smaller"), DashboardAction.Enlarge => L("放大 +", "Larger"),
                    DashboardAction.TurnLeft => L("左转", "Left"), DashboardAction.TurnRight => L("右转", "Right"),
                    DashboardAction.BringCloser => L("拉近", "Closer"), DashboardAction.MoveFarther => L("推远", "Farther"),
                    _ => L("下一条", "Next") });
            }
        }
        private void BindButtons()
        {
            foreach (var a in actions) if (a != null) a.Bind(this);
        }
        private RectTransform Page(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; Layout(rect, Vector2.zero, LogicalSize); return rect;
        }
        private RectTransform Panel(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RoundedUiGraphic));
            go.layer = 5; go.transform.SetParent(parent, false);
            var image = go.GetComponent<RoundedUiGraphic>(); image.color = color; image.raycastTarget = true;image.material=foreground;
            Layout(image.rectTransform, position, size); return image.rectTransform;
        }
        private Text Label(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.layer = 5; go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>(); text.font = readableFont; text.fontSize = fontSize;
            if(name=="Heading"||name=="Label")text.fontStyle=FontStyle.Bold;
            text.alignment = alignment; text.color = new Color(.10f,.25f,.23f); text.raycastTarget = false;text.material=foreground;
            text.resizeTextForBestFit = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            Layout(text.rectTransform, position, size); return text;
        }
        private SpatialDashboardButton Button(Transform parent, DashboardAction action, Vector2 position, Vector2 size)
        {
            var rect = Panel(action.ToString(), parent, position, size, Color.white);
            var b = rect.gameObject.AddComponent<Button>();
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = b.colors; colors.normalColor = new Color(.88f,.90f,.84f);
            colors.highlightedColor = new Color(.76f,.86f,.76f); colors.pressedColor = new Color(.62f,.79f,.68f);
            colors.selectedColor = colors.normalColor; colors.fadeDuration = .12f; b.colors = colors;
            var text = Label("Label", rect, Vector2.zero, size - new Vector2(12, 6), 22);
            var a = rect.gameObject.AddComponent<SpatialDashboardButton>(); a.Initialize(action, b, text); return a;
        }
        private static void SetText(Text text, string value) { if (text != null && text.text != value) text.text = value; }
        private static void Layout(RectTransform r, Vector2 position, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = position; r.sizeDelta = size; r.localRotation = Quaternion.identity; r.localScale = Vector3.one;
        }
    }

}
