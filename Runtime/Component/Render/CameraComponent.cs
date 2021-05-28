using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("InfinityRenderer/Camera Component")]
    public class CameraComponent : BaseComponent
    {
        public Camera unityCamera;
        public ProfilingSampler viewProfiler;

        public CameraComponent() : base()
        {

        }

        protected override void OnRegister()
        {
            GetWorld().AddWorldView(this);

            unityCamera = GetComponent<Camera>();
            viewProfiler = new ProfilingSampler(this.name);
        }

        protected override void EventPlay()
        {
            base.EventPlay();
        }

        protected override void EventTick()
        {
            base.EventTick();
        }

        protected override void OnTransformChange()
        {
            base.OnTransformChange();
        }

        protected override void UnRegister()
        {
            GetWorld().RemoveWorldView(this);
        }
    }
}
