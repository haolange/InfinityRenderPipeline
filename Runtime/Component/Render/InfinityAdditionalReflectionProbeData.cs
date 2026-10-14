using UnityEngine;

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
    [HelpURL(InfinityTech.Rendering.Pipeline.InfinityDocumentation.BaseUrl + "reflection-probes.md")]
    public sealed class InfinityAdditionalReflectionProbeData : MonoBehaviour
    {
        public EReflectionProbeInfluence influence = EReflectionProbeInfluence.Box;
        [Tooltip("Sphere influence radius in meters, centered on the probe box (0 = half the largest box side).")]
        [Min(0)] public float sphereRadius;
    }
}
