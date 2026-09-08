using UnityEngine;
using UnityEngine.XR;

namespace WeatherManager
{
    public sealed class TrackedWeatherPointer : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField] private Transform rig;
        [SerializeField] private bool rightHand;
        private SpatialWeatherDashboard dashboard;
        public void Configure(LineRenderer renderer, Transform origin, bool right) { line=renderer;rig=origin;rightHand=right; }
        private void LateUpdate()
        {
            if (line == null) return;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var valid = rightHand && device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked,out var tracked) && tracked;
            line.enabled = valid;
            if (!valid || !device.TryGetFeatureValue(CommonUsages.devicePosition,out var position) || !device.TryGetFeatureValue(CommonUsages.deviceRotation,out var rotation)) {line.enabled=false;return;}
            var origin = rig.TransformPoint(position);
            var direction = rig.TransformDirection(rotation * Vector3.forward).normalized;
            var ray = new Ray(origin,direction); var length = 2.5f;
            if (Physics.Raycast(ray,out var hit,length,~(1<<5),QueryTriggerInteraction.Ignore)) length=hit.distance;
            dashboard ??= FindFirstObjectByType<SpatialWeatherDashboard>();
            if (dashboard != null && dashboard.IsVisible)
            {
                var rect=(RectTransform)dashboard.Canvas.transform;
                var plane=new Plane(rect.forward,rect.position);
                if (plane.Raycast(ray,out var d) && d>0 && d<length && rect.rect.Contains((Vector2)rect.InverseTransformPoint(ray.GetPoint(d)))) length=d;
            }
            line.useWorldSpace = true; line.SetPosition(0,origin); line.SetPosition(1,ray.GetPoint(length));
        }
        private void OnDisable() {if(line!=null)line.enabled=false;}
    }
}
