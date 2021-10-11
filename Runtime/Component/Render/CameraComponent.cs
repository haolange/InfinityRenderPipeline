using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("InfinityRenderer/Camera Component")]
    public class CameraComponent : BaseComponent
    {
        public Camera unityCamera;
        public RTHandle historyTexture;
        public ProfilingSampler viewProfiler;

        protected override void OnRegister()
        {
            GetWorld().AddWorldView(this);
            unityCamera = GetComponent<Camera>();
            viewProfiler = new ProfilingSampler(this.name);
            historyTexture = RTHandles.Alloc(unityCamera.pixelWidth, unityCamera.pixelHeight, 0, DepthBits.None, GraphicsFormat.B10G11R11_UFloatPack32, FilterMode.Bilinear, TextureWrapMode.Repeat, TextureDimension.Tex2D, false, false, false, false);
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
            RTHandles.Release(historyTexture);
            GetWorld().RemoveWorldView(this);
        }
    }
}
