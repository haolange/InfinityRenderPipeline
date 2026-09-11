using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("InfinityRenderer/Additional Camera Data")]
    public sealed class InfinityAdditionalCameraData : BaseComponent
    {
        public LayerMask volumeLayerMask = ~0;
        public Transform volumeTrigger;
        public ESuperResolutionOverride superResolutionOverride = ESuperResolutionOverride.UseAsset;
        [System.NonSerialized] public ProfilingSampler viewProfiler;

        public Camera attachedCamera => GetComponent<Camera>();

        public static InfinityAdditionalCameraData GetOrCreate(Camera camera, bool undo)
        {
            if (camera == null)
            {
                throw new System.ArgumentNullException(nameof(camera));
            }

            if (camera.TryGetComponent(out InfinityAdditionalCameraData data))
            {
                return data;
            }

#if UNITY_EDITOR
            if (undo)
            {
                return UnityEditor.Undo.AddComponent<InfinityAdditionalCameraData>(camera.gameObject);
            }
#endif
            return camera.gameObject.AddComponent<InfinityAdditionalCameraData>();
        }

        protected override void OnRegister()
        {
            viewProfiler = new ProfilingSampler(name);
        }

        protected override void UnRegister()
        {
        }
    }
}
