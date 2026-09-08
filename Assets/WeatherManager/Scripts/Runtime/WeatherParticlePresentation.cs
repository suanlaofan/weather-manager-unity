using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherManager
{
    /// <summary>Bounded, manually clocked particles in island coordinates. Moving a cast never moves old particles.</summary>
    [DisallowMultipleComponent]
    public sealed class WeatherParticlePresentation : MonoBehaviour
    {
        [SerializeField] private WeatherPresentationSettings settings;
        [SerializeField] private Transform island;
        [SerializeField] private ParticleSystem[] layers = new ParticleSystem[12];
        [SerializeField] private MeshFilter[] funnels = new MeshFilter[5];
        [SerializeField] private Mesh funnelMesh;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Contact[] contacts = new Contact[192];
        private readonly int[] funnelIds = new int[5];
        private readonly float[] funnelFade = new float[5];
        private readonly float[] funnelHeat = new float[5];
        private readonly bool[] funnelSeen = new bool[5];
        private MaterialPropertyBlock funnelBlock;
        private MeshRenderer[] funnelRenderers;
        private WeatherCastState? activeCast;
        private IReadOnlyList<SurfaceVisualCell> cells;
        private IReadOnlyList<TornadoSnapshot> tornadoes;
        private LowPolyTerrainGenerator terrain;
        private float clock, emissionFraction, establish, environmentTimer, cloudTimer;
        private int nextContact, randomCounter, fireCursor;
        private uint random = 91721;
        private const int Flame=5, Smoke=6, Spark=7, Splash=8, Ripple=9, Cloud=10, Steam=11;
        private struct Contact { public Vector3 point; public float due; public WeatherType type; public bool active; }
        public ParticleSystem[] Layers => layers;
        public WeatherPresentationSettings Settings => settings;
        public int PendingContacts { get { var n=0; foreach(var c in contacts) if(c.active)n++; return n; } }
        public static readonly Vector3 WindDirection = new Vector3(1f,0f,.35f).normalized;

        public void Configure(Transform tabletop,WeatherPresentationSettings config=null)
        {
            funnelBlock ??= new MaterialPropertyBlock();
            island=tabletop;
            settings=config!=null?config:Resources.Load<WeatherPresentationSettings>("WeatherManager/Presentation/WeatherPresentation");
            if(settings==null) return;
            terrain=FindFirstObjectByType<LowPolyTerrainGenerator>();
            if(layers==null||layers.Length!=12) layers=new ParticleSystem[12];
            for(var i=0;i<5;i++) layers[i]=MakeLayer(i,settings.weather[i].weather.ToString(),settings.weather[i].material,settings.weather[i].capacity,false);
            layers[Flame]=MakeLayer(Flame,"Flame tongues",settings.flame,80,true);
            layers[Smoke]=MakeLayer(Smoke,"Drifting smoke",settings.smoke,80,true);
            layers[Spark]=MakeLayer(Spark,"Embers",settings.spark,40,false);
            layers[Splash]=MakeLayer(Splash,"Surface splashes",settings.splash,80,false);
            layers[Ripple]=MakeLayer(Ripple,"Contact ripples",settings.ripple,48,false);
            layers[Cloud]=MakeLayer(Cloud,"Weather cloud",settings.cloud,24,false);
            layers[Steam]=MakeLayer(Steam,"Extinguishing steam",settings.steam,40,true);
            var rippleRenderer=layers[Ripple].GetComponent<ParticleSystemRenderer>();
            rippleRenderer.renderMode=ParticleSystemRenderMode.HorizontalBillboard;
            if(funnelMesh==null) funnelMesh=BuildFunnelMesh();
            if(funnels==null||funnels.Length!=5) funnels=new MeshFilter[5];
            funnelRenderers=new MeshRenderer[5];
            for(var i=0;i<5;i++)
            {
                if(funnels[i]==null) { var go=new GameObject("Continuous Funnel "+i,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(transform,false); funnels[i]=go.GetComponent<MeshFilter>(); }
                funnels[i].sharedMesh=funnelMesh;
                funnelRenderers[i]=funnels[i].GetComponent<MeshRenderer>();
                funnelRenderers[i].sharedMaterial=settings.funnel;
                funnelRenderers[i].shadowCastingMode=ShadowCastingMode.Off;
                funnelRenderers[i].receiveShadows=false;
                funnelRenderers[i].enabled=false;
            }
            ResetPresentation();
        }

        private ParticleSystem MakeLayer(int index,string label,Material material,int capacity,bool sheet)
        {
            var system=layers[index];
            if(system==null) { var go=new GameObject(label); go.transform.SetParent(transform,false); system=go.AddComponent<ParticleSystem>(); }
            system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.localPosition=Vector3.zero; system.transform.localRotation=Quaternion.identity; system.transform.localScale=Vector3.one;
            var main=system.main; main.loop=false; main.playOnAwake=false; main.startSpeed=0;
            main.maxParticles=capacity; main.simulationSpace=ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace=island; main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier=0; main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var emission=system.emission; emission.enabled=false;
            var shape=system.shape; shape.enabled=false;
            var size=system.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1f,new AnimationCurve(new Keyframe(0,index==Flame?.65f:.8f),new Keyframe(.45f,1),new Keyframe(1,index==Smoke||index==Steam?1.8f:.6f)));
            if(index==Ripple) size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,.3f,1,1.5f));
            var alpha=system.colorOverLifetime; alpha.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.1f),new GradientAlphaKey(.8f,.58f),new GradientAlphaKey(0,1)});
            alpha.color=gradient;
            var animation=system.textureSheetAnimation; animation.enabled=sheet;
            if(sheet) { animation.numTilesX=4; animation.numTilesY=4; animation.animation=ParticleSystemAnimationType.WholeSheet;
                animation.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,1)); animation.cycleCount=index==Flame?3:1; }
            var renderer=system.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material;
            renderer.renderMode=ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false; renderer.sortMode=ParticleSystemSortMode.Distance;
            renderer.minParticleSize=0; renderer.maxParticleSize=.45f;
            renderer.localBounds=new Bounds(Vector3.zero,new Vector3(118,38,88));
            system.useAutoRandomSeed=false; system.randomSeed=(uint)(index*347+211);
            return system;
        }

        public void SetCast(WeatherCastState? value,bool clear)
        {
            if(value.HasValue&&(!activeCast.HasValue||activeCast.Value.Type!=value.Value.Type)) { establish=0; emissionFraction=1; cloudTimer=0; }
            activeCast=value;
            if(clear) ResetPresentation();
        }
        public void Sync(IReadOnlyList<SurfaceVisualCell> surface,IReadOnlyList<TornadoSnapshot> funnelsNow) { cells=surface; tornadoes=funnelsNow; }
        public void Tick(float delta)
        {
            if(settings==null||delta<=0) return;
            clock+=delta;
            if(activeCast.HasValue&&activeCast.Value.Active) EmitWeather(activeCast.Value,delta);
            for(var i=0;i<contacts.Length;i++) if(contacts[i].active&&clock>=contacts[i].due)
            {
                var c=contacts[i]; contacts[i].active=false;
                var snow=c.type==WeatherType.Snow;
                Emit(Splash,c.point+Vector3.up*.025f,Vector3.up*(snow?.2f:1.2f),snow?.24f:.25f,snow?.5f:.24f,snow?Color.white:new Color(.6f,.86f,1,.8f));
                if(!snow) Emit(Ripple,c.point+Vector3.up*.04f,Vector3.zero,.65f,.65f,new Color(.55f,.88f,1,.65f));
            }
            environmentTimer-=delta;
            if(environmentTimer<=0) { environmentTimer=.16f; EmitEnvironment(); }
            TickFunnels(delta);
            foreach(var system in layers) if(system!=null&&system.particleCount>0) system.Simulate(delta,false,false,false);
        }
        private void EmitWeather(WeatherCastState cast,float delta)
        {
            var profile=settings.weather[(int)cast.Type];
            establish=Mathf.MoveTowards(establish,1,delta/settings.establishSeconds);
            emissionFraction+=delta*profile.particlesPerSecond*settings.density*Mathf.Lerp(.4f,1,establish);
            var count=Mathf.Min(24,(int)emissionFraction); emissionFraction-=count;
            for(var n=0;n<count;n++)
            {
                var angle=Next()*Mathf.PI*2; var radius=Mathf.Sqrt(Next())*cast.Radius;
                var point=cast.Center+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                point.y=terrain!=null?terrain.SampleHeight(point)+.08f:cast.Center.y;
                var life=Mathf.Lerp(profile.lifetime.x,profile.lifetime.y,Next());
                var size=Mathf.Lerp(profile.size.x,profile.size.y,Next());
                var velocity=Vector3.zero; var rotation=0f;
                if(cast.Type==WeatherType.Rain||cast.Type==WeatherType.Snow)
                {
                    // Sparse surface proxy sampling includes real rooftops; no per-particle collision module.
                    var contact=SurfacePoint(point,true);
                    var height=cast.Type==WeatherType.Rain?5.5f:3.8f;
                    point=contact+Vector3.up*height;
                    velocity=new Vector3(cast.Type==WeatherType.Rain?.25f:.13f,-height/life,.08f);
                    if(randomCounter++%6==0) { contacts[nextContact]=new Contact { active=true,point=contact+new Vector3(velocity.x,0,velocity.z)*life,due=clock+life,type=cast.Type }; nextContact=(nextContact+1)%contacts.Length; }
                    if(cast.Type==WeatherType.Snow) rotation=Next()*360;
                }
                else if(cast.Type==WeatherType.Wind) { point+=Vector3.up*(.3f+Next()*1.9f); velocity=WindDirection*(4+Next()*3)+Vector3.up*.15f; rotation=-18f; }
                else if(cast.Type==WeatherType.Heat) { point+=Vector3.up*.3f; velocity=Vector3.up*(.9f+Next()*.5f); }
                else { point+=Vector3.up*(.2f+Next()*2); velocity=Vector3.up*(.7f+Next()); }
                Emit((int)cast.Type,point,velocity,size,life,profile.color,rotation);
            }
            cloudTimer-=delta;
            if(cloudTimer<=0&&(cast.Type==WeatherType.Rain||cast.Type==WeatherType.Snow||cast.Type==WeatherType.Electric))
            {
                cloudTimer=.24f;
                var p=cast.Center+new Vector3((Next()-.5f)*cast.Radius,cast.Type==WeatherType.Rain?8.8f:6.8f,(Next()-.5f)*cast.Radius);
                Emit(Cloud,p,WindDirection*.12f,cast.Radius*.75f,1.5f,new Color(.24f,.31f,.39f,.24f));
            }
        }
        private Vector3 SurfacePoint(Vector3 point,bool roof)
        {
            if(!roof||island==null) return point;
            var ray=new Ray(island.TransformPoint(point+Vector3.up*18),-island.up);
            var count=Physics.RaycastNonAlloc(ray,hits,22*island.lossyScale.x,~(1<<5),QueryTriggerInteraction.Ignore);
            var best=float.MaxValue;
            for(var i=0;i<count;i++) if(hits[i].transform.IsChildOf(island)&&hits[i].distance<best)
            { best=hits[i].distance; point=island.InverseTransformPoint(hits[i].point)+Vector3.up*.035f; }
            return point;
        }
        private void EmitEnvironment()
        {
            if(cells==null||cells.Count==0) return;
            var fires=0; var fluids=0;
            for(var n=0;n<cells.Count;n++)
            {
                var cell=cells[(n+fireCursor)%cells.Count];
                if(cell.Fire>.07f&&fires<16)
                {
                    var p=cell.Position+new Vector3((Next()-.5f)*.48f,.25f,(Next()-.5f)*.48f);
                    Emit(Flame,p,Vector3.up*(.35f+cell.Fire*.45f),.65f+cell.Fire*.85f,.55f+Next()*.25f,Color.white);
                    if((fires&1)==0) Emit(Smoke,p+Vector3.up*.9f,WindDirection*.35f+Vector3.up*.8f,1.05f,2.4f,new Color(.28f,.31f,.34f,.48f));
                    if((fires%4)==0) Emit(Spark,p,Vector3.up*(1.5f+Next())+WindDirection*.4f,.075f,1,new Color(1,.68f,.16f,.9f));
                    if(cell.Water>.12f) Emit(Steam,p,Vector3.up*.9f+WindDirection*.16f,.8f,1.1f,new Color(.85f,.94f,1,.55f));
                    fires++;
                }
                if(cell.Water>.18f&&fluids<4) { Emit(Ripple,cell.Position+Vector3.up*.10f,WindDirection*.12f,.85f,.8f,new Color(.7f,.92f,1,.35f)); fluids++; }
                if(fires>=16&&fluids>=4)break;
            }
            fireCursor=(fireCursor+17)%cells.Count;
        }
        private void TickFunnels(float delta)
        {
            Array.Clear(funnelSeen,0,5);
            if(tornadoes!=null) for(var tornadoIndex=0;tornadoIndex<tornadoes.Count;tornadoIndex++)
            {
                var tornado=tornadoes[tornadoIndex];
                var slot=Array.IndexOf(funnelIds,tornado.Id);
                if(slot<0) { for(var i=0;i<5;i++) if(funnelFade[i]<=0) { slot=i; funnelIds[i]=tornado.Id; break; } }
                if(slot<0)continue;
                funnelSeen[slot]=true;
                var t=funnels[slot].transform;
                t.position=island!=null?island.TransformPoint(tornado.Position):tornado.Position;
                t.localScale=new Vector3(.8f+tornado.Strength*.6f,3.7f+tornado.Strength, .8f+tornado.Strength*.6f);
                t.localRotation=Quaternion.Euler(0,clock*120+tornado.Id*13,0);
                funnelHeat[slot]=Mathf.MoveTowards(funnelHeat[slot],tornado.IsFireTornado?1:0,delta*1.6f);
                if(environmentTimer>=.15f) Emit(Smoke,tornado.Position+Vector3.up*.18f,WindDirection*.4f,1.3f,.6f,new Color(.48f,.41f,.32f,.35f));
            }
            for(var i=0;i<5;i++)
            {
                funnelFade[i]=Mathf.MoveTowards(funnelFade[i],funnelSeen[i]?1:0,delta*(funnelSeen[i]?2.5f:1.2f));
                var renderer=funnelRenderers[i]; renderer.enabled=funnelFade[i]>0;
                if(!renderer.enabled)continue;
                var color=Color.Lerp(new Color(.53f,.66f,.68f,.82f),new Color(1f,.35f,.07f,.9f),funnelHeat[i]); color.a*=funnelFade[i];
                funnelBlock.SetColor("_BaseColor",color); funnelBlock.SetFloat("_FxTime",clock); renderer.SetPropertyBlock(funnelBlock);
            }
        }
        private void Emit(int layer,Vector3 point,Vector3 velocity,float size,float life,Color color,float rotation=0)
        {
            var system=layers[layer]; if(system==null||system.particleCount>=system.main.maxParticles)return;
            // Particle coordinates, velocity and size are authored once in island units.
            var boost=island!=null&&island.lossyScale.x<.05f?1+settings.smallTableReadability:1;
            var p=new ParticleSystem.EmitParams { position=point, velocity=velocity, startSize=size*boost,
                startLifetime=life,startColor=color,rotation=rotation,applyShapeToPosition=false };
            system.Emit(p,1);
        }
        private float Next() { random^=random<<13; random^=random>>17; random^=random<<5; return (random&0xffffff)/16777216f; }
        public void ResetPresentation()
        {
            activeCast=null; clock=0; emissionFraction=0; establish=0; environmentTimer=0; cloudTimer=0; nextContact=0; fireCursor=0;
            cells=null; tornadoes=null; random=91721;
            Array.Clear(contacts,0,contacts.Length); Array.Clear(funnelFade,0,5); Array.Clear(funnelHeat,0,5); Array.Clear(funnelIds,0,5);
            foreach(var system in layers) if(system!=null)system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(funnelRenderers!=null) foreach(var renderer in funnelRenderers)if(renderer!=null)renderer.enabled=false;
        }
        private static Mesh BuildFunnelMesh()
        {
            const int segments=24,rings=20; var vertices=new Vector3[(segments+1)*(rings+1)];var uv=new Vector2[vertices.Length];var triangles=new int[segments*rings*6];
            for(var y=0;y<=rings;y++)for(var x=0;x<=segments;x++)
            { var h=y/(float)rings;var a=x/(float)segments*Mathf.PI*2;var radius=.075f+.70f*Mathf.Pow(h,1.2f);
                var i=y*(segments+1)+x;vertices[i]=new Vector3(Mathf.Cos(a)*radius,h,Mathf.Sin(a)*radius);uv[i]=new Vector2(x/(float)segments,h); }
            var k=0;for(var y=0;y<rings;y++)for(var x=0;x<segments;x++) { var i=y*(segments+1)+x;triangles[k++]=i;triangles[k++]=i+segments+1;triangles[k++]=i+1;triangles[k++]=i+1;triangles[k++]=i+segments+1;triangles[k++]=i+segments+2; }
            var mesh=new Mesh {name="Continuous tapered funnel",vertices=vertices,uv=uv,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
