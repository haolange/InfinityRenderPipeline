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
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.AddWorldView(this);
            });
         }

        protected override void OnUpdate()
        {
            base.OnUpdate();
        }

        protected override void OnTransformChange()
        {
            base.OnTransformChange();
        }

        protected override void UnRegister()
        {
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.RemoveWorldView(this);
            });
        }

        private void ResizeHistoryBuffer()
        {

        }
    }
}
