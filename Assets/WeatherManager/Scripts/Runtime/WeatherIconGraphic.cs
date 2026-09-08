using UnityEngine;
using UnityEngine.UI;

namespace WeatherManager
{
    /// <summary>Resolution independent weather symbols; no embedded text or texture allocation.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WeatherIconGraphic : MaskableGraphic
    {
        [SerializeField] private WeatherType weather;
        public WeatherType Weather { get => weather; set { weather = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            switch (weather)
            {
                case WeatherType.Wind:
                    Line(vh, new Vector2(-.85f,.45f), new Vector2(.65f,.45f));
                    Line(vh, new Vector2(-.65f,0), new Vector2(.85f,0));
                    Line(vh, new Vector2(-.85f,-.45f), new Vector2(.35f,-.45f));
                    Line(vh, new Vector2(.65f,.45f), new Vector2(.4f,.7f)); break;
                case WeatherType.Rain:
                    for (var i=-1;i<=1;i++) Line(vh,new Vector2(i*.6f+.1f,.7f),new Vector2(i*.6f-.15f,-.65f),.16f);
                    break;
                case WeatherType.Electric:
                    Triangle(vh,new Vector2(.1f,.95f),new Vector2(-.65f,-.1f),new Vector2(.18f,.05f));
                    Triangle(vh,new Vector2(-.18f,-.05f),new Vector2(.65f,.1f),new Vector2(-.1f,-.95f)); break;
                case WeatherType.Snow:
                    for(var i=0;i<3;i++) { var d = new Vector2(Mathf.Cos(i*Mathf.PI/3), Mathf.Sin(i*Mathf.PI/3)); Line(vh,-d*.85f,d*.85f); }
                    break;
                case WeatherType.Heat:
                    for(var i=0;i<12;i++) { var a=i*Mathf.PI*2/12; var b=(i+1)*Mathf.PI*2/12;
                        Triangle(vh,Vector2.zero,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.45f,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*.45f); }
                    for(var i=0;i<8;i++) { var d=new Vector2(Mathf.Cos(i*Mathf.PI/4),Mathf.Sin(i*Mathf.PI/4)); Line(vh,d*.65f,d*.94f); } break;
            }
        }
        private void Line(VertexHelper vh,Vector2 a,Vector2 b,float width=.12f)
        {
            var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
            Triangle(vh,a-n,a+n,b+n); Triangle(vh,a-n,b+n,b-n);
        }
        private void Triangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c)
        {
            var r=rectTransform.rect; var scale=Mathf.Min(r.width,r.height)*.5f; var k=vh.currentVertCount;
            vh.AddVert(r.center+a*scale,color,Vector2.zero); vh.AddVert(r.center+b*scale,color,Vector2.zero); vh.AddVert(r.center+c*scale,color,Vector2.zero);
            vh.AddTriangle(k,k+1,k+2);
        }
    }
}
