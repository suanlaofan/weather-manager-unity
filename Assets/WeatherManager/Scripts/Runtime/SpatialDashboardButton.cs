using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WeatherManager
{
    public sealed class SpatialDashboardButton : MonoBehaviour
    {
        [SerializeField] private DashboardAction action;
        [SerializeField] private Button button;
        [SerializeField] private Text label;
        private SpatialWeatherDashboard dashboard;
        public DashboardAction Action => action;
        public void Initialize(DashboardAction value, Button b, Text text) { action = value; button = b; label = text; }
        public void Bind(SpatialWeatherDashboard value) { dashboard = value; button.onClick.RemoveListener(Clicked); button.onClick.AddListener(Clicked); }
        private void Clicked() { dashboard?.Invoke(action); }
        public void SetLabel(string value) { if (label != null && label.text != value) label.text = value; }
        public void SetSelected(bool value)
        {
            if ((int)action >= 5 || button == null) return;
            var colors = button.colors;
            var next = value ? new Color(.67f,.82f,.71f) : new Color(.91f,.92f,.87f);
            if (colors.normalColor == next) return;
            colors.normalColor = colors.selectedColor = next; button.colors = colors;
        }
    }

}
