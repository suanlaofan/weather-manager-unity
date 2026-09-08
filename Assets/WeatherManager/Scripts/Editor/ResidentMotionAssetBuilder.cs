using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WeatherManager.Editor
{
    public static class ResidentMotionAssetBuilder
    {
        private const string Folder="Assets/WeatherManager/Resources/WeatherManager/Motions";
        [Serializable] private sealed class Mapping { public Track[] tracks; }
        [Serializable] private sealed class Track { public string path; public float[] times; public float[] angles; }
        public static RuntimeAnimatorController Build(ResidentAgent reference)
        {
            Directory.CreateDirectory(Folder);
            var path=Folder+"/ResidentMotions.controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine=controller.layers[0].stateMachine;
            foreach(var s in machine.states)machine.RemoveState(s.state);
            var body=reference.transform.Find("Body").localPosition.y;
            var head=reference.transform.Find("Head").localPosition.y;
            var durations=new[]{2f,1f,2f,.8f,1.35f,.68f,1.15f};
            for(var state=0;state<7;state++)
            {
                var motion=(ResidentMotion)state;var clipPath=Folder+"/"+motion+".anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if(clip==null) { clip=new AnimationClip {name=motion.ToString(),frameRate=30}; AssetDatabase.CreateAsset(clip,clipPath); }
                clip.ClearCurves();
                var d=durations[state];var loop=state==0||state==1||state==2||state==4;
                var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
                var parts=new[]{"Left Arm","Right Arm","Left Leg","Right Leg","Body","Head"};
                foreach(var part in parts) foreach(var axis in new[]{"x","y","z"}) Curve(clip,part,"localEulerAnglesRaw."+axis,d,new[]{0f,0f,0f,0f,0f});
                var stride=motion==ResidentMotion.Walk?30f:motion==ResidentMotion.Limp?17f:0f;
                Curve(clip,"Left Leg","localEulerAnglesRaw.x",d,new[]{-stride,0,stride,0,-stride});
                Curve(clip,"Right Leg","localEulerAnglesRaw.x",d,new[]{stride,0,-stride,0,stride});
                Curve(clip,"Left Arm","localEulerAnglesRaw.x",d,new[]{stride*.8f,0,-stride*.8f,0,stride*.8f});
                Curve(clip,"Right Arm","localEulerAnglesRaw.x",d,new[]{-stride*.8f,0,stride*.8f,0,-stride*.8f});
                var bob=motion==ResidentMotion.Run?.045f:motion==ResidentMotion.Walk?.023f:.01f;
                Curve(clip,"Body","m_LocalPosition.y",d,new[]{body,body+bob,body,body+bob,body});
                Curve(clip,"Head","m_LocalPosition.y",d,new[]{head,head+bob,head,head+bob,head});
                if(motion==ResidentMotion.Idle||motion==ResidentMotion.Recover) Curve(clip,"Head","localEulerAnglesRaw.y",d,new[]{0f,-12f,0f,15f,0f});
                if(motion==ResidentMotion.Run)
                {
                    var mapping=JsonUtility.FromJson<Mapping>(File.ReadAllText(Folder+"/AngeRunMapping.json"));
                    foreach(var track in mapping.tracks)
                    {
                        var keys=new Keyframe[track.times.Length];for(var i=0;i<keys.Length;i++)keys[i]=new Keyframe(track.times[i],track.angles[i]);
                        var curve=new AnimationCurve(keys);for(var i=0;i<keys.Length;i++)curve.SmoothTangents(i,0);
                        AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(track.path,typeof(Transform),"localEulerAnglesRaw.x"),curve);
                    }
                    Curve(clip,"Body","localEulerAnglesRaw.x",d,new[]{12f,16f,12f,16f,12f});
                }
                if(motion==ResidentMotion.Guard)
                {
                    Curve(clip,"Left Arm","localEulerAnglesRaw.x",d,new[]{0f,-130f,-130f,-100f,0f});
                    Curve(clip,"Right Arm","localEulerAnglesRaw.x",d,new[]{0f,-130f,-130f,-100f,0f});
                    Curve(clip,"Body","localEulerAnglesRaw.x",d,new[]{0f,20f,24f,18f,0f});
                    Curve(clip,"Head","m_LocalPosition.y",d,new[]{head,head-.09f,head-.09f,head-.05f,head});
                }
                if(motion==ResidentMotion.Limp)
                {
                    Curve(clip,"Right Leg","localEulerAnglesRaw.x",d,new[]{7f,1f,-7f,0f,7f});
                    Curve(clip,"Right Arm","localEulerAnglesRaw.x",d,new[]{-35f,-39f,-35f,-32f,-35f});
                    Curve(clip,"Body","localEulerAnglesRaw.z",d,new[]{7f,10f,7f,4f,7f});
                }
                if(motion==ResidentMotion.Fall)
                {
                    Curve(clip,"Left Arm","localEulerAnglesRaw.z",d,new[]{0f,-45f,-55f,-20f,-10f});
                    Curve(clip,"Right Arm","localEulerAnglesRaw.z",d,new[]{0f,45f,55f,20f,10f});
                    Curve(clip,"Left Leg","localEulerAnglesRaw.x",d,new[]{0f,-24f,-35f,-15f,-12f});
                }
                EditorUtility.SetDirty(clip);
                var animatorState=machine.AddState(motion.ToString());animatorState.motion=clip;animatorState.writeDefaultValues=true;
                if(state==0)machine.defaultState=animatorState;
            }
            EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();return controller;
        }
        private static void Curve(AnimationClip clip,string path,string property,float duration,float[] values)
        {
            var keys=new Keyframe[values.Length];for(var i=0;i<keys.Length;i++)keys[i]=new Keyframe(duration*i/(keys.Length-1),values[i]);
            var curve=new AnimationCurve(keys);for(var i=0;i<keys.Length;i++)curve.SmoothTangents(i,0);
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),property),curve);
        }
    }
}
