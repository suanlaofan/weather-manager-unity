using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherManager.Editor
{
    public static class WeatherEnvironmentAssetBuilder
    {
        public static int Build(WorldBuilder world,DisasterSimulation simulation)
        {
            const string path="Assets/WeatherManager/Resources/WeatherManager/Presentation/WeatherCoating.mat";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(Shader.Find("WeatherManager/Weather Coating"));AssetDatabase.CreateAsset(material,path); }
            var count=0;
            foreach(var record in world.Damageables)
            {
                if(record.Root==null)continue;
                var root=record.Root;
                var pivot=root.Find("Weather Motion Pivot");
                if(pivot==null)
                {
                    var children=new List<Transform>();foreach(Transform child in root)children.Add(child);
                    pivot=new GameObject("Weather Motion Pivot").transform;pivot.SetParent(root,false);
                    foreach(var child in children)child.SetParent(pivot,true);
                }
                var coatings=new List<MeshRenderer>();
                var sources=pivot.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null&&!f.name.StartsWith("Weather Coating"))
                    .OrderByDescending(f=>f.sharedMesh.bounds.size.sqrMagnitude*f.transform.lossyScale.sqrMagnitude).Take(4).ToArray();
                foreach(var source in sources)
                {
                    var overlay=source.transform.Find("Weather Coating");
                    if(overlay==null) { overlay=new GameObject("Weather Coating",typeof(MeshFilter),typeof(MeshRenderer)).transform;overlay.SetParent(source.transform,false); }
                    overlay.GetComponent<MeshFilter>().sharedMesh=source.sharedMesh;
                    var renderer=overlay.GetComponent<MeshRenderer>();
                    renderer.sharedMaterials=Enumerable.Repeat(material,source.sharedMesh.subMeshCount).ToArray();
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.enabled=false;coatings.Add(renderer);
                }
                var presentation=root.GetComponent<WeatherObjectPresentation>();
                if(presentation==null)presentation=root.gameObject.AddComponent<WeatherObjectPresentation>();
                presentation.Configure(pivot,coatings.ToArray(),record.Kind,world,simulation);
                EditorUtility.SetDirty(presentation);count++;
            }
            AssetDatabase.SaveAssets();return count;
        }
    }
}
