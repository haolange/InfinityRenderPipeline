using System;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Command identity: shader/route/pass/mesh/section/material/revisions/flags plus bakedTextureSet.
    /// Field-level Equals is authoritative; GetHashCode is lookup acceleration only.
    /// </summary>
    public struct MeshDrawCommandKey : IEquatable<MeshDrawCommandKey>
    {
        public ulong shaderUnityId;
        public uint materialRoute;
        public int shaderPassIndex;
        public ulong meshUnityId;
        public int sectionIndex;
        public ulong materialUnityId;
        public uint materialRevision;
        public uint sectionRevision;
        public uint platformFeatureKey;
        public uint staticFlags;
        public int bakedTextureSet;

        public bool Equals(MeshDrawCommandKey other)
        {
            return shaderUnityId == other.shaderUnityId
                && materialRoute == other.materialRoute
                && shaderPassIndex == other.shaderPassIndex
                && meshUnityId == other.meshUnityId
                && sectionIndex == other.sectionIndex
                && materialUnityId == other.materialUnityId
                && materialRevision == other.materialRevision
                && sectionRevision == other.sectionRevision
                && platformFeatureKey == other.platformFeatureKey
                && staticFlags == other.staticFlags
                && bakedTextureSet == other.bakedTextureSet;
        }

        public override bool Equals(object obj)
        {
            return obj is MeshDrawCommandKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = shaderPassIndex;
                hash = (hash * 397) ^ meshUnityId.GetHashCode();
                hash = (hash * 397) ^ sectionIndex;
                hash = (hash * 397) ^ materialUnityId.GetHashCode();
                hash = (hash * 397) ^ (int)materialRevision;
                hash = (hash * 397) ^ (int)sectionRevision;
                hash = (hash * 397) ^ (int)platformFeatureKey;
                hash = (hash * 397) ^ (int)staticFlags;
                hash = (hash * 397) ^ shaderUnityId.GetHashCode();
                hash = (hash * 397) ^ (int)materialRoute;
                hash = (hash * 397) ^ bakedTextureSet;
                return hash;
            }
        }
    }

    public struct MeshPassCommand
    {
        public MeshDrawCommandKey key;
        public int memberBegin;
        public int memberCount;
    }

    public struct MeshPassContext
    {
        public MeshPassId passId;
        public int shaderPassIndex;
        public string lightModeTag;
        public EMeshBackendPolicy backendPolicy;
        public ulong viewKey;
        internal UnityEngine.ComputeBuffer previousTransforms;
    }
}
