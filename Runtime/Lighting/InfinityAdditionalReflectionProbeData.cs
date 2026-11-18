using UnityEngine;
using InfinityTech.Core;

namespace InfinityTech.Component
{
    public enum EReflectionProbeInfluence
    {
        Box = 0,
        Sphere = 1,
    }

    // Only what the native ReflectionProbe lacks: a spherical influence volume. Size, center, blend distance,
    // importance, intensity, box projection and the captured texture stay the ReflectionProbe's.
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
