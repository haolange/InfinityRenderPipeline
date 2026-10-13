using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("InfinityRenderer/Decal Component")]
    [HelpURL(InfinityTech.Rendering.Pipeline.InfinityDocumentation.BaseUrl + "decals.md")]
    public class DecalComponent : MonoBehaviour
    {
        public int drawOrder;
        [Tooltip("Scale of the projected UVs (object xz + 0.5).")]
        public Vector2 uvScale = Vector2.one;
        [Tooltip("Offset of the projected UVs.")]
        public Vector2 uvBias = Vector2.zero;
        [Range(0, 180), Tooltip("Surfaces turned this far (degrees) from the projector's up axis start to fade.")]
        public float startAngleFade = 180;
        [Range(0, 180), Tooltip("Surfaces turned this far from the projector are not decaled.")]
        public float endAngleFade = 180;

        static readonly int s_UVScaleBiasID = Shader.PropertyToID("_DecalUVScaleBias");
        static readonly int s_AngleFadeID = Shader.PropertyToID("_DecalAngleFade");
        MaterialPropertyBlock m_Block;

        bool m_Registered;

        void OnEnable()
        {
            EnsureUnitCube();
            ApplyDrawOrder();
            if (!m_Registered)
            {
                m_Registered = true;
                FGraphics.AddTask((RenderContext renderContext) =>
                {
                    renderContext.AddWorldDecal();
                });
            }
        }

        void OnDisable()
        {
            if (m_Registered)
            {
                m_Registered = false;
                FGraphics.AddTask((RenderContext renderContext) =>
                {
                    renderContext.RemoveWorldDecal();
                });
            }
        }

        void OnValidate()
        {
            EnsureUnitCube();
            ApplyDrawOrder();
        }

        void ApplyDrawOrder()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.rendererPriority = drawOrder;
                ApplyProjection(meshRenderer);
            }
        }

        // Projector parameters (Shaders/ShaderLibrary/DecalProjection.hlsl) on this renderer's property block.
        void ApplyProjection(MeshRenderer meshRenderer)
        {
            m_Block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(m_Block);
            m_Block.SetVector(s_UVScaleBiasID, new Vector4(uvScale.x, uvScale.y, uvBias.x, uvBias.y));
            m_Block.SetVector(s_AngleFadeID, AngleFadeParameters(startAngleFade, endAngleFade));
            meshRenderer.SetPropertyBlock(m_Block);
        }

        // fade = saturate(cos(angle) * x + y): 1 up to the start angle, 0 from the end angle; z = 0 disables the fade.
        public static Vector4 AngleFadeParameters(float startDegrees, float endDegrees)
        {
            if (startDegrees >= 180 && endDegrees >= 180)
                return Vector4.zero;
            float end = Mathf.Max(startDegrees, endDegrees);
            float cosStart = Mathf.Cos(startDegrees * Mathf.Deg2Rad);
            float cosEnd = Mathf.Cos(end * Mathf.Deg2Rad);
            float scale = 1.0f / Mathf.Max(cosStart - cosEnd, 1e-4f);
            return new Vector4(scale, -cosEnd * scale, 1, 0);
        }

        void EnsureUnitCube()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                return;
            }

            if (meshFilter.sharedMesh != null)
            {
                return;
            }

            meshFilter.sharedMesh = GetUnitCube();
        }

        static Mesh GetUnitCube()
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (cube != null)
            {
                return cube;
            }

            cube = new Mesh { name = "InfinityDecalUnitCube" };
            cube.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f)
            };
            cube.triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,
                1, 2, 6, 1, 6, 5,
                5, 6, 7, 5, 7, 4,
                4, 7, 3, 4, 3, 0,
                3, 7, 6, 3, 6, 2,
                4, 0, 1, 4, 1, 5
            };
            cube.RecalculateNormals();
            cube.RecalculateBounds();
            return cube;
        }
    }
}
