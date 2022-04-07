using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("InfinityRenderer/Camera Component")]
    public class CameraComponent : BaseComponent
    {
        public Camera unityCamera;
        public ProfilingSampler viewProfiler;

        protected override void OnRegister()
        {
            unityCamera = GetComponent<Camera>();
            viewProfiler = new ProfilingSampler(this.name);
            FGraphics.AddTask((FRenderContext renderContext) =>
            {
                renderContext.AddWorldView(this);
            });
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
            FGraphics.AddTask((FRenderContext renderContext) =>
            {
                renderContext.RemoveWorldView(this);
            });
        }

        private void ResizeHistoryBuffer()
        {

        }
    }
}
