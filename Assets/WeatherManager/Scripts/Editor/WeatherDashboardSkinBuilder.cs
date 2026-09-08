using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherManager.Editor
{
    public static class WeatherDashboardSkinBuilder
    {
        public static void Build()
        {
            const string path="Assets/WeatherManager/Resources/WeatherManager/UI/NanoBananaIcons.png";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new FileNotFoundException("Nano Banana icon atlas must be imported first.");
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=false;importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
            importer.GetSourceTextureWidthAndHeight(out var width,out var height);
            var sprites=new SpriteMetaData[16];var cell=width/4f;
            for(var i=0;i<16;i++) sprites[i]=new SpriteMetaData {name="WeatherIcon_"+i.ToString("D2"),pivot=new Vector2(.5f,.5f),alignment=9,
                rect=new Rect((i%4)*cell+16,height-(i/4+1)*cell+16,cell-32,cell-32)};
            #pragma warning disable CS0618
            importer.spritesheet=sprites;
            #pragma warning restore CS0618
            importer.SaveAndReimport();
            const string materialPath="Assets/WeatherManager/Resources/WeatherManager/UI/SpatialUiForeground.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null){material=new Material(Shader.Find("WeatherManager/Spatial UI Foreground"));AssetDatabase.CreateAsset(material,materialPath);}
            const string iconPath="Assets/WeatherManager/Resources/WeatherManager/UI/SpatialUiIcons.mat";
            var iconMaterial=AssetDatabase.LoadAssetAtPath<Material>(iconPath);
            if(iconMaterial==null){iconMaterial=new Material(material);AssetDatabase.CreateAsset(iconMaterial,iconPath);}
            iconMaterial.SetFloat("_PaperKey",1);EditorUtility.SetDirty(iconMaterial);
            AssetDatabase.SaveAssets();
        }
    }
}
