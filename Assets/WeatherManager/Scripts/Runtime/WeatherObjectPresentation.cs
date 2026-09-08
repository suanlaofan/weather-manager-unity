using UnityEngine;

namespace WeatherManager
{
    public sealed class WeatherObjectPresentation : MonoBehaviour
    {
        [SerializeField] private Transform motionPivot;
        [SerializeField] private MeshRenderer[] coatings;
        [SerializeField] private DamageableKind kind;
        [SerializeField] private WorldBuilder world;
        [SerializeField] private DisasterSimulation simulation;
        private GameManager manager;
        private MrTabletopExperience mr;
        private MaterialPropertyBlock block;
        private float clock,wind,snow,wet,dry,damageAge;
        private bool animatingDamage;
        private Quaternion fromRotation,toRotation;
        private Vector3 fromScale,toScale;
        public void Configure(Transform pivot,MeshRenderer[] overlay,DamageableKind type,WorldBuilder town,DisasterSimulation sim)
        { motionPivot=pivot;coatings=overlay;kind=type;world=town;simulation=sim; }
        public void BeginDamagePose(Quaternion rotation,Vector3 scale)
        {
            fromRotation=transform.localRotation;fromScale=transform.localScale;
            toRotation=rotation;toScale=scale;damageAge=0;animatingDamage=true;
        }
        public void ResetPresentation()
        {
            animatingDamage=false;clock=wind=snow=wet=dry=0;
            if(motionPivot!=null) {motionPivot.localRotation=Quaternion.identity;motionPivot.localPosition=Vector3.zero;}
            if(coatings!=null)foreach(var coating in coatings)if(coating!=null)coating.enabled=false;
        }
        private void Update()
        {
            if(world==null||simulation==null)return;
            manager ??= FindFirstObjectByType<GameManager>();
            mr ??= FindFirstObjectByType<MrTabletopExperience>();
            var delta=Mathf.Min(Time.unscaledDeltaTime,.05f)*(manager!=null?manager.TimeScale:1);
            if(delta<=0)return;
            clock+=delta;
            if(animatingDamage)
            {
                damageAge+=delta;var t=Mathf.SmoothStep(0,1,damageAge/.65f);
                transform.localRotation=Quaternion.Slerp(fromRotation,toRotation,t);
                transform.localScale=Vector3.Lerp(fromScale,toScale,t);
                if(t>=1)animatingDamage=false;
            }
            var hazard=simulation.Sample(world.GetGameplayPosition(transform));
            var blend=1-Mathf.Exp(-delta*3);
            wind=Mathf.Lerp(wind,hazard.Wind,blend);snow=Mathf.Lerp(snow,hazard.Snow,blend);
            wet=Mathf.Lerp(wet,Mathf.Clamp01(hazard.Flood+hazard.Moisture*.25f),blend);dry=Mathf.Lerp(dry,hazard.Heat,blend);
            if(motionPivot!=null&&(kind==DamageableKind.Tree||kind==DamageableKind.Farm))
            {
                var phase=transform.localPosition.x*.7f+transform.localPosition.z*.41f;
                var gust=.65f+.35f*Mathf.Sin(clock*3.4f+phase);
                var axis=new Vector3(.35f,0,-1).normalized;
                var lean=(kind==DamageableKind.Tree?6f:1.4f)*wind*gust;
                motionPivot.localRotation=Quaternion.AngleAxis(lean,axis);
            }
            if(coatings==null)return;
            block ??=new MaterialPropertyBlock();
            block.SetFloat("_Snow",snow);block.SetFloat("_Wet",wet);
            block.SetFloat("_Heat",kind==DamageableKind.Tree||kind==DamageableKind.Farm?dry:0);
            block.SetVector("_WeatherUp",mr!=null&&mr.TabletopIsland!=null?mr.TabletopIsland.up:Vector3.up);
            foreach(var coating in coatings)if(coating!=null)
            {
                coating.enabled=snow>.035f||wet>.2f||dry>.25f;
                if(coating.enabled)coating.SetPropertyBlock(block);
            }
        }
    }
}
