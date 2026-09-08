using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WeatherManager.Editor
{
    [McpForUnityTool("weather_manager_revision", Description = "Inspect, bake and validate the Weather Manager revision in its own scene.")]
    public static class WeatherManagerRevisionTools
    {
        private const string ExpectedScene = "Assets/WeatherManager/Scenes/WeatherManager.unity";
        public static object HandleCommand(JObject parameters)
        {
            if (SceneManager.GetActiveScene().path != ExpectedScene)
                return new ErrorResponse("Open the WeatherManager scene before operating this tool.");
            var action = (string)parameters["action"] ?? "inspect";
            try
            {
                switch (action)
                {
                    case "inspect": return new SuccessResponse("Revision scene references", Inspect());
                    case "configure_application_icon":
                        WeatherApplicationIcon.Configure();return new SuccessResponse("Game application icon configured and validated");
                    case "audit_scene": return new SuccessResponse("Model grounding audit", WeatherScenePolishBuilder.Audit());
                    case "bake_scene_polish":return new SuccessResponse("Low-poly diorama refined",WeatherScenePolishBuilder.Build());
                    case "bake_vehicle_pose":
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake only in Edit Mode.");
                        var vehicleWorld=Object.FindFirstObjectByType<WorldBuilder>();
                        var vehicleCount=vehicleWorld.AlignBakedVehiclesToRoads();
                        EditorUtility.SetDirty(vehicleWorld);
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                        return new SuccessResponse("Vehicle initial heading saved",new {vehicleCount});
                    case "bake_mobility":
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake only in Edit Mode.");
                        var changed = ClearIslandStaticFlags(Object.FindFirstObjectByType<MrTabletopExperience>().TabletopIsland);
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                        return new SuccessResponse("Movable MR island saved", new { changed });
                    case "bake_ui": BakeUi(); return new SuccessResponse("Spatial UI saved", Inspect());
                    case "bake_ui_skin":
                        WeatherDashboardSkinBuilder.Build();
                        var skinUi=Object.FindFirstObjectByType<UIController>(FindObjectsInactive.Include);
                        skinUi.SpatialPanel.Rebuild(skinUi,Resources.Load<Font>("WeatherManager/Fonts/NotoSansCJKsc-Regular"));
                        BakeUi();return new SuccessResponse("Nano Banana spatial UI saved",Inspect());
                    case "bake_environment":
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake only in Edit Mode.");
                        var count = WeatherEnvironmentAssetBuilder.Build(Object.FindFirstObjectByType<WorldBuilder>(),Object.FindFirstObjectByType<DisasterSimulation>());
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
                        return new SuccessResponse("Environment presentation baked", new { objects = count });
                    case "bake_motion":
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake only in Edit Mode.");
                        var residents = Object.FindObjectsByType<ResidentAgent>(FindObjectsSortMode.None);
                        var library = ResidentMotionAssetBuilder.Build(residents[0]);
                        foreach (var resident in residents)
                        {
                            var motion = resident.GetComponent<ResidentMotionController>();
                            if (motion == null) motion = resident.gameObject.AddComponent<ResidentMotionController>();
                            motion.Configure(library); EditorUtility.SetDirty(resident);
                        }
                        PrefabUtility.SaveAsPrefabAsset(residents[0].gameObject, "Assets/WeatherManager/Prefabs/AnimatedResident.prefab");
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
                        return new SuccessResponse("Resident animations baked", new { residents = residents.Length, clips = library.animationClips.Length });
                    case "bake_vfx":
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake only in Edit Mode.");
                        var settings = WeatherPresentationAssetBuilder.Build();
                        var vfx = Object.FindFirstObjectByType<WeatherVfxController>();
                        vfx.PrepareBakedScene(vfx.transform, Camera.main);
                        vfx.PreparePresentation(settings);
                        var cursorMesh = vfx.GetComponentsInChildren<MeshFilter>(true).First(f => f.name == "CastCursor");
                        if (!AssetDatabase.Contains(cursorMesh.sharedMesh))
                        {
                            const string cursorPath = "Assets/WeatherManager/Generated/Meshes/WeatherTargetOutline.asset";
                            var oldOutline=AssetDatabase.LoadAssetAtPath<Mesh>(cursorPath);
                            if (oldOutline == null) AssetDatabase.CreateAsset(cursorMesh.sharedMesh,cursorPath);
                            else {EditorUtility.CopySerialized(cursorMesh.sharedMesh,oldOutline);cursorMesh.sharedMesh=oldOutline;}
                        }
                        foreach (var filter in vfx.Presentation.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (filter.sharedMesh != null && !AssetDatabase.Contains(filter.sharedMesh))
                                AssetDatabase.CreateAsset(filter.sharedMesh, "Assets/WeatherManager/Generated/Meshes/ContinuousFunnel.asset");
                        }
                        var surface = Object.FindFirstObjectByType<WeatherSurfacePresentation>();
                        if (surface != null) foreach (var filter in surface.GetComponentsInChildren<MeshFilter>(true))
                            if (filter.sharedMesh != null && !AssetDatabase.Contains(filter.sharedMesh))
                            {
                                var meshPath = "Assets/WeatherManager/Generated/Meshes/" + filter.name.Replace(" ", "") + ".asset";
                                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                                if (existing == null) AssetDatabase.CreateAsset(filter.sharedMesh, meshPath);
                                else { EditorUtility.CopySerialized(filter.sharedMesh, existing); filter.sharedMesh = existing; }
                            }
                        EditorUtility.SetDirty(vfx);
                        PrefabUtility.SaveAsPrefabAsset(vfx.Presentation.gameObject, "Assets/WeatherManager/Prefabs/WeatherPresentation.prefab");
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                        AssetDatabase.SaveAssets();
                        return new SuccessResponse("Weather presentation baked", new { layers = vfx.Presentation.Layers.Length });
                    case "validate": return new SuccessResponse("Revision checks passed", Validate());
                    case "preview":
                        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Preview requires Play Mode.");
                        var mr = Object.FindFirstObjectByType<MrTabletopExperience>();
                        var camera = Camera.main;
                        camera.transform.SetPositionAndRotation(new Vector3(0, 1.62f, 0), Quaternion.identity);
                        mr.BeginEditorPreview();
                        Object.FindFirstObjectByType<SpatialWeatherDashboard>().Canvas.enabled=true;
                        camera.fieldOfView = 70f;
                        camera.transform.rotation = Quaternion.Euler(25f, 0, 0);
                        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
                        return new SuccessResponse("Editor spatial preview active", Inspect());
                    case "review_view":
                        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Review requires Play Mode.");
                        var viewIsland=Object.FindFirstObjectByType<MrTabletopExperience>().TabletopIsland;
                        var from=new Vector3((float?)parameters["x"]??72,(float?)parameters["y"]??70,(float?)parameters["z"]??-91);
                        var to=new Vector3((float?)parameters["tx"]??0,(float?)parameters["ty"]??0,(float?)parameters["tz"]??0);
                        var view=Camera.main;view.transform.SetPositionAndRotation(viewIsland.TransformPoint(from),Quaternion.LookRotation(viewIsland.TransformDirection(to-from),viewIsland.up));
                        view.fieldOfView=(float?)parameters["fov"]??48;
                        Object.FindFirstObjectByType<SpatialWeatherDashboard>().Canvas.enabled=false;
                        return new SuccessResponse("Editor review camera positioned");
                    case "review_cast":
                        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Review requires Play Mode.");
                        var casting = Object.FindFirstObjectByType<WeatherCastingController>();
                        var weatherName = (string)parameters["weather"];
                        var castPoint = new Vector3((float?)parameters["x"] ?? 0, 0, (float?)parameters["z"] ?? 0);
                        castPoint.y = Object.FindFirstObjectByType<DisasterSimulation>().SampleGroundHeight(castPoint);
                        casting.SetReviewCast(weatherName == "stop" ? (WeatherType?)null : (WeatherType)Enum.Parse(typeof(WeatherType), weatherName, true), castPoint);
                        return new SuccessResponse("Editor review cast updated");
                    case "runtime_checks":
                        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Checks require Play Mode.");
                        var runner = new GameObject("Revision runtime validation").AddComponent<WeatherRevisionRuntimeChecks>();
                        runner.Run(); return new SuccessResponse("Runtime validation started");
                    case "review_sequence":
                        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Review requires Play Mode.");
                        var directory = (string)parameters["path"];
                        if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory)) throw new ArgumentException("Absolute recording path required.");
                        new GameObject("Revision Game View recording").AddComponent<WeatherRevisionRecorder>().Begin(directory);
                        return new SuccessResponse("Game View sequence started", new { directory });
                    case "build_revision":
                        if (EditorApplication.isPlaying) throw new InvalidOperationException("Build only in Edit Mode.");
                        EditorApplication.delayCall -= PicoAndroidBuildPipeline.BuildRevisionApk;
                        PicoAndroidBuildPipeline.QueueRevisionBuild();
                        return new SuccessResponse("Revision APK build queued");
                    case "capture":
                        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Game capture requires Play Mode.");
                        var path = (string)parameters["path"];
                        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) throw new ArgumentException("Absolute screenshot path required.");
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        ScreenCapture.CaptureScreenshot(path);
                        return new SuccessResponse("Game View capture queued", new { path });
                    case "page":
                        var panel = Object.FindFirstObjectByType<SpatialWeatherDashboard>(FindObjectsInactive.Include);
                        panel.Invoke((DashboardAction)Enum.Parse(typeof(DashboardAction), (string)parameters["page"], true));
                        return new SuccessResponse("Dashboard action invoked");
                    default: return new ErrorResponse("Unknown revision action: " + action);
                }
            }
            catch (Exception e) { return new ErrorResponse(e.ToString()); }
        }

        [MenuItem("Weather Manager/Revision/Bake Spatial UI")]
        public static void BakeUi()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake only in Edit Mode.");
            var mr = Object.FindFirstObjectByType<MrTabletopExperience>();
            var ui = Object.FindFirstObjectByType<UIController>(FindObjectsInactive.Include);
            var serialized = new SerializedObject(mr);
            serialized.FindProperty("dashboardHeightOffset").floatValue = -.12f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var anchor = (Transform)serialized.FindProperty("spatialDashboard").objectReferenceValue;
            var island = (Transform)serialized.FindProperty("tabletopIsland").objectReferenceValue;
            var camera = (Camera)serialized.FindProperty("hmdCamera").objectReferenceValue;
            if (anchor == null || island == null || camera == null || ui == null) throw new InvalidOperationException("Existing scene references must resolve.");
            Undo.RegisterFullObjectHierarchyUndo(anchor.gameObject, "Migrate weather spatial dashboard");
            anchor.SetParent(mr.transform, false);
            anchor.name = "Spatial UI Anchor";
            anchor.localPosition = new Vector3(0, 1.50f, 1.2f);
            anchor.localRotation = Quaternion.Euler(5.71f, 0, 0);
            anchor.localScale = Vector3.one;
            ui.BuildEditableSpatialPanel(anchor);
            foreach (var icon in ui.SpatialPanel.GetComponentsInChildren<WeatherIconGraphic>(true))
                if (icon.GetComponent<CanvasRenderer>() == null) icon.gameObject.AddComponent<CanvasRenderer>();
            ui.PrepareSpatialInput(mr.transform);
            mr.Configure(island, anchor, camera, ui);
            BakeControllerRay(mr.transform);
            EditorUtility.SetDirty(ui);
            EditorUtility.SetDirty(mr);
            var folder = "Assets/WeatherManager/Prefabs/Spatial";
            Directory.CreateDirectory(folder);
            PrefabUtility.SaveAsPrefabAsset(ui.SpatialPanel.gameObject, folder + "/SpatialWeatherDashboard.prefab");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public static void BakeControllerRay(Transform rig)
        {
            var path = "Assets/WeatherManager/Generated/Materials/TrackedPointer.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("WeatherManager/Weather Particle"));
                AssetDatabase.CreateAsset(material, path);
            }
            foreach (var line in rig.GetComponentsInChildren<LineRenderer>(true))
            {
                if (line.name != "Right Hand Ray" && line.name != "Left Hand Ray") continue;
                line.sharedMaterial = material;
                line.startWidth = .0018f; line.endWidth = .0008f;
                line.startColor = new Color(.3f,.83f,1f,.65f); line.endColor = new Color(.6f,.92f,1f,.18f);
                line.enabled = false;
                var controller = line.GetComponent<TrackedWeatherPointer>();
                if (controller == null) controller = line.gameObject.AddComponent<TrackedWeatherPointer>();
                controller.Configure(line, rig, line.name == "Right Hand Ray");
            }
        }

        private static object Inspect()
        {
            var mr = Object.FindFirstObjectByType<MrTabletopExperience>();
            var ui = Object.FindFirstObjectByType<UIController>(FindObjectsInactive.Include);
            var resident = Object.FindFirstObjectByType<ResidentAgent>();
            var panel = ui != null ? ui.SpatialPanel : null;
            return new {
                scene = SceneManager.GetActiveScene().path, playing = EditorApplication.isPlaying,
                mrActive = mr != null && mr.IsMrActive,
                ui = ui != null ? ui.name : null,
                panel = panel != null ? new { parent = panel.transform.parent.name,
                    ancestor = panel.transform.parent.parent.name, size = Vector3.Scale(new Vector3(800,SpatialWeatherDashboard.LogicalSize.y,0),panel.Canvas.transform.lossyScale).ToString(),
                    position = panel.transform.position.ToString(), visible = panel.IsVisible,
                    font = panel.GetComponentInChildren<Text>(true).font.name,
                    buttons = panel.GetComponentsInChildren<SpatialDashboardButton>(true).Length } : null,
                resident = resident != null ? resident.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray() : Array.Empty<string>(),
                highRenderers = EditorApplication.isPlaying ? Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy && Camera.main.WorldToViewportPoint(r.bounds.center).y > .75f)
                    .Take(12).Select(r => new { name=r.name, parent=r.transform.parent != null ? r.transform.parent.name : "", position=r.bounds.center.ToString(), transform=r.transform.position.ToString(), scale=r.transform.lossyScale.ToString(), batched=r.isPartOfStaticBatch }).ToArray() : null
            };
        }

        public static int ClearIslandStaticFlags(Transform island)
        {
            if (island == null) throw new InvalidOperationException("MR island missing.");
            var changed = 0;
            foreach (var child in island.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(child.gameObject) == 0) continue;
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
                changed++;
            }
            return changed;
        }

        public static object Validate()
        {
            var checks = new List<string>();
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
            var gate = new WeatherPressGate();
            bool begin;
            Check(!gate.Read(true, true, true, false, out begin), "UI press never casts");
            Check(!gate.Read(true, false, true, false, out begin), "UI to map hold remains captured");
            gate.Read(false, false, true, false, out begin);
            Check(gate.Read(true, false, true, false, out begin) && begin, "Fresh map press starts casting");
            Check(!gate.Read(true, true, true, false, out begin), "Map to UI terminates casting");
            Check(!gate.Read(true, false, true, false, out begin), "Leaving UI while held cannot resume");
            gate.Read(false, false, true, false, out begin);
            gate.Read(true, false, true, false, out begin);
            gate.RequireRelease();
            Check(!gate.Read(true, false, true, false, out begin), "Focus tracking reset require release");
            gate.Read(false, false, true, false, out begin);
            Check(!gate.Read(true, false, true, true, out begin), "Paused press is blocked");
            Check(!gate.Read(true, false, true, false, out begin), "Unpause while held is blocked");
            gate.Read(false, false, true, false, out begin);
            Check(!gate.Read(true, false, false, false, out begin), "Invalid target blocks input");
            Check(!gate.Read(true, false, true, false, out begin), "Reacquiring target needs fresh press");
            var panel = Object.FindFirstObjectByType<SpatialWeatherDashboard>(FindObjectsInactive.Include);
            Check(panel != null, "Spatial panel exists");
            Check(!panel.transform.IsChildOf(Camera.main.transform), "UI independent of HMD");
            Check(Object.FindFirstObjectByType<MrTabletopExperience>().TabletopIsland.GetComponentsInChildren<Transform>(true)
                .All(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) == 0), "Movable island has no static flags");
            Check(Mathf.Abs(panel.Canvas.transform.lossyScale.x * 800 - .8f) < .001f, "UI meter width .8");
            Check(panel.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("Nano Banana Icon") && i.sprite!=null)==6, "Six Nano Banana icons are bound");
            foreach(var viewAction in new[]{DashboardAction.Shrink,DashboardAction.Enlarge,DashboardAction.TurnLeft,DashboardAction.TurnRight,DashboardAction.BringCloser,DashboardAction.MoveFarther,DashboardAction.ResetView})
                Check(panel.GetComponentsInChildren<SpatialDashboardButton>(true).Count(b=>b.Action==viewAction)==1,"Unique view button: "+viewAction);
            Check(panel.GetComponentsInChildren<RawImage>(true).Length==0,"Auxiliary inspection overlay removed");
            foreach(var shader in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.shader).Distinct())Check(shader!=null&&!ShaderUtil.ShaderHasError(shader),"Scene shader compiles: "+shader.name);
            var terrain=Object.FindFirstObjectByType<LowPolyTerrainGenerator>();
            var road=terrain.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Curved Road Network").sharedMesh;
            var roadVertices=road.vertices;var roadTriangles=road.triangles;var minGap=float.PositiveInfinity;var maxGap=float.NegativeInfinity;
            for(var i=0;i<roadTriangles.Length;i+=3){var p=(roadVertices[roadTriangles[i]]+roadVertices[roadTriangles[i+1]]+roadVertices[roadTriangles[i+2]])/3;var gap=p.y-terrain.SampleRenderedHeight(p);minGap=Mathf.Min(minGap,gap);maxGap=Mathf.Max(maxGap,gap);}
            Check(minGap>.025f&&maxGap<.045f,"Every road triangle follows terrain without penetration: "+minGap.ToString("F4")+".."+maxGap.ToString("F4"));
            var rect = (RectTransform)panel.Canvas.transform;
            var corners = new Vector3[4];
            foreach (var text in panel.GetComponentsInChildren<Text>(true))
            {
                Check(!text.resizeTextForBestFit && text.fontSize >= 19, "Readable text: " + text.name);
                text.rectTransform.GetWorldCorners(corners);
                Check(corners.All(c => rect.rect.Contains((Vector2)rect.InverseTransformPoint(c) * .99999f)), "Text bounds: " + text.name);
                Check(text.font != null && text.font.name.StartsWith("Noto"), "Bundled font: " + text.name);
            }
            var json = JsonUtility.ToJson(new CheckResult { passed = checks.ToArray() }, true);
            Directory.CreateDirectory("Evidence/Revision");
            File.WriteAllText("Evidence/Revision/structural-input-checks.json", json);
            Debug.Log("WEATHER_MANAGER_REVISION_CHECKS_PASS:count=" + checks.Count);
            return new { count = checks.Count, checks };
        }
        [Serializable] private sealed class CheckResult { public string[] passed; }
    }
}
