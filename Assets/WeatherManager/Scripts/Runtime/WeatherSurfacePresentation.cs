using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherManager
{
    /// <summary>Shared terrain corners keep flood and snow surfaces continuous across simulation cells.</summary>
    public sealed class WeatherSurfacePresentation : MonoBehaviour
    {
        private const int Columns=88,Rows=60;
        [SerializeField] private MeshFilter[] surfaces=new MeshFilter[3];
        private readonly float[] water=new float[Columns*Rows],snow=new float[Columns*Rows],wet=new float[Columns*Rows];
        private readonly List<Vector3> vertices=new List<Vector3>(Columns*Rows*4);
        private readonly List<Color> colors=new List<Color>(Columns*Rows*4);
        private readonly List<int> indices=new List<int>(Columns*Rows*6);
        private readonly Mesh[] meshes=new Mesh[3];
        private LowPolyTerrainGenerator terrain;
        private bool ready;
        public void Configure(Material material)
        {
            terrain=FindFirstObjectByType<LowPolyTerrainGenerator>();
            for(var i=0;i<3;i++)
            {
                if(surfaces[i]==null) { var go=new GameObject(i==0?"Continuous flood surface":i==1?"Accumulated snow":"Receding wet ground",typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(transform,false); surfaces[i]=go.GetComponent<MeshFilter>(); }
                if(meshes[i]==null) { meshes[i]=new Mesh {name="Dynamic weather surface "+i};meshes[i].MarkDynamic(); }
                surfaces[i].sharedMesh=meshes[i];var r=surfaces[i].GetComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            }
            ready=true;ResetSurfaces();
        }
        public void Sync(IReadOnlyList<SurfaceVisualCell> cells,float delta)
        {
            if(!ready)return;
            System.Array.Clear(water,0,water.Length);System.Array.Clear(snow,0,snow.Length);
            for(var cellIndex=0;cellIndex<cells.Count;cellIndex++)
            {
                var cell=cells[cellIndex];
                var x=Mathf.Clamp(Mathf.FloorToInt((cell.Position.x+52)*Columns/104f),0,Columns-1);
                var z=Mathf.Clamp(Mathf.FloorToInt((cell.Position.z+36)*Rows/72f),0,Rows-1);
                water[z*Columns+x]=cell.Water;snow[z*Columns+x]=cell.Snow;
            }
            for(var i=0;i<wet.Length;i++)wet[i]=water[i]>.035f?Mathf.Max(wet[i],water[i]):Mathf.MoveTowards(wet[i],0,delta*.09f);
            Build(0,water,new Color(.18f,.58f,.79f,.68f));
            Build(1,snow,new Color(.9f,.96f,1,.95f));
            Build(2,wet,new Color(.09f,.22f,.27f,.24f));
        }
        private void Build(int layer,float[] values,Color tint)
        {
            vertices.Clear();colors.Clear();indices.Clear();
            for(var z=0;z<Rows;z++)for(var x=0;x<Columns;x++)
            {
                if(values[z*Columns+x]<=.035f)continue;
                var k=vertices.Count;
                Vertex(x,z,layer,values,tint);Vertex(x+1,z,layer,values,tint);Vertex(x+1,z+1,layer,values,tint);Vertex(x,z+1,layer,values,tint);
                indices.Add(k);indices.Add(k+2);indices.Add(k+1);indices.Add(k);indices.Add(k+3);indices.Add(k+2);
            }
            var mesh=meshes[layer];mesh.Clear(false);mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0,true);
            mesh.bounds=new Bounds(Vector3.zero,new Vector3(108,30,76));
        }
        private void Vertex(int x,int z,int layer,float[] values,Color tint)
        {
            var amount=(At(values,x-1,z-1)+At(values,x,z-1)+At(values,x-1,z)+At(values,x,z))*.25f;
            var point=new Vector3(-52+x*104f/Columns,0,-36+z*72f/Rows);
            point.y=(terrain!=null?terrain.SampleHeight(point):0)+(layer==2?.05f:layer==0?.12f+amount*.16f:.10f+amount*.14f);
            tint.a*=Mathf.Clamp01(amount*5f);
            vertices.Add(point);colors.Add(tint);
        }
        private static float At(float[] values,int x,int z) => x<0||z<0||x>=Columns||z>=Rows?0:values[z*Columns+x];
        public void ResetSurfaces()
        {
            System.Array.Clear(water,0,water.Length);System.Array.Clear(snow,0,snow.Length);System.Array.Clear(wet,0,wet.Length);
            foreach(var mesh in meshes)if(mesh!=null)mesh.Clear(false);
        }
        private void OnDestroy()
        {
            foreach(var mesh in meshes)if(mesh!=null)
            {
#if UNITY_EDITOR
                if(UnityEditor.EditorUtility.IsPersistent(mesh))continue;
#endif
                if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);
            }
        }
    }
}
