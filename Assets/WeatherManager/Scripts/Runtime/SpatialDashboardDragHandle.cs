using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WeatherManager
{
    public sealed class SpatialDashboardDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private SpatialWeatherDashboard dashboard;
        private Plane dragPlane;
        private Vector3 grabOffset;
        public void OnBeginDrag(PointerEventData e)
        {
            dashboard = GetComponentInParent<SpatialWeatherDashboard>();
            if (dashboard == null) return;
            var root = dashboard.transform.parent;
            dragPlane = new Plane(root.forward, root.position);
            if (!Point(e, out var point)) return;
            var pressedPoint = e.pointerPressRaycast.isValid ? e.pointerPressRaycast.worldPosition : point;
            dashboard.IsDragging = true; grabOffset = root.position - pressedPoint;
            FindFirstObjectByType<WeatherCastingController>()?.SuspendInput();
        }
        public void OnDrag(PointerEventData e)
        {
            if (dashboard == null || !dashboard.IsDragging || !Point(e, out var point)) return;
            dashboard.transform.parent.position = point + grabOffset;
        }
        public void OnEndDrag(PointerEventData e) { if (dashboard != null) dashboard.IsDragging = false; }
        private bool Point(PointerEventData e, out Vector3 point)
        {
            point = default;
            var camera = e.pressEventCamera ?? dashboard.Canvas.worldCamera;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(e.position);
            var mr = FindFirstObjectByType<MrTabletopExperience>();
            if (e is ExtendedPointerEventData extended && extended.pointerType == UIPointerType.Tracked && mr != null)
                if (mr.TryGetRightHandCastRay(out var handRay, out _, out _)) ray = handRay;
            if (!dragPlane.Raycast(ray, out var d) || d < 0f || d > 6f) return false;
            point = ray.GetPoint(d); return true;
        }
    }
}
