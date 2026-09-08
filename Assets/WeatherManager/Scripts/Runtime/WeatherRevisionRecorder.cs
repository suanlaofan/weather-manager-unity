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
            status.segment="空间控制台 · Editor 鼠标与视角预览";yield return new WaitForSeconds(3);
            foreach(var action in new[]{DashboardAction.Enlarge,DashboardAction.Shrink,DashboardAction.TurnLeft,DashboardAction.TurnRight,DashboardAction.BringCloser,DashboardAction.MoveFarther})
            {
                status.segment=action switch {DashboardAction.Enlarge=>"放大按钮 · 整体沙盘放大",DashboardAction.Shrink=>"缩小按钮 · 整体沙盘缩小",DashboardAction.TurnLeft=>"左转按钮 · 查看场景侧面",DashboardAction.TurnRight=>"右转按钮 · 调整观察方向",DashboardAction.BringCloser=>"拉近按钮 · 沙盘向玩家移动",_=>"推远按钮 · 留出整体观察空间"};
                panel.Invoke(action);panel.Invoke(action);yield return new WaitForSeconds(2.5f);
            }
            panel.Invoke(DashboardAction.Enlarge);panel.Invoke(DashboardAction.TurnRight);panel.Invoke(DashboardAction.BringCloser);yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.ResetView);status.segment="视角复位 · 保留天气进度，恢复沙盘位置与大小";yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.Details);status.segment="详情页 · 常用信息集中阅读";yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.Settings);status.segment="设置页 · 独立操作面板";yield return new WaitForSeconds(2);
            panel.Invoke(DashboardAction.Home);
            yield return ArtView(new Vector3(72,70,-91),Vector3.zero,"Low-poly 全景 · 分层海岸与连通聚落");
            yield return ArtView(new Vector3(20,24,-29),new Vector3(0,2,-2),"村庄近景 · 建筑基座、步道、花园与居民");
            yield return ArtView(new Vector3(-20,20,-38),new Vector3(-30,2,-16),"农田近景 · 三种作物与独立风车叶片");
            yield return ArtView(new Vector3(43,13,-26),new Vector3(31,1,-10),"湖岸近景 · 桥梁支撑、水轮与水面动画");
            foreach(var type in new[]{WeatherType.Wind,WeatherType.Rain,WeatherType.Electric,WeatherType.Snow})
            {
                manager.ResetWorld();var point=Center(type==WeatherType.Wind?BiomeType.Forest:type==WeatherType.Rain?BiomeType.Lowland:BiomeType.Town);CloseView(point);
                yield return Cast(type,point,type==WeatherType.Electric?8:11,type+" · 施法、地表接触与灾害发展");
                status.segment=type+" · 松开后自然消散";yield return new WaitForSeconds(3);
            }
            manager.ResetWorld();var forest=Center(BiomeType.Forest);CloseView(forest);
            yield return Cast(WeatherType.Heat,forest,11,"高温 · 干燥、起火与烟雾");
            yield return Cast(WeatherType.Wind,forest,9,"火场加风 · 连续旋涡与火龙卷");
            yield return Cast(WeatherType.Rain,forest,12,"降雨灭火 · 蒸汽、湿地与消散");
            status.segment="停止施法 · 尾迹保留";yield return new WaitForSeconds(5);
            manager.ResetWorld();camera.transform.SetPositionAndRotation(new Vector3(0,1.62f,0),Quaternion.identity);mr.BeginEditorPreview();camera.transform.rotation=Quaternion.Euler(25,0,0);camera.fieldOfView=70;panel.Canvas.enabled=true;
            status.segment="重置完成 · 面板和沙盘保持独立";yield return new WaitForSeconds(3);
            recording=false;
        }
        private void OnGUI()
        {
            if(!recording)return;
            var style=new GUIStyle(GUI.skin.label) {font=font,fontSize=25,alignment=TextAnchor.MiddleLeft};style.normal.textColor=Color.white;
            GUI.Box(new Rect(18,16,Screen.width-36,46),GUIContent.none);
            GUI.Label(new Rect(34,16,Screen.width-68,46),"EDITOR 运行画面  |  "+status.segment,style);
        }
        private void OnDestroy() {recording=false;if(timing!=null){timing.Dispose();timing=null;}}
    }
}
#endif
