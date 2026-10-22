#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace WeatherManager.Editor
{
    public sealed class WeatherRevisionRuntimeChecks : MonoBehaviour
    {
        [Serializable] private sealed class Report { public string status; public string[] checks; public string failure; public int resets; }
        private readonly List<string> checks=new List<string>();
        private readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[300];
        private readonly Vector3[] corners=new Vector3[4];
        private Report report=new Report {status="running"};
        private GameManager manager;
        private WeatherCastingController casting;
        private WeatherParticlePresentation fx;
        private MrTabletopExperience mr;
        private Mouse testMouse;
        private const string PathName="Evidence/Revision/runtime-checks.json";
        public void Run() { Directory.CreateDirectory("Evidence/Revision");Save();StartCoroutine(Safe(CheckSequence())); }
        private IEnumerator Safe(IEnumerator routine)
        {
            while(true)
            {
                object step;
                try { if(!routine.MoveNext())break;step=routine.Current; }
                catch(Exception error) { report.status="failed";report.failure=error.ToString();Debug.LogError("WEATHER_MANAGER_RUNTIME_CHECKS_FAIL:"+error.Message);Cleanup();Save();yield break; }
                yield return step;
            }
            report.status="passed";Cleanup();Save();Debug.Log("WEATHER_MANAGER_RUNTIME_CHECKS_PASS:checks="+checks.Count+":resets="+report.resets);
        }
        private void Cleanup() { if(testMouse!=null)InputSystem.RemoveDevice(testMouse);casting?.SetReviewCast(null,Vector3.zero);manager?.SetTimeScale(1); }
        private void Check(bool condition,string name) { if(!condition)throw new InvalidOperationException(name);checks.Add(name);Save(); }
        private void Save() {report.checks=checks.ToArray();File.WriteAllText(PathName,JsonUtility.ToJson(report,true));}
        private IEnumerator CheckSequence()
        {
            manager=Object.FindFirstObjectByType<GameManager>();casting=Object.FindFirstObjectByType<WeatherCastingController>();
            mr=Object.FindFirstObjectByType<MrTabletopExperience>();fx=Object.FindFirstObjectByType<WeatherParticlePresentation>();
            var panel=Object.FindFirstObjectByType<SpatialWeatherDashboard>();var camera=Camera.main;
            Check(mr.IsMrActive&&panel.IsVisible,"Spatial preview is active");
            var anchor=panel.transform.parent;var position=anchor.position;var rotation=anchor.rotation;
            var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;
            foreach(var yaw in new[]{-30f,30f,0f})
            {
                camera.transform.rotation=Quaternion.Euler(25,yaw,0);yield return null;yield return null;
                Check(Vector3.Distance(position,anchor.position)<.00001f&&Quaternion.Angle(rotation,anchor.rotation)<.001f,"Head turn does not move UI "+yaw);
            }
            camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            var island=mr.TabletopIsland;var originalScale=island.localScale;
            foreach(var scale in new[]{.01f,.018f,.04f})
            {
                island.localScale=Vector3.one*scale;yield return null;Physics.SyncTransforms();
                Check(Mathf.Abs(panel.Canvas.transform.lossyScale.x*800-.8f)<.001f,"Map scale leaves UI .8m: "+scale);
                Check(fx.Layers.All(p=>p.main.customSimulationSpace==island&&p.main.scalingMode==ParticleSystemScalingMode.Hierarchy),"Particle scale and island reference: "+scale);
                var terrain=Object.FindFirstObjectByType<LowPolyTerrainGenerator>();
                var groundCollider=terrain.GetComponentsInChildren<MeshCollider>().First(c=>c.name=="Triangulated Heightfield");
                foreach(var p in new[]{new Vector3(-31,0,-20),new Vector3(3,0,-4),new Vector3(-27,0,12),new Vector3(36,0,-10),new Vector3(5,0,-25)})
                {
                    var ray=new Ray(island.TransformPoint(p+Vector3.up*60),-island.up);
                    Check(groundCollider.Raycast(ray,out var hit,120*scale)&&Mathf.Abs(island.InverseTransformPoint(hit.point).y-terrain.SampleRenderedHeight(p))<.002f,"Moving-object height matches rendered triangles: "+p+"/"+scale);
                }
                foreach (var rock in island.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Coast Rock")))
                {
                    var expected=rock.transform.TransformPoint(rock.GetComponent<MeshFilter>().sharedMesh.bounds.center);
                    Check(!rock.isPartOfStaticBatch&&Vector3.Distance(expected,rock.bounds.center)<.001f,"Coast renderer follows MR scale: "+rock.name+"/"+scale);
                }
            }
            island.localScale=originalScale;
            foreach(var language in new[]{AppLanguage.English})
            {
                LocalizationService.SetLanguage(language);
                foreach(var page in new[]{DashboardAction.Home,DashboardAction.Details,DashboardAction.Settings,DashboardAction.Reset})
                {
                    panel.Invoke(page);yield return null;Canvas.ForceUpdateCanvases();
                    foreach(var label in panel.GetComponentsInChildren<Text>())
                        Check(label.preferredHeight<=label.rectTransform.rect.height+2,"Text fits "+language+"/"+page+"/"+label.name);
                }
            }
            LocalizationService.SetLanguage(AppLanguage.English);panel.Invoke(DashboardAction.Home);
            var resident=Object.FindFirstObjectByType<ResidentAgent>();var limb=resident.transform.Find("Left Leg");var firstAngle=limb.localRotation;
            yield return new WaitForSeconds(.6f);
            Check(Object.FindObjectsByType<ResidentMotionController>(FindObjectsSortMode.None).Length==84,"84 residents have shared Animator motions");
            Check(Quaternion.Angle(firstAngle,limb.localRotation)>.1f,"Resident limb moves in Play Mode");
            // Queue genuine Input System mouse events into the same spatial GraphicRaycaster route used by the emulator.
            testMouse=InputSystem.AddDevice<Mouse>("WeatherRevisionTestMouse");
            var button=panel.GetComponentsInChildren<SpatialDashboardButton>().First(b=>b.Action==DashboardAction.Rain);
            var screen=(Vector2)camera.WorldToScreenPoint(button.transform.position);
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=screen});yield return null;
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=screen}.WithButton(MouseButton.Left));yield return null;yield return null;
            Check(!casting.IsCasting,"Real UI press cannot cast through panel");
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=screen});yield return null;yield return null;
            Check(casting.SelectedWeather==WeatherType.Rain,"Real pointer press selects Rain");
            var homePosition=island.position;var homeRotation=island.rotation;var homeDistance=mr.ViewDistance;
            var fov=camera.fieldOfView;
            foreach(var action in new[]{DashboardAction.Enlarge,DashboardAction.Shrink,DashboardAction.TurnLeft,DashboardAction.TurnRight,DashboardAction.BringCloser,DashboardAction.MoveFarther})
            {
                var priorScale=island.localScale.x;var priorYaw=mr.ViewYaw;var priorDistance=mr.ViewDistance;
                var viewButton=panel.GetComponentsInChildren<SpatialDashboardButton>().First(b=>b.Action==action);
                var viewScreen=(Vector2)camera.WorldToScreenPoint(viewButton.transform.position);
                InputSystem.QueueStateEvent(testMouse,new MouseState {position=viewScreen});yield return null;
                InputSystem.QueueStateEvent(testMouse,new MouseState {position=viewScreen}.WithButton(MouseButton.Left));yield return null;yield return null;
                Check(!casting.IsCasting,"View button cannot cast through UI: "+action);
                InputSystem.QueueStateEvent(testMouse,new MouseState {position=viewScreen});yield return new WaitForSeconds(.65f);
                Check(action switch {
                    DashboardAction.Enlarge=>island.localScale.x>priorScale+.001f,
                    DashboardAction.Shrink=>island.localScale.x<priorScale-.001f,
                    DashboardAction.TurnLeft=>Mathf.Abs(mr.ViewYaw-priorYaw+15)<.01f,
                    DashboardAction.TurnRight=>Mathf.Abs(mr.ViewYaw-priorYaw-15)<.01f,
                    DashboardAction.BringCloser=>mr.ViewDistance<priorDistance-.09f,
                    _=>mr.ViewDistance>priorDistance+.09f},"Real pointer changes tabletop: "+action);
                Check(Vector3.Distance(position,anchor.position)<.00001f&&Quaternion.Angle(rotation,anchor.rotation)<.001f,"View adjustment leaves panel anchored: "+action);
                Check(Vector3.Distance(cameraPosition,camera.transform.position)<.0001f&&Quaternion.Angle(cameraRotation,camera.transform.rotation)<.001f&&Mathf.Abs(camera.fieldOfView-fov)<.001f,"View adjustment preserves HMD pose and projection: "+action);
            }
            Check(Vector3.Distance(homePosition,island.position)<.001f&&Quaternion.Angle(homeRotation,island.rotation)<.01f,"Opposite buttons return to the starting pose");
            manager.SetTimeScale(0);panel.Invoke(DashboardAction.Enlarge);panel.Invoke(DashboardAction.TurnRight);panel.Invoke(DashboardAction.BringCloser);
            yield return new WaitForSeconds(.7f);Physics.SyncTransforms();
            Check(island.localScale.x>originalScale.x+.001f&&Quaternion.Angle(homeRotation,island.rotation)>14.9f&&mr.ViewDistance<homeDistance-.09f,"Scene controls remain available while simulation is paused");
            var ground=Object.FindFirstObjectByType<LowPolyTerrainGenerator>();
            var transformedCollider=ground.GetComponentsInChildren<MeshCollider>().First(c=>c.name=="Triangulated Heightfield");
            var localPoint=new Vector3(-31,0,-20);
            var transformedRay=new Ray(island.TransformPoint(localPoint+Vector3.up*60),-island.up);
            Check(transformedCollider.Raycast(transformedRay,out var transformedHit,4)&&Mathf.Abs(island.InverseTransformPoint(transformedHit.point).y-ground.SampleRenderedHeight(localPoint))<.002f,"Rotated scaled and translated terrain retains accurate collisions");
            Check(mr.TryRaycastGameplay(transformedRay,Object.FindFirstObjectByType<DisasterSimulation>(),out var hitPoint)&&Vector2.Distance(new Vector2(hitPoint.x,hitPoint.z),new Vector2(localPoint.x,localPoint.z))<.01f,"Controller ray still resolves authored gameplay coordinates after view changes");
            ScreenCapture.CaptureScreenshot("Evidence/Revision/tabletop-adjusted-gameview.png");yield return null;
            var adjustedPosition=island.position;var adjustedRotation=island.rotation;var adjustedScale=island.localScale;
            manager.ResetWorld();yield return null;
            Check(Vector3.Distance(adjustedPosition,island.position)<.0001f&&Quaternion.Angle(adjustedRotation,island.rotation)<.01f&&Vector3.Distance(adjustedScale,island.localScale)<.00001f,"Reset town preserves the chosen viewing pose");
            var elapsedBeforeResetView=manager.Stats.Elapsed;manager.SetTimeScale(0);manager.SetWeather(WeatherType.Snow);
            var resetButton=panel.GetComponentsInChildren<SpatialDashboardButton>().First(b=>b.Action==DashboardAction.ResetView);
            var resetScreen=(Vector2)camera.WorldToScreenPoint(resetButton.transform.position);
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=resetScreen});yield return null;
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=resetScreen}.WithButton(MouseButton.Left));yield return null;
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=resetScreen});yield return new WaitForSeconds(.7f);
            Check(Vector3.Distance(homePosition,island.position)<.0001f&&Quaternion.Angle(homeRotation,island.rotation)<.01f&&Vector3.Distance(originalScale,island.localScale)<.00001f,"Real reset-view button restores all three adjustments");
            Check(Mathf.Abs(manager.Stats.Elapsed-elapsedBeforeResetView)<.01f&&casting.SelectedWeather==WeatherType.Snow&&manager.TimeScale==0,"Reset view preserves simulation time weather selection and pause");
            foreach(var action in new[]{DashboardAction.Enlarge,DashboardAction.Shrink,DashboardAction.MoveFarther,DashboardAction.BringCloser})
            {
                for(var n=0;n<40;n++)panel.Invoke(action);yield return new WaitForSeconds(.8f);
                Check(action switch {DashboardAction.Enlarge=>mr.ViewScalePercent==160,DashboardAction.Shrink=>mr.ViewScalePercent==50,DashboardAction.MoveFarther=>Mathf.Abs(mr.ViewDistance-3)<.001f,_=>Mathf.Abs(mr.ViewDistance-.85f)<.001f},"Repeated presses respect view boundary: "+action);
            }
            for(var n=0;n<24;n++)panel.Invoke(DashboardAction.TurnRight);yield return new WaitForSeconds(.7f);
            Check(Mathf.Abs(mr.ViewYaw)<.01f&&Quaternion.Angle(homeRotation,island.rotation)<.01f,"A full button rotation wraps cleanly through 360 degrees");
            panel.Invoke(DashboardAction.ResetView);manager.SetTimeScale(1);yield return new WaitForSeconds(.8f);
            Check(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).All(c=>!c.name.Contains("Magnifier"))&&panel.GetComponentsInChildren<RawImage>(true).Length==0,"Magnifier camera and overlay are absent");
            var windmill=island.GetComponentsInChildren<Transform>().First(t=>t.name=="Windmill 01");
            var tower=windmill.Find("Windmill Tower");var sail=windmill.Find("Sail axle");var towerPose=tower.rotation;var sailPose=sail.localRotation;
            var waterwheel=island.GetComponentsInChildren<Transform>().First(t=>t.name=="WaterWheel 01");var wheelPose=waterwheel.rotation;var wheelAxle=waterwheel.Find("Wheel axle");var initialWheel=wheelAxle.localRotation;
            yield return new WaitForSeconds(.7f);
            Check(Quaternion.Angle(tower.rotation,towerPose)<.001f&&Quaternion.Angle(sail.localRotation,sailPose)>1,"Windmill tower stays fixed while independent sails rotate");
            Check(Quaternion.Angle(waterwheel.rotation,wheelPose)<.001f&&Quaternion.Angle(wheelAxle.localRotation,initialWheel)>1,"Waterwheel rotates at its axle without moving the mount");
            var handle=panel.GetComponentInChildren<SpatialDashboardDragHandle>();
            var dragStart=(Vector2)camera.WorldToScreenPoint(handle.transform.position);
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=dragStart});yield return null;
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=dragStart}.WithButton(MouseButton.Left));yield return null;
            var dragEnd=dragStart+new Vector2(90,35);
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=dragEnd}.WithButton(MouseButton.Left));yield return null;yield return null;
            Check(panel.IsDragging&&Vector3.Distance(position,anchor.position)>.015f&&!casting.IsCasting,"Actual pointer drag moves title without casting");
            var duringDrag=anchor.position;mr.RecallDashboard();
            Check(Vector3.Distance(duringDrag,anchor.position)<.0001f,"Recall does not move a pressed drag target");
            InputSystem.QueueStateEvent(testMouse,new MouseState {position=dragEnd});yield return null;yield return null;
            Check(!panel.IsDragging,"Pointer release ends dragging");
            mr.RecallDashboard();yield return null;
            Check(Vector3.Distance(position,anchor.position)<.001f,"Recall restores a reachable panel after dragging");
            InputSystem.RemoveDevice(testMouse);testMouse=null;
            var point=new Vector3(0,Object.FindFirstObjectByType<DisasterSimulation>().SampleGroundHeight(Vector3.zero),0);
            casting.SetReviewCast(WeatherType.Snow,point);yield return new WaitForSeconds(.45f);
            var layer=fx.Layers[3];var count=layer.GetParticles(particles);
            Check(count>0,"Snow emits visible particles");
            var castLine=Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None).First(l=>l.name=="CastTrajectory");
            Check(castLine.widthMultiplier<.003f,"Cast trajectory width respects small MR scale");
            var seed=particles[0].randomSeed;var prior=particles[0].position;
            casting.SetReviewCast(WeatherType.Snow,point+Vector3.right*12);yield return new WaitForSeconds(.12f);
            count=layer.GetParticles(particles);var found=false;
            for(var i=0;i<count;i++)if(particles[i].randomSeed==seed) {found=true;Check(Vector3.Distance(prior,particles[i].position)<1.2f,"Old snow does not follow 12-unit cursor move");break;}
            Check(found,"Tracked emitted particle survives cursor move");
            casting.SetReviewCast(null,point);Check(layer.particleCount>0,"Release retains snow tail");
            manager.SetTimeScale(0);layer.GetParticles(particles);var frozen=particles[0].position;var frozenCount=layer.particleCount;
            var frozenLimb=limb.localRotation;
            yield return new WaitForSeconds(.25f);layer.GetParticles(particles);
            Check(layer.particleCount==frozenCount&&Vector3.Distance(particles[0].position,frozen)<.0001f,"Pause freezes existing particles");
            Check(Quaternion.Angle(frozenLimb,limb.localRotation)<.001f,"Pause freezes resident animation");
            manager.SetTimeScale(1);yield return new WaitForSeconds(3.8f);
            Check(layer.particleCount==0,"Snow tail finishes naturally");
            manager.ResetWorld();yield return null;
            var objectCount=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
            var materialCount=Resources.FindObjectsOfTypeAll<Material>().Length;
            for(var i=0;i<20;i++)
            {
                casting.SetReviewCast((WeatherType)(i%5),point);yield return new WaitForSeconds(.18f);
                casting.SetReviewCast(null,point);manager.ResetWorld();yield return null;
                Check(fx.Layers.All(p=>p.particleCount==0)&&fx.PendingContacts==0,"Reset clears particles and delayed contacts "+i);
                Check(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objectCount,"No new objects after reset "+i);
                Check(Vector3.Distance(cameraPosition,camera.transform.position)<.0001f&&Quaternion.Angle(cameraRotation,camera.transform.rotation)<.001f,"Reset preserves MR viewpoint "+i);
                report.resets=i+1;
            }
            Check(Resources.FindObjectsOfTypeAll<Material>().Length<=materialCount+2,"Materials do not grow across 20 resets");
            Check(manager.Stats.AliveResidents==84&&manager.Stats.DestroyedObjects==0,"Reset restores city statistics");
            yield return null;
        }
    }
}

#endif
