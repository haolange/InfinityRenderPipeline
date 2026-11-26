using UnityEngine;
using InfinityTech.Core;

namespace InfinityTech.Component
{
    public enum EReflectionProbeInfluence
    {
        Box = 0,
        Sphere = 1,
        OrientedBox = 2,
    }

    public enum EReflectionProjectionProxy
    {
        Auto = 0,
        None = 1,
        Box = 2,
        Sphere = 3,
    }

    // Infinity-specific reflection probe extensions layered cleanly over Unity ReflectionProbe: influence shape
    // (including OrientedBox and Sphere), independent projection proxy, and custom capture origin.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReflectionProbe))]
    [AddComponentMenu("InfinityRenderer/Additional Reflection Probe Data")]
    [HelpURL(InfinityTech.Core.InfinityDocumentation.BaseUrl + "reflection-probes.md")]
    public sealed class InfinityAdditionalReflectionProbeData : EntityComponent
    {
        public EReflectionProbeInfluence influence = EReflectionProbeInfluence.Box;
        [Tooltip("Sphere influence radius in meters, centered on the probe box (0 = half the largest box side).")]
        [Min(0)] public float sphereRadius;

        [Header("Projection Proxy & Capture Origin")]
        public EReflectionProjectionProxy projectionProxy = EReflectionProjectionProxy.Auto;
        [Tooltip("Custom projection proxy half-extents in local box space (used when Projection Proxy is Box).")]
        public Vector3 projectionBoxExtents = Vector3.one;
        [Min(0), Tooltip("Custom projection proxy sphere radius (used when Projection Proxy is Sphere; 0 falls back to influence radius).")]
        public float projectionSphereRadius;
        public bool useCustomCaptureOrigin;
        [Tooltip("Capture origin offset relative to the influence volume center in local probe space.")]
        public Vector3 captureOriginOffset;

        // The live set of probe extensions by ReflectionProbe entity id: the authority the scene world's probe table derives from.
        internal static readonly ComponentRegistry<InfinityAdditionalReflectionProbeData> s_Registry = new ComponentRegistry<InfinityAdditionalReflectionProbeData>();
        ulong m_RegisteredProbeId;

        protected override void OnRegister()
        {
            m_RegisteredProbeId = UnityEntityId.ToUInt64(GetComponent<ReflectionProbe>());
            s_Registry.Register(m_RegisteredProbeId, this);
        }

        protected override void UnRegister()
        {
            s_Registry.Unregister(m_RegisteredProbeId);
        }
    }
}
