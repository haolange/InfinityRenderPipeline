using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("InfinityRenderer/Decal Component")]
    public class DecalComponent : MonoBehaviour
    {
        public int drawOrder;

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
            }
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
