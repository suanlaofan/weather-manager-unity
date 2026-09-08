using UnityEngine;

namespace WeatherManager
{
    public enum ResidentMotion { Idle, Walk, Run, Guard, Limp, Fall, Recover }
    [RequireComponent(typeof(Animator))]
    public sealed class ResidentMotionController : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController motions;
        private static readonly int[] States = { Animator.StringToHash("Idle"),Animator.StringToHash("Walk"),Animator.StringToHash("Run"),
            Animator.StringToHash("Guard"),Animator.StringToHash("Limp"),Animator.StringToHash("Fall"),Animator.StringToHash("Recover") };
        private ResidentMotion current;
        private bool initialized;
        public ResidentMotion Current => current;
        public void Configure(RuntimeAnimatorController library=null)
        {
            animator=GetComponent<Animator>();
            motions=library!=null?library:Resources.Load<RuntimeAnimatorController>("WeatherManager/Motions/ResidentMotions");
            animator.runtimeAnimatorController=motions; animator.applyRootMotion=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.speed=0;
            initialized=motions!=null;
        }
        public void ResetMotion(float phase)
        {
            if(!initialized)Configure(); if(!initialized)return;
            animator.Rebind(); current=ResidentMotion.Idle;
            animator.Play(States[0],0,Mathf.Repeat(phase,1)); animator.Update(0); animator.speed=0;
        }
        public void Tick(ResidentMotion motion,float speed,float delta,float phase)
        {
            if(!initialized)Configure(); if(!initialized||delta<=0)return;
            if(motion!=current)
            {
                var offset=motion==ResidentMotion.Run||motion==ResidentMotion.Walk||motion==ResidentMotion.Limp?Mathf.Repeat(phase,1):0;
                animator.CrossFadeInFixedTime(States[(int)motion],motion==ResidentMotion.Fall?.10f:.18f,0,offset);
                current=motion;
            }
            animator.speed=Mathf.Clamp(speed,.3f,2f);
            // GameManager supplies the simulation clock; automatic Animator evaluation stays frozen.
            animator.Update(delta); animator.speed=0;
        }
    }
}
