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
    /// Content-side proxy of one mesh entity. Property edits mark the component dirty; once per frame the render context
    /// syncs every dirty component inside one <see cref="MeshWorldUpdate"/>. A component whose materials cannot be drawn
    /// is reported with its context and not drawn; it does not roll back the other components.
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
        [SerializeField] private EStateType movebility = EStateType.Static;

        [Header("Mesh")]
        [SerializeField] private Mesh meshAsset;

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

        MeshEntityId m_EntityId;
        MeshElementId[] m_ElementIds;
        Mesh m_SyncedMesh;
        int m_SyncedSubMeshCount;
        uint m_SyncedGeometryRevision;
        EStateType m_SyncedMobility;
        MeshEntityDesc m_SyncedDesc;
        int m_SyncedPriority;
        MaterialSnapshot[] m_SyncedMaterials;
        FAABB m_BoundBox;
        bool m_DirtyEnqueued;
        uint m_RegistrationEpoch;

        internal MeshEntityId entityId => m_EntityId;

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
            RefreshGeometryReference();
            s_Registered.Add(this);
            FGraphics.RegisterMesh(this, epoch);
        }

        protected override void UnRegister()
        {
            ++m_RegistrationEpoch;
            s_Registered.Remove(this);
            ReleaseGeometryReference();
            MeshEntityId entity = m_EntityId;
            // Clear local state immediately so UnRegister is idempotent across OnDisable and OnDestroy.
            ResetSyncState();
            m_DirtyEnqueued = false;
            transformPending = false;
            FGraphics.UnregisterMesh(this, entity);
        }

        public void MarkDirty()
        {
            if (m_DirtyEnqueued) return;
            m_DirtyEnqueued = true;
            RenderContext.EnqueueDirtyMesh(this);
        }

        void ResetSyncState()
        {
            m_EntityId = MeshEntityId.Invalid;
            m_ElementIds = null;
            m_SyncedMesh = null;
            m_SyncedMaterials = null;
        }

        /// <summary>Applies this component's current state to the world inside the frame's transaction.</summary>
        internal void Sync(MeshWorld world, in MeshWorldUpdate update)
        {
            m_DirtyEnqueued = false;
            RefreshGeometryReference();
            bool alive = world.IsAlive(m_EntityId);
            bool drawable = isActiveAndEnabled && meshAsset != null && materials != null;
            if (drawable && !TryValidate(out string error))
            {
                Debug.LogError($"MeshComponent '{name}' is not drawn: {error}", this);
                drawable = false;
            }
            if (!drawable)
            {
                if (alive) update.RemoveEntity(m_EntityId);
                ResetSyncState();
                return;
            }

            MeshEntityDesc desc = BuildDesc();
            if (!alive || m_SyncedMesh != meshAsset || m_SyncedSubMeshCount != m_Geometry.subMeshes || m_SyncedGeometryRevision != m_GeometryRevision)
            {
                if (alive) update.RemoveEntity(m_EntityId);
                Create(update, desc);
                return;
            }

            if (!desc.Equals(m_SyncedDesc))
            {
                update.SetEntityDesc(m_EntityId, desc);
                m_SyncedDesc = desc;
            }
            if (movebility != m_SyncedMobility)
            {
                update.SetEntityMobility(m_EntityId, movebility == EStateType.Dynamic ? transform : null, localBounds);
                m_SyncedMobility = movebility;
            }
            for (int i = 0; i < m_ElementIds.Length; ++i)
            {
                MaterialSnapshot material = ReadMaterial(i);
                if (material.id != m_SyncedMaterials[i].id || !material.state.Equals(m_SyncedMaterials[i].state))
                {
                    update.SetElementMaterial(m_ElementIds[i], material.id, material.state);
                    m_SyncedMaterials[i] = material;
                }
                if (renderPriority != m_SyncedPriority) update.SetElementPriority(m_ElementIds[i], renderPriority);
            }
            m_SyncedPriority = renderPriority;
        }

        void Create(in MeshWorldUpdate update, in MeshEntityDesc desc)
        {
            float4x4 localToWorld = transform.localToWorldMatrix;
            m_BoundBox = Geometry.CaculateWorldBound(localBounds, localToWorld);
            m_EntityId = update.CreateEntity(localToWorld, m_BoundBox, desc);
            int subMeshCount = m_Geometry.subMeshes;
            m_ElementIds = new MeshElementId[subMeshCount];
            m_SyncedMaterials = new MaterialSnapshot[subMeshCount];
            for (int i = 0; i < subMeshCount; ++i)
            {
                MaterialSnapshot material = ReadMaterial(i);
                m_ElementIds[i] = update.AddElement(m_EntityId, m_Geometry.identity, i, material.id, material.state, renderPriority);
                m_SyncedMaterials[i] = material;
            }
            if (movebility == EStateType.Dynamic) update.SetEntityMobility(m_EntityId, transform, localBounds);
            m_SyncedMesh = meshAsset;
            m_SyncedSubMeshCount = subMeshCount;
            m_SyncedGeometryRevision = m_GeometryRevision;
            m_SyncedMobility = movebility;
            m_SyncedDesc = desc;
            m_SyncedPriority = renderPriority;
        }

        MaterialSnapshot ReadMaterial(int submesh)
        {
            Material material = submesh < materials.Length ? materials[submesh] : null;
            if (material == null) return default;
            MaterialRouteUtility.ReadMeshRoute(material, out int route, out int stage, out int queue, out _);
            return new MaterialSnapshot { id = UnityEntityId.ToUInt64(material), state = new MeshMaterialState(queue, route, stage) };
        }

        /// <summary>
        /// Every assigned material must route through Infinity surface routing, read entity data through the mesh
        /// instancing interface and declare each pass its route enables.
        /// </summary>
        bool TryValidate(out string error)
        {
            error = null;
            foreach (Material material in materials)
            {
                if (material == null) continue;
                if (!MeshPassShaderUtility.SupportsMeshInstancing(material.shader))
                {
                    error = $"shader '{material.shader.name}' of material '{material.name}' does not declare InfinityMeshInstancing.";
                    return false;
                }
                int route, stage;
                try { MaterialRouteUtility.ReadMeshRoute(material, out route, out stage, out _, out _); }
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
