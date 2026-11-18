using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public static class GraphicsUtility
    {
        internal static bool VolumeComponentActive(VolumeComponent component)
        {
            if (component == null)
            {
                return false;
            }

            if (component is InfinityTech.Rendering.Volume.IInfinityVolumeActivity activity)
            {
                return activity.IsActive();
            }

            return component.active;
        }

        internal static Material s_BlitMaterial;

        internal static Material BlitMaterial
        {
            get
            {
                return s_BlitMaterial;
            }
        }

        private static Mesh m_FullScreenMesh;

        internal static Mesh FullScreenMesh
        {
            get
            {
                if (m_FullScreenMesh != null)
                {
                    return m_FullScreenMesh;
                }

                m_FullScreenMesh = new Mesh { name = "FullScreen Mesh" };
                m_FullScreenMesh.vertices = new Vector3[] {new Vector3(-1f, -1f, 0f), new Vector3(-1f,  3f, 0f), new Vector3( 3f, -1f, 0f)};
                m_FullScreenMesh.SetIndices(new int[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
                m_FullScreenMesh.UploadMeshData(false);
                return m_FullScreenMesh;
            }
        }
    }
}
