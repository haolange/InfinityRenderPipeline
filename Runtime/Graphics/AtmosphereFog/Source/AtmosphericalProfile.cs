using UnityEngine;

namespace InfinityTech.Rendering.Feature
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/AtmosphericalProfile", order = 359)]
    public sealed class AtmosphericalProfile : ScriptableObject
    {
        [Header("PlanetSetting")]
        [Min(10000)]
        public float Radius = 6371e3f;

        [Min(100)]
        public float Thickness = 8e3f;

        [Header("ScatterSetting")]
        [Range(0.01f, 100f)]
        public float Brightness = 1;

        public bool DrawGround = false;

        public Color GroundAlbedo = new Color(0.25f, 0.25f, 0.25f);

        public Color RayleighScatter = new Color(0.1752f, 0.40785f, 1f);

        [Min(0)]
        public float MieStrength = 1;

        [Min(0)]
        public float OzoneStrength = 1;

        [Min(0)]
        public float RayleighStrength = 1;

        [Min(0)]
        public float MultiScatterStrength = 1f;

        [Range(0.0001f, 0.03f)]
        public float SunSolidAngle = (0.5f / 180.0f * Mathf.PI);

        [Header("Material")]
        public Material LUTMaterial;
        public ComputeShader LUTCompute;
    }
}
