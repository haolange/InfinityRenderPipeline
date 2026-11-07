using System;
using UnityEngine;
using InfinityTech.Component;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>One LOD level of a <see cref="MeshAsset"/>.</summary>
    [Serializable]
    public struct MeshLODInfo : IEquatable<MeshLODInfo>
    {
        /// <summary>The level's lower screen-size bound, as LODGroup <c>screenRelativeTransitionHeight</c>.</summary>
        public float screenSize;
        /// <summary>
        /// <c>materialSlot[s]</c> is the index into the asset's materials (and the component's material array) of submesh s.
        /// A submesh at or beyond the array length has no material slot and draws nothing, as a Unity renderer with fewer
        /// materials than submeshes.
        /// </summary>
        public int[] materialSlot;

        /// <summary>The material slot of <paramref name="submesh"/>, or -1 when it has none.</summary>
        public int GetMaterialSlot(int submesh) => materialSlot != null && submesh < materialSlot.Length ? materialSlot[submesh] : -1;

        public bool Equals(MeshLODInfo other)
        {
            if (!screenSize.Equals(other.screenSize)) return false;
            int count = materialSlot?.Length ?? 0;
            if (count != (other.materialSlot?.Length ?? 0)) return false;
            for (int i = 0; i < count; ++i)
                if (materialSlot[i] != other.materialSlot[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is MeshLODInfo other && Equals(other);

        public override int GetHashCode()
        {
            int hash = screenSize.GetHashCode();
            if (materialSlot != null)
                for (int i = 0; i < materialSlot.Length; ++i) hash = unchecked(hash * 397 ^ materialSlot[i]);
            return hash;
        }
    }

    /// <summary>
    /// A mesh with its LOD chain, shared by every <see cref="MeshComponent"/> that references it. <c>meshes[i]</c> is LOD i
    /// (one mesh may fill several levels) and <c>lODInfos[i]</c> its screen size and material slots. <c>materials</c> are
    /// authoring defaults that the Editor copies into a component once when the asset is assigned; the component's
    /// material array, indexed by material slot, is the runtime authority.
    /// </summary>
    [CreateAssetMenu(menuName = "Rendering/Infinity/Static Mesh Asset", order = 358)]
    public class MeshAsset : ScriptableObject
    {
        public const float DefaultFadeWidth = 0.2f;

#if UNITY_EDITOR
        // Migration bridge: deleted after the 0.5 migration.
        [SerializeField, HideInInspector] private GameObject target;
#endif

        [Header("Mesh")]
        [SerializeField] private Mesh[] meshes;

        [Header("Material")]
        [SerializeField] private Material[] materials;

        [Header("LOD")]
        [SerializeField] private MeshLODInfo[] lODInfos;
        [SerializeField, Range(0.01f, 0.99f)] private float fadeWidth = DefaultFadeWidth;

        public ReadOnlySpan<Mesh> Meshes => meshes;
        public ReadOnlySpan<Material> Materials => materials;
        public int lodCount => lODInfos?.Length ?? 0;
        /// <summary>The cross-fade band above each LOD threshold, as a fraction of the gap to the previous threshold.</summary>
        public float FadeWidth => fadeWidth;

        /// <summary>The lower screen-size bound of LOD <paramref name="level"/>.</summary>
        public float GetScreenSize(int level) => lODInfos[level].screenSize;

        /// <summary>The material slot of <paramref name="submesh"/> in LOD <paramref name="level"/>, or -1 when it has none.</summary>
        public int GetMaterialSlot(int level, int submesh) => lODInfos[level].GetMaterialSlot(submesh);

        internal bool LodEquals(int level, in MeshLODInfo other) => lODInfos[level].Equals(other);

        /// <summary>The world LOD chain of this asset. Call <see cref="ThrowIfInvalid"/> first.</summary>
        public MeshLodChain BuildLodChain()
        {
            int count = lodCount;
            if (count < 1 || count > MeshLodChain.MaxLevels) throw new InvalidOperationException($"MeshAsset '{name}' has {count} LOD levels; 1 to {MeshLodChain.MaxLevels} are supported.");
            Span<float> sizes = stackalloc float[MeshLodChain.MaxLevels];
            for (int i = 0; i < count; ++i) sizes[i] = lODInfos[i].screenSize;
            return MeshLodChain.Create(sizes.Slice(0, count), fadeWidth);
        }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> unless the asset has 1 to 8 levels, one non-null mesh per level,
        /// screen sizes and fade width that <see cref="MeshLodChain.Create"/> accepts, and material slots inside
        /// <c>materials</c>. Submesh counts are not checked here: they belong to the live meshes.
        /// </summary>
        public void ThrowIfInvalid() => ThrowIfInvalid(this, meshes, materials, lODInfos, fadeWidth);

        /// <param name="owner">Names the data in messages; read only when throwing.</param>
        internal static void ThrowIfInvalid(UnityEngine.Object owner, Mesh[] meshes, Material[] materials, MeshLODInfo[] lods, float fadeWidth)
        {
            string assetName() => owner != null ? owner.name : "<unnamed>";
            int count = lods?.Length ?? 0;
            if (count < 1 || count > MeshLodChain.MaxLevels)
                throw new InvalidOperationException($"MeshAsset '{assetName()}' has {count} LOD levels; 1 to {MeshLodChain.MaxLevels} are supported.");
            if ((meshes?.Length ?? 0) != count)
                throw new InvalidOperationException($"MeshAsset '{assetName()}' has {meshes?.Length ?? 0} meshes for {count} LOD levels; each level needs one mesh.");
            int materialCount = materials?.Length ?? 0;
            Span<float> sizes = stackalloc float[MeshLodChain.MaxLevels];
            for (int i = 0; i < count; ++i)
            {
                if (meshes[i] == null) throw new InvalidOperationException($"MeshAsset '{assetName()}' has no mesh in LOD {i}.");
                int[] slots = lods[i].materialSlot;
                if (slots != null)
                    for (int s = 0; s < slots.Length; ++s)
                        if (slots[s] < 0 || slots[s] >= materialCount)
                            throw new InvalidOperationException($"MeshAsset '{assetName()}' LOD {i} submesh {s} uses material slot {slots[s]}; the asset has {materialCount} slots.");
                sizes[i] = lods[i].screenSize;
            }
            try { MeshLodChain.Create(sizes.Slice(0, count), fadeWidth); }
            catch (ArgumentException exception) { throw new InvalidOperationException($"MeshAsset '{assetName()}': {exception.Message}", exception); }
        }

        /// <summary>
        /// Replaces the asset's data without validating it (tests build invalid assets this way; other callers validate
        /// first) and notifies the components that reference it.
        /// </summary>
        internal void Initialize(Mesh[] meshes, Material[] materials, MeshLODInfo[] lods, float fadeWidth)
        {
            this.meshes = meshes;
            this.materials = materials;
            lODInfos = lods;
            this.fadeWidth = fadeWidth;
            MeshComponent.NotifyGeometryChanged(this);
        }

        /// <summary>One level at screen size 0 whose submesh s uses slot s; the materials are padded to the submesh count.</summary>
        internal void InitializeSingleLevel(Mesh mesh, ReadOnlySpan<Material> defaults)
        {
            int subMeshCount = mesh.subMeshCount;
            var slots = new Material[Math.Max(defaults.Length, subMeshCount)];
            defaults.CopyTo(slots);
            var mapping = new int[subMeshCount];
            for (int s = 0; s < subMeshCount; ++s) mapping[s] = s;
            Initialize(new[] { mesh }, slots, new[] { new MeshLODInfo { screenSize = 0, materialSlot = mapping } }, DefaultFadeWidth);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Inspector, Undo and reimport edits re-sync the components; nothing is saved here.
            MeshComponent.NotifyGeometryChanged(this);
        }
#endif

        // Destroying or unloading the asset re-syncs its components, which then stop drawing it.
        void OnDisable() => MeshComponent.NotifyGeometryChanged(this);
    }
}
