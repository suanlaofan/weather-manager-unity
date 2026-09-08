using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherManager.Editor
{
    public static class WeatherScenePolishBuilder
    {
        public const string Folder = "Assets/WeatherManager/Generated/Polish";
        [Serializable] public sealed class GroundRecord
        {
            public string name, kind;
            public Vector3 position, min, max;
            public float ground, gap, slopeSpan;
        }
        [Serializable] public sealed class GroundReport { public int count; public GroundRecord[] records; }
        public static object Audit()
        {
            var world = Object.FindFirstObjectByType<WorldBuilder>();
            var terrain = Object.FindFirstObjectByType<LowPolyTerrainGenerator>();
            var island = Object.FindFirstObjectByType<MrTabletopExperience>().TabletopIsland;
            Physics.SyncTransforms();
            var rows = new List<GroundRecord>();
            foreach (var r in world.Damageables)
            {
                if (r.Root == null || !BoundsInIsland(r.Root, island, out var b)) continue;
                var p = island.InverseTransformPoint(r.Root.position);
                var ground = Ground(terrain, island, p);
                var samples = Footprint(terrain, island, b).ToArray();
                rows.Add(new GroundRecord { name = r.Root.name, kind = r.Kind.ToString(), position = p,
                    min = b.min, max = b.max, ground = ground, gap = b.min.y - ground,
                    slopeSpan = samples.Max() - samples.Min() });
            }
            var props = island.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "AngeProps");
            if (props != null) foreach (Transform t in props)
            {
                if (!BoundsInIsland(t, island, out var b)) continue;
                var p = island.InverseTransformPoint(t.position);
                var ground = Ground(terrain, island, p);
                rows.Add(new GroundRecord { name = t.name, kind = "Prop", position = p, min = b.min, max = b.max, ground = ground, gap = b.min.y - ground });
            }
            var report = new GroundReport { count = rows.Count, records = rows.OrderByDescending(r => Math.Abs(r.gap)).ToArray() };
            Directory.CreateDirectory("Evidence/Revision");
            File.WriteAllText("Evidence/Revision/scene-grounding-audit.json", JsonUtility.ToJson(report, true));
            return new { count = report.count, largestGaps = report.records.Take(24).Select(r=>new {r.name,r.kind,position=r.position.ToString(),r.gap,r.slopeSpan}).ToArray() };
        }
        public static float Ground(LowPolyTerrainGenerator terrain, Transform island, Vector3 p)
        {
            var collider = terrain.GetComponentsInChildren<MeshCollider>().FirstOrDefault(c => c.name == "Triangulated Heightfield");
            if (collider != null)
            {
                var ray = new Ray(island.TransformPoint(new Vector3(p.x, 64, p.z)), -island.up);
                if (collider.Raycast(ray, out var hit, 128 * island.lossyScale.y)) return island.InverseTransformPoint(hit.point).y;
            }
            return terrain.SampleHeight(p);
        }
        public static IEnumerable<float> Footprint(LowPolyTerrainGenerator terrain, Transform island, Bounds b)
        {
            yield return Ground(terrain, island, b.center);
            for (var x = 0; x < 2; x++) for (var z = 0; z < 2; z++)
                yield return Ground(terrain, island, new Vector3(x == 0 ? b.min.x : b.max.x, 0, z == 0 ? b.min.z : b.max.z));
        }
        public static bool BoundsInIsland(Transform root, Transform island, out Bounds bounds)
        {
            bounds = default; var first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || filter.name.StartsWith("Weather Coating")) continue;
                var b = filter.sharedMesh.bounds;
                for (var x = 0; x < 2; x++) for (var y = 0; y < 2; y++) for (var z = 0; z < 2; z++)
                {
                    var p = island.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z)));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            return !first;
        }

        public static object Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Polish only in Edit Mode.");
            var world=Object.FindFirstObjectByType<WorldBuilder>();
            var terrain=Object.FindFirstObjectByType<LowPolyTerrainGenerator>();
            var mr=Object.FindFirstObjectByType<MrTabletopExperience>();var island=mr.TabletopIsland;
            if(Vector3.Distance(island.lossyScale,Vector3.one)>.001f)throw new InvalidOperationException("Edit the unscaled authored island.");
            var modelPaths=new[]{"buildings-0-6d455bfe.glb","buildings-1-e19101a9.glb","buildings-2-51c98878.glb"};
            var models=modelPaths.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WeatherManager/Art/PolishAnge/"+n)).ToArray();
            var bridgeModel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WeatherManager/Art/PolishAnge/groundworks-2-bba9d563.glb");
            if(models.Any(m=>m==null)||bridgeModel==null)throw new InvalidOperationException("All selected Ange models must finish importing first.");
            Undo.RegisterFullObjectHierarchyUndo(island.gameObject,"Refine low-poly diorama");
            Directory.CreateDirectory(Folder);
            terrain.RebuildPolishedPresentation();
            foreach(var filter in terrain.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.sharedMesh==null)continue;
                var mesh=Persist(filter.sharedMesh,filter.name.Replace(' ','_'));
                filter.sharedMesh=mesh;var collider=filter.GetComponent<MeshCollider>();if(collider!=null)collider.sharedMesh=mesh;
            }
            Physics.SyncTransforms();
            var road=terrain.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Curved Road Network");
            road.sharedMaterial=Solid("Warm Asphalt",new Color(.25f,.31f,.29f));
            var water=terrain.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Lake and River");
            water.sharedMaterial=Solid("Lagoon Teal",new Color(.22f,.53f,.57f));
            water.sharedMaterial.shader=Shader.Find("WeatherManager/Quiet Water");EditorUtility.SetDirty(water.sharedMaterial);
            var old=island.Find("Diorama Details");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var details=new GameObject("Diorama Details").transform;details.SetParent(island,false);
            var footings=new MeshBatch();var paths=new MeshBatch();var gardens=new MeshBatch();var trim=new MeshBatch();
            var buildingIndex=0;var farmIndex=0;var treeIndex=0;var vehicleIndex=0;var grounded=0;
            var parkTrees=new[]{new Vector2(-10,-20),new Vector2(-8,-25),new Vector2(0,-28),new Vector2(8,-27),new Vector2(16,-24),new Vector2(19,-18),new Vector2(23,-15),new Vector2(23,-6),new Vector2(18,0),new Vector2(20,9),new Vector2(27,15),new Vector2(32,17),new Vector2(38,5),new Vector2(40,-4),new Vector2(-39,-3),new Vector2(-33,-1),new Vector2(-27,0),new Vector2(-23,-3),new Vector2(-19,4),new Vector2(-17,8),new Vector2(-14,14),new Vector2(9,16)};
            foreach(var record in world.Damageables)
            {
                if(record.Root==null)continue;
                if(record.Kind==DamageableKind.Building)
                {
                    var pick=buildingIndex==20||buildingIndex==38?2:((buildingIndex*7+buildingIndex/6)%5<3?0:1);
                    var bp=island.InverseTransformPoint(record.Root.position);
                    if(buildingIndex<30){bp.x=-10+(buildingIndex%6)*4;bp.z=-9.5f+(buildingIndex/6)*3.8f;}
                    else if(buildingIndex<34){bp.x=-38+(buildingIndex-30)*6;bp.z=-5.3f;}
                    else if(buildingIndex==34){bp.x=-13;bp.z=14;}
                    record.Root.position=island.TransformPoint(bp);
                    record.Root.localRotation=Quaternion.Euler(0,((buildingIndex*13)%7-3)*1.3f,0);
                    ReplaceHouse(record,models[pick],modelPaths[pick],pick,island);
                    buildingIndex++;
                }
                if(record.Kind==DamageableKind.Tree&&treeIndex<parkTrees.Length)
                {
                    var p=parkTrees[treeIndex++];record.Root.position=island.TransformPoint(new Vector3(p.x,0,p.y));
                }
                if(record.Kind==DamageableKind.Vehicle)
                {
                    var route=LowPolyTerrainGenerator.TownRoadPath;var segment=vehicleIndex%10;
                    var q=Vector2.Lerp(route[segment],route[segment+1],vehicleIndex<10?.2f:.68f);
                    record.Root.position=island.TransformPoint(new Vector3(q.x,0,q.y));
                    var direction=route[segment+1]-route[segment];record.Root.localRotation=Quaternion.LookRotation(new Vector3(direction.x,0,direction.y))*Quaternion.Euler(0,-90,0);
                    record.VehicleRouteTargetIndex=segment+1==10?0:segment+1;vehicleIndex++;
                }
                if(record.Kind==DamageableKind.Farm)
                {
                    var p=island.InverseTransformPoint(record.Root.position);
                    p.x=-34f+(farmIndex%6)*3.6f;p.z=-22f+(farmIndex/6)*3.4f;
                    BuildFarmPlants(record,farmIndex%3);
                    record.Root.position=island.TransformPoint(p);farmIndex++;
                }
                if(!BoundsInIsland(record.Root,island,out var bounds))continue;
                var position=island.InverseTransformPoint(record.Root.position);
                var onFooting=record.Kind==DamageableKind.Building||record.Kind==DamageableKind.Farm;
                var target=onFooting?Footprint(terrain,island,bounds).Max()+.025f:Ground(terrain,island,position)-.015f;
                if(record.Kind==DamageableKind.Vehicle)target+=.08f;
                record.Root.position+=island.up*(target-bounds.min.y);
                BoundsInIsland(record.Root,island,out bounds);
                if(onFooting)
                {
                    var border=record.Kind==DamageableKind.Farm?.10f:.16f;
                    var pad=new Bounds(bounds.center,new Vector3(bounds.size.x+border*2,0,bounds.size.z+border*2));
                    pad.center=new Vector3(pad.center.x,target,pad.center.z);
                    AddFooting(footings,terrain,island,pad,record.Kind==DamageableKind.Farm?new Color(.39f,.29f,.18f):new Color(.63f,.62f,.51f));
                    if(record.Kind==DamageableKind.Building)
                    {
                        var center=new Vector3(pad.center.x,target+.035f,pad.min.z-.32f);
                        paths.Box(center,new Vector3(pad.size.x+.20f,.06f,.55f),new Color(.72f,.69f,.56f));
                        if(buildingIndex%3!=0)
                        {
                            var planter=new Vector3(pad.max.x-.35f,target+.13f,pad.min.z-.37f);
                            trim.Box(planter,new Vector3(.55f,.25f,.36f),new Color(.45f,.36f,.23f));
                            gardens.Bush(planter+Vector3.up*.22f,.28f,new Color(.26f,.47f,.31f));
                            gardens.Bush(planter+new Vector3(.12f,.35f,0),.105f,new Color(.80f,.49f,.36f));
                        }
                    }
                }
                Capture(record,world);grounded++;
            }
            var props=island.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="AngeProps");
            if(props!=null)
            {
                BuildWindmill(props,Object.FindFirstObjectByType<DisasterSimulation>());
                BuildWaterwheel(props,terrain,island,trim,Object.FindFirstObjectByType<DisasterSimulation>());
                foreach(var fence in props.Cast<Transform>().Where(t=>t.name.StartsWith("Fence ")).ToArray())Object.DestroyImmediate(fence.gameObject);
                foreach(Transform prop in props)
                {
                    if(prop.name.StartsWith("Bridge")||prop.name=="WaterWheel 01")continue;
                    var p=island.InverseTransformPoint(prop.position);
                    if(prop.name=="Hay Bale 01"){p.x=-33;p.z=-23.6f;}
                    if(prop.name=="Hay Bale 02"){p.x=-31.6f;p.z=-23.6f;}
                    if(prop.name=="Windmill 01"){p.x=-39;p.z=-13f;}
                    if(prop.name.StartsWith("Split Rail Fence")){p.x=-33;p.z=-24;}
                    if(prop.name=="Storage Shed 01"){p.x=-23;p.z=-24.5f;}
                    if(prop.name=="Gazebo 01"){p.x=2;p.z=-22;}
                    if(prop.name=="Bench 01"){p.x=-3;p.z=-18;}
                    if(prop.name=="Bench 02"){p.x=8;p.z=-18;}
                    if(prop.name=="Bench 03"){p.x=-3;p.z=-25;}
                    if(prop.name=="Bench 04"){p.x=8;p.z=-25;}
                    if(prop.name=="Stacked Cliff 02"){p.x=-37;p.z=25;}
                    if(prop.name=="Grey Boulder 02"){p.x=-30;p.z=24;}
                    prop.position=island.TransformPoint(p);
                    if(!BoundsInIsland(prop.name=="Windmill 01"?prop.Find("Windmill Tower"):prop,island,out var b))continue;
                    var target=Ground(terrain,island,p)-.035f;
                    if(prop.name.IndexOf("Boulder",StringComparison.OrdinalIgnoreCase)>=0||prop.name.IndexOf("Rock",StringComparison.OrdinalIgnoreCase)>=0)target-=.18f;
                    prop.position+=island.up*(target-b.min.y);
                    if(b.size.x>1.8f&&b.size.z>1.2f&&!prop.name.Contains("Tree")&&!prop.name.Contains("Rock")&&!prop.name.Contains("Bush")&&!prop.name.Contains("Boulder")&&!prop.name.Contains("Cliff"))
                    {
                        BoundsInIsland(prop,island,out b);var top=Footprint(terrain,island,b).Max()+.025f;
                        prop.position+=island.up*(top-b.min.y);b.center=new Vector3(b.center.x,top,b.center.z);b.size=new Vector3(b.size.x+.10f,0,b.size.z+.10f);
                        AddFooting(footings,terrain,island,b,new Color(.56f,.56f,.45f));
                    }
                }
                var bridge=props.Find("Bridge 01");if(bridge!=null)Object.DestroyImmediate(bridge.gameObject);
                bridge=new GameObject("Bridge 01").transform;bridge.SetParent(props,false);bridge.localPosition=new Vector3(31,0,-10.05f);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(bridgeModel);model.transform.SetParent(bridge,false);
                model.transform.localScale=Vector3.one*6.4f;
                var bank=Mathf.Max(Ground(terrain,island,new Vector3(28.3f,0,-10.05f)),Ground(terrain,island,new Vector3(33.7f,0,-10.05f)));
                bridge.localPosition=new Vector3(31,bank-.75f,-10.05f);
                for(var end=-1;end<=1;end+=2)
                {
                    var x=31+end*2.8f;var bottom=Ground(terrain,island,new Vector3(x,0,-10.05f))-.15f;
                    var height=Mathf.Max(.25f,bank-bottom);
                    trim.Box(new Vector3(x,bottom+height*.5f,-10.05f),new Vector3(.65f,height,2.1f),new Color(.52f,.53f,.47f));
                }
            }
            foreach(var rock in island.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Coast Rock")))
            {
                var p=island.InverseTransformPoint(rock.transform.position);var floor=Ground(terrain,island,p)-.12f;
                if(BoundsInIsland(rock.transform,island,out var b))rock.transform.position+=island.up*(floor-b.min.y);
            }
            // Short pedestrian lanes tie the previously isolated house plots together.
            foreach(var z in new[]{-11.7f,-7.6f,-3.8f,0f,3.8f,7.8f})
                Ribbon(paths,terrain,new Vector2(-12.2f,z),new Vector2(12.2f,z),.22f,new Color(.68f,.66f,.52f));
            foreach(var x in new[]{-8f,-4f,0f,4f,8f})
                Ribbon(paths,terrain,new Vector2(x,-11.7f),new Vector2(x,7.8f),.20f,new Color(.68f,.66f,.52f));
            var walkColor=new Color(.64f,.59f,.44f);
            for(var i=0;i<40;i++)
            {
                Vector2 ParkPoint(int n){var a=n*Mathf.PI*2/40;return new Vector2(3+12*Mathf.Cos(a),-22+5.9f*Mathf.Sin(a));}
                Ribbon(paths,terrain,ParkPoint(i),ParkPoint(i+1),.44f,walkColor);
            }
            Ribbon(paths,terrain,new Vector2(2,-12.5f),new Vector2(2,-22),.55f,walkColor);
            Ribbon(paths,terrain,new Vector2(-9,-22),new Vector2(-16,-22),.4f,walkColor);
            for(var i=0;i<4;i++)Ribbon(paths,terrain,new Vector2(-38+i*6,-6.8f),new Vector2(-38+i*6,-9),.4f,walkColor);
            foreach(var p in new[]{new Vector2(-5,-21),new Vector2(11,-22),new Vector2(2,-26),new Vector2(16,-14),new Vector2(18,-3),new Vector2(23,11)})
            {
                var h=terrain.SampleRenderedHeight(new Vector3(p.x,0,p.y));
                for(var k=0;k<5;k++){var q=new Vector3(p.x+Mathf.Cos(k*2.4f)*.7f,h+.25f,p.y+Mathf.Sin(k*2.4f)*.6f);gardens.Bush(q,.55f,new Color(.26f,.43f,.27f));gardens.Bush(q+Vector3.up*.39f,.12f,k%2==0?new Color(.82f,.58f,.33f):new Color(.69f,.37f,.32f));}
            }
            for(var i=0;i<18;i++)
            {
                var p=new Vector3(-37.2f+i*1.22f,0,-24.2f);p.y=terrain.SampleRenderedHeight(p);
                trim.Box(p+Vector3.up*.34f,new Vector3(.13f,.68f,.13f),new Color(.50f,.37f,.22f));
                if(i<17)trim.Box(p+new Vector3(.61f,.48f,0),new Vector3(1.22f,.10f,.10f),new Color(.65f,.49f,.30f));
            }
            footings.Save(details,"Terrain Foundations");paths.Save(details,"Village Paving");gardens.Save(details,"Pocket Gardens");trim.Save(details,"Diorama Joinery");
            foreach(var resident in world.GetComponentsInChildren<ResidentAgent>(true)){resident.SetTerrain(terrain);resident.SnapToTerrain();EditorUtility.SetDirty(resident);}
            WeatherEnvironmentAssetBuilder.Build(world,Object.FindFirstObjectByType<DisasterSimulation>());
            WeatherManagerRevisionTools.ClearIslandStaticFlags(island);
            EditorUtility.SetDirty(world);EditorUtility.SetDirty(terrain);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            Audit();
            return new {grounded,buildings=buildingIndex,farms=farmIndex,newAngeModels=4,detailBatches=4};
        }

        private static void Capture(DamageableState record,WorldBuilder world)
        {
            record.InitialPosition=world.GetGameplayPosition(record.Root);record.InitialRotation=world.GetGameplayRotation(record.Root);
            record.InitialLocalPosition=record.Root.localPosition;record.InitialLocalRotation=record.Root.localRotation;
            record.InitialScale=record.Root.localScale;record.HasLocalTransformBaseline=true;
            var binding=record.Root.GetComponent<AngeVisualBinding>();binding?.RefreshRenderers();
            record.Renderers=record.Root.GetComponentsInChildren<Renderer>(true).Where(r=>!r.name.StartsWith("Weather Coating")).ToArray();
            record.BaseColors=record.Renderers.Select(r=>r.sharedMaterial!=null&&r.sharedMaterial.HasProperty("_BaseColor")?r.sharedMaterial.GetColor("_BaseColor"):Color.white).ToArray();
        }
        private static void BuildWindmill(Transform props,DisasterSimulation simulation)
        {
            var old=props.Find("Windmill 01");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("Windmill 01").transform;root.SetParent(props,false);
            var tower=new MeshBatch();
            tower.Frustum(Vector3.zero,.85f,.54f,3.85f,10,new Color(.81f,.72f,.52f));
            tower.Frustum(new Vector3(0,3.85f,0),.76f,0,1.2f,10,new Color(.49f,.28f,.20f));
            tower.Box(new Vector3(0,.7f,-.78f),new Vector3(.55f,1.4f,.09f),new Color(.34f,.26f,.19f));
            tower.Box(new Vector3(.60f,2.1f,-.27f),new Vector3(.08f,.46f,.34f),new Color(.20f,.38f,.39f));
            tower.Box(new Vector3(-.60f,2.1f,-.27f),new Vector3(.08f,.46f,.34f),new Color(.20f,.38f,.39f));
            tower.Save(root,"Windmill Tower");
            var pivot=new GameObject("Sail axle").transform;pivot.SetParent(root,false);pivot.localPosition=new Vector3(0,3.25f,-.76f);
            var sails=new MeshBatch();sails.Box(Vector3.zero,new Vector3(.32f,.32f,.30f),new Color(.38f,.28f,.19f));
            for(var i=0;i<4;i++)
            {
                var a=i*Mathf.PI*.5f+.25f;var along=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);var across=new Vector3(Mathf.Cos(a),-Mathf.Sin(a),0);
                void Strip(float start,float end,float width,Color color,float depth)
                {
                    var p=along*start+Vector3.back*depth;var q=along*end+Vector3.back*depth;
                    sails.Quad(p-across*width,q-across*width,q+across*width,p+across*width,color);
                    sails.Quad(p+across*width,q+across*width,q-across*width,p-across*width,color*.9f);
                }
                Strip(.12f,2.25f,.055f,new Color(.39f,.29f,.19f),.08f);
                Strip(.72f,2.16f,.27f,new Color(.91f,.84f,.64f),.09f);
                for(var j=0;j<5;j++)Strip(.75f+j*.32f,.79f+j*.32f,.29f,new Color(.52f,.40f,.25f),.10f);
            }
            sails.Save(pivot,"Windmill Sails");
            root.gameObject.AddComponent<AngeEnvironmentalAnimator>().Configure(AngeEnvironmentalMotion.WindRotor,simulation,pivot,Vector3.forward,7,140);
        }
        private static void BuildFarmPlants(DamageableState record,int variety)
        {
            var pivot=record.Root.Find("Weather Motion Pivot")??record.Root;
            foreach(var old in pivot.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Crop Row")||t.name.StartsWith("Crop Plants")).ToArray())if(old!=null)Object.DestroyImmediate(old.gameObject);
            var plants=new MeshBatch();
            for(var row=0;row<5;row++)for(var i=0;i<5;i++)
            {
                var p=new Vector3((row-2)*.42f,.075f,(i-2)*.27f);
                if(variety==0)
                {
                    plants.Frustum(p,.022f,.014f,.31f,4,new Color(.47f,.47f,.23f));
                    plants.Frustum(p+Vector3.up*.29f,.062f,.018f,.18f,5,new Color(.80f,.63f,.30f));
                }
                else
                {
                    plants.Frustum(p,.025f,.018f,.16f,4,new Color(.29f,.40f,.20f));
                    plants.Bush(p+Vector3.up*.20f,.155f,variety==1?new Color(.37f,.55f,.26f):new Color(.25f,.44f,.28f));
                    if(variety==2&&i%2==0)plants.Bush(p+new Vector3(.07f,.28f,0),.065f,new Color(.75f,.36f,.22f));
                }
            }
            plants.Save(pivot,"Crop Plants "+variety);
        }
        private static void BuildWaterwheel(Transform props,LowPolyTerrainGenerator terrain,Transform island,MeshBatch trim,DisasterSimulation simulation)
        {
            var old=props.Find("WaterWheel 01");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("WaterWheel 01").transform;root.SetParent(props,false);root.localPosition=new Vector3(34,terrain.WaterLevel+1.1f,-15.5f);
            var pivot=new GameObject("Wheel axle").transform;pivot.SetParent(root,false);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WeatherManager/Art/Ange/Landmarks/WaterWheel.glb");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab);model.transform.SetParent(pivot,false);model.transform.localScale=Vector3.one*2.8f;
            root.gameObject.AddComponent<AngeEnvironmentalAnimator>().Configure(AngeEnvironmentalMotion.WaterWheel,simulation,pivot,Vector3.right,5,95);
            var center=root.localPosition;
            trim.Box(center,new Vector3(3.4f,.18f,.18f),new Color(.40f,.29f,.18f));
            foreach(var x in new[]{32.5f,35.5f})
            {
                var bottom=Ground(terrain,island,new Vector3(x,0,-15.5f))-.12f;
                var height=Mathf.Max(.2f,center.y+.2f-bottom);
                trim.Box(new Vector3(x,bottom+height*.5f,-15.5f),new Vector3(.24f,height,.28f),new Color(.40f,.29f,.18f));
            }
        }
        private static void ReplaceHouse(DamageableState record,GameObject prefab,string name,int index,Transform island)
        {
            var binding=record.Root.GetComponent<AngeVisualBinding>();if(binding==null)return;
            var parent=record.Root.Find("Weather Motion Pivot")??record.Root;
            var old=binding.VisualRoot;var destroyed=binding.DestroyedVisual;var critical=binding.CriticalVisual;var legacy=binding.LegacyVisual;
            if(old!=null)Object.DestroyImmediate(old.gameObject);if(critical!=null)critical.gameObject.SetActive(false);
            var visual=new GameObject("Visual").transform;visual.SetParent(parent,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab);model.transform.SetParent(visual,false);
            if(!BoundsInIsland(visual,visual,out var local))throw new InvalidOperationException("New house is missing readable bounds.");
            var factor=Mathf.Min(3.15f/local.size.x,2.95f/local.size.z,index==2?4.4f/local.size.y:3.5f/local.size.y);
            model.transform.localScale=Vector3.one*factor;
            model.transform.localPosition=new Vector3(-local.center.x,-local.min.y,-local.center.z)*factor;
            var ids=new[]{"6d455bfe-cc9f-4bbd-8942-0a5a7e7147cb","e19101a9-3897-4a51-afd7-e46a88ea1bb8","51c98878-ce4d-4375-b294-38a9be128df2"};
            binding.Configure(ids[index],name,record.Kind,legacy,visual,null,destroyed,local.size*factor,prefab,visual.GetComponentsInChildren<Renderer>());
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>()){renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;renderer.receiveShadows=true;}
            EditorUtility.SetDirty(binding);
        }
        private static Material Solid(string name,Color color)
        {
            var path=Folder+"/"+name.Replace(' ','_')+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.16f);EditorUtility.SetDirty(m);return m;
        }
        private static Mesh Persist(Mesh mesh,string name)
        {
            if(AssetDatabase.Contains(mesh))return mesh;
            var path=Folder+"/"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            saved.Clear();saved.indexFormat=mesh.indexFormat;saved.vertices=mesh.vertices;saved.normals=mesh.normals;
            saved.colors=mesh.colors;saved.uv=mesh.uv;saved.subMeshCount=mesh.subMeshCount;
            for(var i=0;i<mesh.subMeshCount;i++)saved.SetIndices(mesh.GetIndices(i),mesh.GetTopology(i),i);
            saved.bounds=mesh.bounds;saved.UploadMeshData(false);
            Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;
        }
        private static void AddFooting(MeshBatch batch,LowPolyTerrainGenerator terrain,Transform island,Bounds pad,Color color)
        {
            var perimeter=new List<Vector3>();
            var a=new Vector3(pad.min.x,pad.center.y,pad.min.z);var b=new Vector3(pad.max.x,pad.center.y,pad.min.z);
            var c=new Vector3(pad.max.x,pad.center.y,pad.max.z);var d=new Vector3(pad.min.x,pad.center.y,pad.max.z);
            var corners=new[]{a,b,c,d,a};
            for(var edge=0;edge<4;edge++)
            {
                var steps=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(corners[edge],corners[edge+1])/.45f));
                for(var i=0;i<steps;i++)perimeter.Add(Vector3.Lerp(corners[edge],corners[edge+1],i/(float)steps));
            }
            for(var i=0;i<perimeter.Count;i++)
            {
                var p=perimeter[i];var q=perimeter[(i+1)%perimeter.Count];
                var loP=new Vector3(p.x,Ground(terrain,island,p)-.055f,p.z);var loQ=new Vector3(q.x,Ground(terrain,island,q)-.055f,q.z);
                batch.Quad(p,q,loQ,loP,color*.88f);batch.Triangle(pad.center,q,p,color);
            }
        }
        private static void Ribbon(MeshBatch mesh,LowPolyTerrainGenerator terrain,Vector2 a,Vector2 b,float half,Color color)
        {
            var n=new Vector2(-(b-a).normalized.y,(b-a).normalized.x)*half;var steps=Mathf.CeilToInt(Vector2.Distance(a,b)/.5f);
            Vector3 Point(Vector2 p)=>new Vector3(p.x,terrain.SampleRenderedHeight(new Vector3(p.x,0,p.y))+.055f,p.y);
            for(var i=0;i<steps;i++){var p=Vector2.Lerp(a,b,i/(float)steps);var q=Vector2.Lerp(a,b,(i+1)/(float)steps);mesh.Quad(Point(p-n),Point(p+n),Point(q+n),Point(q-n),color);}
        }
        private sealed class MeshBatch
        {
            private readonly List<Vector3> vertices=new List<Vector3>();private readonly List<Color> colors=new List<Color>();private readonly List<int> indices=new List<int>();
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color){var i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);colors.Add(color);colors.Add(color);colors.Add(color);indices.Add(i);indices.Add(i+1);indices.Add(i+2);}
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color){Triangle(a,b,c,color);Triangle(a,c,d,color);}
            public void Box(Vector3 p,Vector3 size,Color color)
            {
                var e=size*.5f;var a=p+new Vector3(-e.x,-e.y,-e.z);var b=p+new Vector3(e.x,-e.y,-e.z);var c=p+new Vector3(e.x,-e.y,e.z);var d=p+new Vector3(-e.x,-e.y,e.z);
                var up=Vector3.up*size.y;Quad(a+up,b+up,b,a,color*.88f);Quad(b+up,c+up,c,b,color*.94f);Quad(c+up,d+up,d,c,color);Quad(d+up,a+up,a,d,color*.91f);Quad(a+up,d+up,c+up,b+up,color*1.06f);
            }
            public void Bush(Vector3 center,float radius,Color color)
            {
                var top=center+Vector3.up*radius;var bottom=center-Vector3.up*radius*.55f;
                for(var i=0;i<7;i++){var a=i*Mathf.PI*2/7;var b=(i+1)*Mathf.PI*2/7;var p=center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;var q=center+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*radius;Triangle(top,q,p,color*Mathf.Lerp(.92f,1.04f,i/6f));Triangle(bottom,p,q,color*.84f);}
            }
            public void Frustum(Vector3 center,float bottom,float top,float height,int sides,Color color)
            {
                for(var i=0;i<sides;i++)
                {
                    var a=i*Mathf.PI*2/sides;var b=(i+1)*Mathf.PI*2/sides;
                    var u=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var v=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
                    Quad(center+u*bottom,center+u*top+Vector3.up*height,center+v*top+Vector3.up*height,center+v*bottom,color*Mathf.Lerp(.94f,1.02f,i/(float)sides));
                    if(top>0)Triangle(center+Vector3.up*height,center+v*top+Vector3.up*height,center+u*top+Vector3.up*height,color);
                }
            }
            public void Save(Transform parent,string name)
            {
                if(vertices.Count==0)return;var mesh=new Mesh {name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=Persist(mesh,name.Replace(' ','_'));
                var materialPath=Folder+"/Diorama_Vertex_Color.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(m==null){m=new Material(Shader.Find("WeatherManager/LowPolyTerrain"));AssetDatabase.CreateAsset(m,materialPath);}
                go.GetComponent<MeshRenderer>().sharedMaterial=m;
            }
        }
    }
}
