using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherManager.Editor
{
    /// <summary>Authoring fallback after Ange material and sprite searches found no compatible alpha sheets.</summary>
    public static class WeatherPresentationAssetBuilder
    {
        private const string Folder = "Assets/WeatherManager/Resources/WeatherManager/Presentation";
        [MenuItem("Weather Manager/Revision/Bake Weather Presentation Assets")]
        public static WeatherPresentationSettings Build()
        {
            Directory.CreateDirectory(Folder);
            var settings = AssetDatabase.LoadAssetAtPath<WeatherPresentationSettings>(Folder + "/WeatherPresentation.asset");
            if (settings == null) { settings = ScriptableObject.CreateInstance<WeatherPresentationSettings>(); AssetDatabase.CreateAsset(settings, Folder + "/WeatherPresentation.asset"); }
            var names = new[] { "Wind", "Rain", "Electric", "Snow", "Heat" };
            var capacities = new[] { 100, 180, 24, 260, 50 };
            var rates = new[] { 62f, 210f, 18f, 76f, 24f };
            var sizes = new[] { new Vector2(.95f,1.65f), new Vector2(.85f,1.25f), new Vector2(.09f,.17f), new Vector2(.24f,.42f), new Vector2(1f,1.6f) };
            var lives = new[] { new Vector2(.6f,1.1f), new Vector2(.38f,.62f), new Vector2(.22f,.4f), new Vector2(2.4f,3.3f), new Vector2(1f,1.6f) };
            settings.weather = new WeatherPresentationSettings.WeatherLayer[5];
            for (var i=0;i<5;i++) settings.weather[i] = new WeatherPresentationSettings.WeatherLayer {
                weather=(WeatherType)i, material=Material(names[i], i, false), capacity=capacities[i],
                particlesPerSecond=rates[i],size=sizes[i],lifetime=lives[i],
                color=i==1 ? new Color(.65f,.86f,1f,.9f) : i==4 ? new Color(1f,.67f,.32f,.32f) : WeatherPalette.For((WeatherType)i)
            };
            var surfacePath=Folder+"/Surface.mat";
            settings.surface=AssetDatabase.LoadAssetAtPath<Material>(surfacePath);
            if(settings.surface==null) { settings.surface=new Material(Shader.Find("WeatherManager/Weather Particle")); AssetDatabase.CreateAsset(settings.surface,surfacePath); }
            settings.flame=Material("Flame",5,true); settings.smoke=Material("Smoke",6,true);
            settings.spark=Material("Spark",2,false); settings.splash=Material("Splash",7,false);
            settings.ripple=Material("Ripple",8,false); settings.cloud=Material("Cloud",6,false);
            settings.steam=Material("Steam",6,true);
            settings.funnel=FunnelMaterial("Funnel",new Color(.51f,.64f,.65f,.8f));
            settings.fireFunnel=FunnelMaterial("FireFunnel",new Color(1f,.36f,.065f,.85f));
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            return settings;
        }
        private static Material FunnelMaterial(string name,Color color)
        {
            var path=Folder+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null) { mat=new Material(Shader.Find("WeatherManager/Continuous Funnel")); AssetDatabase.CreateAsset(mat,path); }
            mat.SetColor("_BaseColor",color); mat.enableInstancing=true; EditorUtility.SetDirty(mat); return mat;
        }
        private static Material Material(string name,int kind,bool animated)
        {
            var side=animated?256:128; var texture=new Texture2D(side,side,TextureFormat.RGBA32,false);
            var pixels=new Color[side*side];
            for(var y=0;y<side;y++) for(var x=0;x<side;x++)
            {
                var tiles=animated?4:1; var tileSide=side/tiles;
                var frame=animated ? (y/tileSide)*4+x/tileSide : 0;
                var u=((x%tileSide)+.5f)/tileSide; var v=((y%tileSide)+.5f)/tileSide;
                pixels[y*side+x]=Pixel(kind,u,v,frame/16f);
            }
            texture.SetPixels(pixels); texture.Apply();
            var png=Folder+"/"+name+(animated?"-4x4":"")+".png";
            File.WriteAllBytes(png,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(png,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(png);
            importer.alphaSource=TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false; importer.wrapMode=TextureWrapMode.Clamp; importer.filterMode=FilterMode.Bilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=256;
            importer.SaveAndReimport();
            var path=Folder+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null) { mat=new Material(Shader.Find("WeatherManager/Weather Particle")); AssetDatabase.CreateAsset(mat,path); }
            mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(png); mat.SetColor("_BaseColor",Color.white);
            EditorUtility.SetDirty(mat); return mat;
        }
        private static Color Pixel(int kind,float u,float v,float phase)
        {
            var p=new Vector2(u*2-1,v*2-1); var radial=p.magnitude;
            var a=0f; var color=Color.white;
            switch(kind)
            {
                case 0: // Curved stream ribbon with tapered ends.
                    var curve=.24f*Mathf.Sin(p.x*3.2f);
                    a=(1-Smooth(.035f,.11f,Mathf.Abs(p.y-curve)))*(1-Smooth(.60f,.96f,Mathf.Abs(p.x))); break;
                case 1:
                    a=(1-Smooth(.065f,.16f,Mathf.Abs(p.x)))*(1-Smooth(.3f,.96f,Mathf.Abs(p.y))); break;
                case 2:
                    a=Mathf.Pow(Mathf.Clamp01(1-radial),2f); break;
                case 3:
                    for(var i=0;i<3;i++) { var angle=i*Mathf.PI/3; var d=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                        var distance=Mathf.Abs(p.x*d.y-p.y*d.x); a=Mathf.Max(a,(1-Smooth(.035f,.09f,distance))*(1-Smooth(.56f,.87f,radial))); }
                    a=Mathf.Max(a,Mathf.Pow(Mathf.Clamp01(1-radial*2),2)); break;
                case 4:
                    a=(1-Smooth(.02f,.08f,Mathf.Abs(p.x-.22f*Mathf.Sin(v*7))))*(1-Smooth(.15f,.93f,Mathf.Abs(p.y)))*.7f; break;
                case 5:
                    var time=phase*Mathf.PI*2;
                    var center=.12f*Mathf.Sin(v*8-time)+.08f*Mathf.Sin(v*14+time);
                    var width=Mathf.Lerp(.63f,.015f,Mathf.Pow(v,.66f));
                    var turbulence=.075f*Mathf.Sin(p.x*12+v*19-time*2)+.055f*Mathf.Sin(p.x*19-v*13+time);
                    a=1-Smooth(width-.11f,width+.08f,Mathf.Abs(p.x-center)+turbulence);
                    a*=Smooth(0,.10f,v)*(1-Smooth(.83f,.99f,v));
                    var core=(1-Smooth(.08f,.28f,Mathf.Abs(p.x-center)))*(1-Smooth(.17f,.6f,v));
                    color=Color.Lerp(new Color(1,.22f,.025f),new Color(1,.83f,.22f),core);
                    a*=.9f; break;
                case 6:
                    var n=Mathf.PerlinNoise(u*4.6f+Mathf.Cos(phase*Mathf.PI*2)*.28f,v*4.6f+Mathf.Sin(phase*Mathf.PI*2)*.28f);
                    a=(1-Smooth(.25f,.97f,radial))*(.38f+.62f*n);
                    color=Color.Lerp(new Color(.76f,.78f,.8f),Color.white,n); break;
                case 7:
                    a=(1-Smooth(.04f,.14f,Mathf.Abs(radial-.54f)))*(1-Smooth(.9f,1,radial));
                    a*=.6f+.4f*Mathf.Sin(Mathf.Atan2(p.y,p.x)*6); break;
                case 8:
                    a=1-Smooth(.026f,.075f,Mathf.Abs(radial-.73f)); break;
            }
            a*=Smooth(0,.035f,u)*Smooth(0,.035f,v)*(1-Smooth(.965f,1,u))*(1-Smooth(.965f,1,v));
            color.a=Mathf.Clamp01(a); return color;
        }
        private static float Smooth(float a,float b,float value) { var t=Mathf.Clamp01((value-a)/(b-a)); return t*t*(3-2*t); }
    }
}
