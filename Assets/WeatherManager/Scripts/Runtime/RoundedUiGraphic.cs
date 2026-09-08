using UnityEngine;
using UnityEngine.UI;

namespace WeatherManager
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedUiGraphic : MaskableGraphic
    {
        [SerializeField] private float radius = 18f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = GetPixelAdjustedRect();
            var corner = Mathf.Min(radius, Mathf.Min(r.width, r.height) * .5f);
            vh.AddVert(r.center, color, Vector2.one * .5f);
            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var center = new Vector2(quadrant == 0 || quadrant == 3 ? r.xMax - corner : r.xMin + corner,
                    quadrant < 2 ? r.yMax - corner : r.yMin + corner);
                for (var step = 0; step <= 6; step++)
                {
                    var angle = (quadrant * 90 + step * 15) * Mathf.Deg2Rad;
                    var p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * corner;
                    vh.AddVert(p, color, new Vector2((p.x-r.xMin)/r.width,(p.y-r.yMin)/r.height));
                }
            }
            for (var i = 1; i <= 28; i++) vh.AddTriangle(0, i, i == 28 ? 1 : i + 1);
        }
    }
}
