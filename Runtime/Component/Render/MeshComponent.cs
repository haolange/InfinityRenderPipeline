using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Component
{
    /// <summary>
    /// Content-side proxy of one mesh entity. The entity's LOD chain comes from its <see cref="MeshAsset"/>, and every
    /// submesh of every LOD level is one element carrying its level. Elements draw with the component's materials,
    /// indexed by the asset's material slots. Property edits mark the component dirty; once per frame the render context
    /// syncs every dirty component inside one <see cref="MeshWorldUpdate"/>. A component whose asset or materials cannot
    /// be drawn is reported with its context and not drawn; it does not roll back the other components.
    /// </summary>
    [ExecuteAlways]
#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects]
#endif
    [AddComponentMenu("InfinityRenderer/Mesh Component")]
    public partial class MeshComponent : EntityComponent
    {
        struct MaterialSnapshot
        {
            public ulong id;
            public MeshMaterialState state;
        }

        [Header("State")]
        [SerializeField] private EMeshMobility mobility = EMeshMobility.Static;
        // Migration bridge: deleted after the 0.5 migration. Only the migration tool reads it.
        [SerializeField, HideInInspector] private EMeshMobility movebility;

        [Header("Mesh")]
        [SerializeField] private MeshAsset asset;
        // Migration bridge: deleted after the 0.5 migration. Only the migration tool reads it.
        [SerializeField, HideInInspector] private Mesh meshAsset;

        [Header("Material")]
        [SerializeField] private Material[] materials;

        [Header("Lighting")]
        [SerializeField] private ECastShadowMethod castShadow = ECastShadowMethod.Off;
        [SerializeField] private bool receiveShadow = true;
        [SerializeField] private bool affectIndirectLighting = true;

        [Header("Rendering")]
        [SerializeField] private bool visible = true;
        [SerializeField] private ERenderingLayer renderingLayer = ERenderingLayer.LightLayerDefault;
        [SerializeField] private int renderPriority = 0;
        [SerializeField] private EMotionType motionVector = EMotionType.Object;

        /// <summary>What this component published to one world. Arrays are replaced, never edited in place.</summary>
        struct SyncState
        {
            public int worldId;
            public MeshEntityId entity;
            public MeshElementId[] elements;
            public MaterialSnapshot[] materials;
            public MeshAsset asset;
            /// <summary>Submesh count per LOD level, shared with the geometry state that never edits it in place.</summary>
            public int[] subMeshCounts;
            public uint geometryRevision;
            public Bounds localBounds;
            public EMeshMobility mobility;
            public MeshEntityDesc desc;
            public int priority;
        }

        // m_State matches the committed world; m_Next is staged while the frame transaction is open.
        SyncState m_State;
        SyncState m_Next;
        bool m_Staged;
        FAABB m_BoundBox;
        bool m_DirtyEnqueued;
        uint m_RegistrationEpoch;

        internal MeshEntityId entityId => m_State.entity;

        ulong SceneCullingMask
        {
            get
            {
#if UNITY_EDITOR
                return gameObject.sceneCullingMask;
#else
                return ulong.MaxValue;
#endif
            }
        }

        protected override void OnRegister()
        {
            uint epoch = ++m_RegistrationEpoch;
            s_Registered.Add(this);
            RefreshGeometryReference();
            FGraphics.RegisterMesh(this, epoch);
        }

        protected override void UnRegister()
        {
            ++m_RegistrationEpoch;
            s_Registered.Remove(this);
            ReleaseGeometryReference();
            SyncState published = m_State;
            // Clear local state immediately so UnRegister is idempotent across OnDisable and OnDestroy.
            ResetSyncState();
            m_DirtyEnqueued = false;
            transformPending = false;
            FGraphics.UnregisterMesh(this, published.entity, published.worldId);
        }

        public void MarkDirty()
        {
            if (m_DirtyEnqueued) return;
            m_DirtyEnqueued = true;
            RenderContext.EnqueueDirtyMesh(this);
        }

        void ResetSyncState()
        {
            m_State = default;
            m_Next = default;
            m_Staged = false;
        }

        bool IsPublishedIn(MeshWorld world, in SyncState state) => state.worldId == world.worldId && world.IsAlive(state.entity);

        /// <summary>
        /// Stages this component's current state in the frame's transaction. The render context publishes the staged
        /// state after the transaction commits and abandons it when the transaction rolls back.
        /// </summary>
        internal void Sync(MeshWorld world, in MeshWorldUpdate update)
        {
            m_DirtyEnqueued = false;
            RefreshGeometryReference();
            SyncState current = m_Staged ? m_Next : m_State;
            bool alive = IsPublishedIn(world, current);
            bool drawable = isActiveAndEnabled && asset != null && materials != null && m_Geometry != null;
            if (drawable && !TryValidate(out string error))
            {
                Debug.LogError($"MeshComponent '{name}' is not drawn: {error}", this);
                drawable = false;
            }
            if (!drawable)
            {
                if (alive) update.RemoveEntity(current.entity);
                Stage(default);
                return;
            }

            MeshEntityDesc desc = BuildDesc();
            GeometryState geometry = m_Geometry;
            if (!alive || !ReferenceEquals(current.asset, asset) || !SameCounts(current.subMeshCounts, geometry.subMeshCounts) || current.geometryRevision != geometry.revision)
            {
                if (alive) update.RemoveEntity(current.entity);
                Stage(Create(world, update, desc));
                return;
            }

            SyncState next = current;
            if (!desc.Equals(current.desc))
            {
                update.SetEntityDesc(current.entity, desc);
                next.desc = desc;
            }
            if (mobility != current.mobility)
            {
                update.SetEntityMobility(current.entity, mobility == EMeshMobility.Dynamic ? transform : null, current.localBounds);
                next.mobility = mobility;
            }
            for (int i = 0; i < current.elements.Length; ++i)
            {
                MaterialSnapshot material = ReadMaterial(geometry.elementSlot[i]);
                if (material.id != current.materials[i].id || !material.state.Equals(current.materials[i].state))
                {
                    if (ReferenceEquals(next.materials, current.materials)) next.materials = (MaterialSnapshot[])current.materials.Clone();
                    update.SetElementMaterial(current.elements[i], material.id, material.state);
                    next.materials[i] = material;
                }
                if (renderPriority != current.priority) update.SetElementPriority(current.elements[i], renderPriority);
            }
            next.priority = renderPriority;
            Stage(next);
        }

        void Stage(in SyncState next)
        {
            m_Next = next;
            m_Staged = true;
        }

        internal void PublishSync()
        {
            if (!m_Staged) return;
            m_State = m_Next;
            m_Next = default;
            m_Staged = false;
        }

        internal void AbandonSync()
        {
            m_Next = default;
            m_Staged = false;
        }

        SyncState Create(MeshWorld world, in MeshWorldUpdate update, in MeshEntityDesc desc)
        {
            GeometryState geometry = m_Geometry;
            float4x4 localToWorld = transform.localToWorldMatrix;
            m_BoundBox = Geometry.CaculateWorldBound(geometry.bounds, localToWorld);
            int elementCount = geometry.elementLod.Length;
            var state = new SyncState
            {
                worldId = world.worldId,
                entity = update.CreateEntity(localToWorld, m_BoundBox, desc, asset.BuildLodChain()),
                elements = new MeshElementId[elementCount],
                materials = new MaterialSnapshot[elementCount],
                asset = asset,
                subMeshCounts = geometry.subMeshCounts,
                geometryRevision = geometry.revision,
                localBounds = geometry.bounds,
                mobility = mobility,
                desc = desc,
                priority = renderPriority
            };
            for (int i = 0; i < elementCount; ++i)
            {
                int lod = geometry.elementLod[i];
                MaterialSnapshot material = ReadMaterial(geometry.elementSlot[i]);
                state.elements[i] = update.AddElement(state.entity, geometry.meshIds[lod], geometry.elementSubmesh[i], lod, material.id, material.state, renderPriority);
                state.materials[i] = material;
            }
            if (mobility == EMeshMobility.Dynamic) update.SetEntityMobility(state.entity, transform, state.localBounds);
            return state;
        }

        static bool SameCounts(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>The component's material at <paramref name="slot"/>; a missing slot or a null material is no material.</summary>
        MaterialSnapshot ReadMaterial(int slot)
        {
            Material material = slot >= 0 && slot < materials.Length ? materials[slot] : null;
            if (material == null) return default;
            MaterialRouteUtility.ReadMeshRoute(material, out int route, out int stage, out int queue, out Shader shader);
            return new MaterialSnapshot { id = UnityEntityId.ToUInt64(material), state = new MeshMaterialState(queue, route, stage, UnityEntityId.ToUInt64(shader)) };
        }

        /// <summary>
        /// The asset must be valid and its live meshes must match the cached geometry, and every material that a drawn
        /// slot selects must route through Infinity surface routing, read entity data through the mesh instancing interface
        /// and declare each pass its route enables.
        /// </summary>
        bool TryValidate(out string error)
        {
            error = null;
            try { asset.ThrowIfInvalid(); }
            catch (System.InvalidOperationException exception)
            {
                error = exception.Message;
                return false;
            }
            GeometryState geometry = m_Geometry;
            System.ReadOnlySpan<Mesh> meshes = asset.Meshes;
            if (meshes.Length != geometry.meshes.Length)
            {
                error = $"MeshAsset '{asset.name}' changed its LOD levels without a notification; call MeshComponent.NotifyGeometryChanged.";
                return false;
            }
            for (int i = 0; i < meshes.Length; ++i)
            {
                if (!ReferenceEquals(meshes[i], geometry.meshes[i]) || meshes[i].subMeshCount != geometry.subMeshCounts[i])
                {
                    error = $"mesh '{meshes[i].name}' (LOD {i} of MeshAsset '{asset.name}') changed in place; call MeshComponent.NotifyGeometryChanged.";
                    return false;
                }
            }
            foreach (int slot in geometry.drawnSlots)
            {
                Material material = slot < materials.Length ? materials[slot] : null;
                if (material == null) continue;
                if (!MeshPassShaderUtility.SupportsMeshInstancing(material.shader))
                {
                    error = $"shader '{material.shader.name}' of material '{material.name}' does not declare InfinityMeshInstancing.";
                    return false;
                }
                int route, stage;
                try { MaterialRouteUtility.ReadMeshRoute(material, out route, out stage, out _, out Shader _); }
                catch (System.InvalidOperationException exception)
                {
                    error = exception.Message;
                    return false;
                }
                foreach (MeshPassDesc pass in MeshPassPresets.all)
                {
                    if (!MaterialRouteUtility.RouteEnablesPass(route, stage, pass.lightMode)) continue;
                    if (MeshPassShaderUtility.FindPassIndex(material, pass.lightMode) < 0)
                    {
                        error = $"material '{material.name}' routes to '{pass.lightMode}', which shader '{material.shader.name}' does not declare.";
                        return false;
                    }
                }
            }
            return true;
        }

        MeshEntityDesc BuildDesc()
        {
            EMeshEntityFlags flags = EMeshEntityFlags.None;
            if (visible) flags |= EMeshEntityFlags.Visible;
            if (receiveShadow) flags |= EMeshEntityFlags.ReceiveShadow;
            if (castShadow != ECastShadowMethod.Off) flags |= EMeshEntityFlags.CastShadow;
            if (affectIndirectLighting) flags |= EMeshEntityFlags.AffectIndirect;
            if (motionVector == EMotionType.Object) flags |= EMeshEntityFlags.ObjectMotion;
            return new MeshEntityDesc
            {
                layerMask = 1 << gameObject.layer,
                sceneCullingMask = SceneCullingMask,
                renderingLayerMask = (uint)renderingLayer,
                flags = flags
            };
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Geometry.DrawBound(m_BoundBox, Color.blue);
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled || !gameObject.scene.IsValid()) return;
            // Inspector edits are an explicit resource notification.
            NotifyGeometryChanged();
        }
#endif
    }
}
