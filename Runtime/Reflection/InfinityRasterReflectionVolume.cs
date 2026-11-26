using UnityEngine;
using InfinityTech.Core;

namespace InfinityTech.Component
{
    /// <summary>
    /// Programmable raster reflection volume drawn in <c>RasterReflectionPass</c> before deferred lighting.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("InfinityRenderer/Raster Reflection Volume")]
    [HelpURL(InfinityTech.Core.InfinityDocumentation.BaseUrl + "reflection-probes.md")]
    public sealed class InfinityRasterReflectionVolume : MonoBehaviour
    {
        public int drawOrder;
        [Min(0.01f), Tooltip("Edge fade distance inside the unit cube in local axes.")]
        public float blendDistance = 0.25f;

        internal static readonly ComponentRegistry<InfinityRasterReflectionVolume> s_Registry = new ComponentRegistry<InfinityRasterReflectionVolume>();
        static readonly int s_BlendDistanceID = Shader.PropertyToID("_ReflectionBlendDistance");

        ulong m_RegistryId;
        bool m_Registered;
        MaterialPropertyBlock m_Block;

        void OnEnable()
        {
            EnsureUnitCube();
            ApplyProperties();
            if (!m_Registered)
            {
                m_Registered = true;
                m_RegistryId = UnityEntityId.ToUInt64(this);
                s_Registry.Register(m_RegistryId, this);
            }
        }

        void OnDisable()
        {
            if (m_Registered)
            {
                m_Registered = false;
                s_Registry.Unregister(m_RegistryId);
            }
        }

        void OnValidate()
        {
            EnsureUnitCube();
            ApplyProperties();
        }

        void ApplyProperties()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.rendererPriority = drawOrder;
                m_Block ??= new MaterialPropertyBlock();
                meshRenderer.GetPropertyBlock(m_Block);
                m_Block.SetFloat(s_BlendDistanceID, Mathf.Max(blendDistance, 1e-4f));
                meshRenderer.SetPropertyBlock(m_Block);
            }
        }

        void EnsureUnitCube()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh != null)
            {
                return;
            }
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            meshFilter.sharedMesh = cube != null ? cube : Resources.GetBuiltinResource<Mesh>("Cube");
        }
    }
}
