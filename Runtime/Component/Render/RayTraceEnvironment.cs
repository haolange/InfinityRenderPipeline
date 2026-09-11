using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [AddComponentMenu("InfinityRenderer/Ray Tracing Environment")]
    public sealed class RayTraceEnvironment : MonoBehaviour
    {
        public ERayTracingBackend backend => InfinityRayTracingEnvironment.lastBackend;

        void OnEnable()
        {
            InfinityRayTracingEnvironment.ResolveBackend();
        }
    }
}
