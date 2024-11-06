using UnityEngine;

namespace InfinityTech.Rendering.Feature
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/AtmosphericalProfile", order = 359)]
    public sealed class AtmosphericalProfile : ScriptableObject
    {
        [Header("Planet")]
        [Min(10000)]
        public float radius = 6371e3f;

        [Min(100)]
        public float thickness = 8e3f;

        [Header("Scatter")]
        [Range(0.01f, 100f)]
        public float brightness = 1;

        public bool drawGround = false;

        public Color groundAlbedo = new Color(0.25f, 0.25f, 0.25f);

        public Color rayleighScatter = new Color(0.1752f, 0.40785f, 1f);

        [Min(0)]
        public float mieStrength = 1;

        [Min(0)]
        public float ozoneStrength = 1;

        [Min(0)]
        public float rayleighStrength = 1;

        [Min(0)]
        public float multiScatterStrength = 1f;

        [Range(0.0001f, 0.03f)]
        public float sunAngle = (0.5f / 180.0f * Mathf.PI);

        [Header("Material")]
        public Material material;
        public ComputeShader shader;
    }
}
