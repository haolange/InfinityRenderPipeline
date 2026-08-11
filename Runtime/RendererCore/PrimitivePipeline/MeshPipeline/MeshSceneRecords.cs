using System.Runtime.InteropServices;
using InfinityTech.Core.Geometry;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshInstanceRecord
    {
        public TransformId transform;
        public FBound worldBounds;
        public int layerMask;
        public uint renderingLayerMask;
        public EMeshInstanceFlags flags;
        public EMotionType motionType;
        public ECastShadowMethod castShadow;
        public int drawStart;
        public int drawCount;
        public EGeometrySourceKind geometrySource;
        public uint deformationDataId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TransformRecord
    {
        public float4x4 current;
        public float4x4 previous;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MeshDrawRecord
    {
        public MeshInstanceId instance;
        public MeshSectionId section;
        public MaterialDataId material;
        public EPassEligibility eligibility;
        public int renderQueue;
        public int priority;
        public int meshUnityId;
        public int materialUnityId;
        public int sectionIndex;
        /// <summary>
        /// Draw-level static / batching flags (e.g. 1 = Static mobility). Feeds MeshPassDrawCacheKey.
        /// </summary>
        public uint staticFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MeshSectionRecord
    {
        public int meshUnityId;
        public int sectionIndex;
        public EGeometrySourceKind geometrySource;
        public int refCount;
        public uint revision;
        /// <summary>
        /// Geometry fingerprint (e.g. hash of subMeshCount/vertexCount). Stored separately;
        /// changes bump <see cref="revision"/> so template cache keys observe geometry edits.
        /// </summary>
        public uint geometryRevision;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialDataRecord
    {
        public int materialUnityId;
        public int renderQueue;
        public uint revision;
        public int refCount;
    }

    public struct MeshSceneRevisionSnapshot
    {
        public int StructuralRevision;
        public int ContentRevision;
        public int VisibilityRevision;
    }

    /// <summary>
    /// Transaction snapshot for revisions + dirty ranges only.
    /// highWater / free-list membership are owned by Free*/Restore*/deferred reclaim and may grow monotonically.
    /// </summary>
    public struct MeshSceneStateSnapshot
    {
        public int StructuralRevision;
        public int ContentRevision;
        public int VisibilityRevision;

        public int TransformDirtyBegin;
        public int TransformDirtyEnd;
        public int BoundsDirtyBegin;
        public int BoundsDirtyEnd;
    }
}
