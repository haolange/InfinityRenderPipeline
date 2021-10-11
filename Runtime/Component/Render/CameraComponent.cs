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
        public RenderTexture historyTexture;
        public ProfilingSampler viewProfiler;

        protected override void OnRegister()
        {
            GetWorld().AddWorldView(this);
            unityCamera = GetComponent<Camera>();
            viewProfiler = new ProfilingSampler(this.name);
            historyTexture = new RenderTexture(unityCamera.pixelWidth, unityCamera.pixelHeight, 0, RenderTextureFormat.RGB111110Float, 0);
            historyTexture.depth = 0;
            historyTexture.anisoLevel = 0;
            historyTexture.antiAliasing = 1;
            historyTexture.useMipMap = false;
            historyTexture.bindTextureMS = false;
            historyTexture.autoGenerateMips = false;
            historyTexture.Create();
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
            historyTexture.Release();
            GetWorld().RemoveWorldView(this);
        }
    }
}
