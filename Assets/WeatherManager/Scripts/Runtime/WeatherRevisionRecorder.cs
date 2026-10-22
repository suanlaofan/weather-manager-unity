#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Object=UnityEngine.Object;
namespace WeatherManager.Editor
{
    public sealed class WeatherRevisionRecorder : MonoBehaviour
    {
        [Serializable] private sealed class Status { public string state="running",segment; public int frames,peakParticles,peakFires,peakTornadoes,peakFireTornadoes,peakDamage; public float wallSeconds; }
        private Status status=new Status();
        private GameManager manager; private WeatherCastingController casting; private DisasterSimulation simulation;
        private MrTabletopExperience mr; private WeatherParticlePresentation particles; private SpatialWeatherDashboard panel;
        private Camera camera; private string folder; private StreamWriter timing; private float start; private bool recording;
        private Font font;
        public void Begin(string directory)
        {
            folder=directory;Directory.CreateDirectory(folder);Directory.CreateDirectory(folder+"/frames");
            manager=Object.FindFirstObjectByType<GameManager>();casting=Object.FindFirstObjectByType<WeatherCastingController>();
            simulation=Object.FindFirstObjectByType<DisasterSimulation>();mr=Object.FindFirstObjectByType<MrTabletopExperience>();
            particles=Object.FindFirstObjectByType<WeatherParticlePresentation>();panel=Object.FindFirstObjectByType<SpatialWeatherDashboard>();camera=Camera.main;
            font=Resources.Load<Font>("WeatherManager/Fonts/NotoSansCJKsc-Regular");
            timing=new StreamWriter(folder+"/frames.tsv");timing.WriteLine("frame\tseconds\tsegment");start=Time.realtimeSinceStartup;recording=true;
            StartCoroutine(Capture());StartCoroutine(Sequence());
        }
        private void Save() {status.wallSeconds=Time.realtimeSinceStartup-start;File.WriteAllText(folder+"/status.json",JsonUtility.ToJson(status,true));}
        private IEnumerator Capture()
        {
            var due=Time.realtimeSinceStartup;
            while(recording)
            {
                yield return new WaitForEndOfFrame();
                if(Time.realtimeSinceStartup<due)continue;
                var texture=ScreenCapture.CaptureScreenshotAsTexture();
                var path=folder+"/frames/"+status.frames.ToString("D5")+".jpg";
                File.WriteAllBytes(path,texture.EncodeToJPG(90));Destroy(texture);
                timing.WriteLine(status.frames+"\t"+(Time.realtimeSinceStartup-start).ToString("F5",System.Globalization.CultureInfo.InvariantCulture)+"\t"+status.segment);
                status.frames++;due=Time.realtimeSinceStartup+1f/12;
                var count=0;foreach(var ps in particles.Layers)count+=ps.particleCount;
                status.peakParticles=Mathf.Max(status.peakParticles,count);status.peakFires=Mathf.Max(status.peakFires,manager.Stats.ActiveFires);
                status.peakTornadoes=Mathf.Max(status.peakTornadoes,manager.Stats.Tornadoes);status.peakFireTornadoes=Mathf.Max(status.peakFireTornadoes,manager.Stats.FireTornadoes);
                status.peakDamage=Mathf.Max(status.peakDamage,manager.Stats.DestroyedObjects);
                if(status.frames%12==0)Save();
            }
            timing.Flush();timing.Dispose();timing=null;status.state="complete";Save();
        }
        private Vector3 Center(BiomeType type)
        {
            foreach(var zone in Object.FindFirstObjectByType<WorldBuilder>().Biomes.Zones)
                if(zone.Type==type)return new Vector3(zone.Center.x,0,zone.Center.y);
            return Vector3.zero;
        }
        private void CloseView(Vector3 point)
        {
            panel.Canvas.enabled=false;
            point.y=simulation.SampleGroundHeight(point);
            var target=mr.TabletopIsland.TransformPoint(point+Vector3.up*2.2f);
            camera.transform.position=target+new Vector3(0,.36f,-.48f);camera.transform.LookAt(target);camera.fieldOfView=55;
        }
        private IEnumerator ArtView(Vector3 from,Vector3 to,string label)
        {
            panel.Canvas.enabled=false;status.segment=label;
            camera.transform.SetPositionAndRotation(mr.TabletopIsland.TransformPoint(from),Quaternion.LookRotation(mr.TabletopIsland.TransformDirection(to-from),mr.TabletopIsland.up));camera.fieldOfView=48;
            yield return new WaitForSeconds(5);
        }
        private IEnumerator Cast(WeatherType type,Vector3 point,float seconds,string label)
        {
            status.segment=label;point.y=simulation.SampleGroundHeight(point);casting.SetReviewCast(type,point);Save();
            var finish=manager.Stats.Elapsed+seconds;
            while(manager.Stats.Elapsed<finish)yield return null;
            casting.SetReviewCast(null,point);
        }
        private IEnumerator Sequence()
        {
            status.segment="Spatial panel · Editor mouse and view preview";yield return new WaitForSeconds(3);
            foreach(var action in new[]{DashboardAction.Enlarge,DashboardAction.Shrink,DashboardAction.TurnLeft,DashboardAction.TurnRight,DashboardAction.BringCloser,DashboardAction.MoveFarther})
            {
                status.segment=action switch {DashboardAction.Enlarge=>"Larger · Enlarge the whole table",DashboardAction.Shrink=>"Smaller · Shrink the whole table",DashboardAction.TurnLeft=>"Left · View the side of the scene",DashboardAction.TurnRight=>"Right · Change the viewing angle",DashboardAction.BringCloser=>"Closer · Move the table toward the player",_=>"Farther · Make room for an overview"};
                panel.Invoke(action);panel.Invoke(action);yield return new WaitForSeconds(2.5f);
            }
            panel.Invoke(DashboardAction.Enlarge);panel.Invoke(DashboardAction.TurnRight);panel.Invoke(DashboardAction.BringCloser);yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.ResetView);status.segment="Reset view · Keep weather progress, restore table position and size";yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.Details);status.segment="Details · Key stats at a glance";yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.Settings);status.segment="Settings · Standalone control panel";yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.Home);
            yield return ArtView(new Vector3(72,70,-91),Vector3.zero,"Low-poly overview · Layered coast and connected settlements");
            yield return ArtView(new Vector3(20,24,-29),new Vector3(0,2,-2),"Village close-up · Foundations, paths, gardens and residents");
            yield return ArtView(new Vector3(-20,20,-38),new Vector3(-30,2,-16),"Farm close-up · Three crops and spinning windmill blades");
            yield return ArtView(new Vector3(43,13,-26),new Vector3(31,1,-10),"Lakeshore close-up · Bridge supports, water wheel and animated water");
            foreach(var type in new[]{WeatherType.Wind,WeatherType.Rain,WeatherType.Electric,WeatherType.Snow})
            {
                manager.ResetWorld();var point=Center(type==WeatherType.Wind?BiomeType.Forest:type==WeatherType.Rain?BiomeType.Lowland:BiomeType.Town);CloseView(point);
                yield return Cast(type,point,type==WeatherType.Electric?8:11,type+" · Casting, ground contact and disaster growth");
                status.segment=type+" · Fades naturally after release";yield return new WaitForSeconds(3);
            }
            manager.ResetWorld();var forest=Center(BiomeType.Forest);CloseView(forest);
            yield return Cast(WeatherType.Heat,forest,11,"Heat · Drought, ignition and smoke");
            yield return Cast(WeatherType.Wind,forest,9,"Wind on fire · Sustained vortex and fire tornado");
            yield return Cast(WeatherType.Rain,forest,12,"Rain on fire · Steam, wet ground and fade-out");
            status.segment="Cast stopped · Trails linger";yield return new WaitForSeconds(5);
            manager.ResetWorld();camera.transform.SetPositionAndRotation(new Vector3(0,1.62f,0),Quaternion.identity);mr.BeginEditorPreview();camera.transform.rotation=Quaternion.Euler(25,0,0);camera.fieldOfView=70;panel.Canvas.enabled=true;
            status.segment="Reset complete · Panel and table stay independent";yield return new WaitForSeconds(3);
            recording=false;
        }
        private void OnGUI()
        {
            if(!recording)return;
            var style=new GUIStyle(GUI.skin.label) {font=font,fontSize=25,alignment=TextAnchor.MiddleLeft};style.normal.textColor=Color.white;
            GUI.Box(new Rect(18,16,Screen.width-36,46),GUIContent.none);
            GUI.Label(new Rect(34,16,Screen.width-68,46),"EDITOR PLAYBACK  |  "+status.segment,style);
        }
        private void OnDestroy() {recording=false;if(timing!=null){timing.Dispose();timing=null;}}
    }
}
#endif
