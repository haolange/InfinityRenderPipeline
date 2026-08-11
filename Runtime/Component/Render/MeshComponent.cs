using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.MeshPipeline;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Component
{
    /// <summary>
    /// Mesh proxy state machine: property change → snapshot diff → structural/lightweight path →
    /// MeshScene transaction → world Static/Dynamic list membership → MeshPassDrawCache revision.
    /// MarkDirty uses a shared dirty queue (not the Dynamic world list), so Dynamic→Static still syncs.
    /// </summary>
    [ExecuteAlways]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    [AddComponentMenu("InfinityRenderer/Mesh Component")]
    public class MeshComponent : EntityComponent
    {
        private struct MeshComponentSnapshot
        {
            public bool valid;
            public bool visible;
            public EStateType movebility;
            public int meshAssetId;
            public int subMeshCount;
            public uint geometryRevision;
            public int[] materialInstanceIds;
            public int[] materialRenderQueues;
            public ERenderingLayer renderingLayer;
            public int renderPriority;
            public ECastShadowMethod castShadow;
            public bool receiveShadow;
            public bool affectIndirect;
            public EMotionType motionVector;
        }

        [Header("State")]
        public EStateType movebility = EStateType.Static;

        [Header("Mesh")]
        public Mesh meshAsset;

        [Header("Material")]
        public Material[] materials;
#if UNITY_EDITOR
        private Material[] m_LastMaterials;
#endif

        [Header("Lighting")]
        public ECastShadowMethod castShadow = ECastShadowMethod.Off;
        public bool receiveShadow = true;
        public bool affectIndirectLighting = true;

        [Header("Rendering")]
        public bool visible = true;
        public ERenderingLayer renderingLayer = ERenderingLayer.LightLayerDefault;
        public int renderPriority = 0;
        public EMotionType motionVector = EMotionType.Object;

        private MeshInstanceId m_InstanceId;
        private TransformId m_TransformId;
        private MeshDrawId[] m_DrawIds;
        private FAABB m_BoundBox;
        private FSphere m_BoundSphere;
        private MeshComponentSnapshot m_Snapshot;
        private bool m_DirtyEnqueued;
        private uint m_GeometryRevision;
        private float4x4 m_LocalToWorldMatrix => transform.localToWorldMatrix;

        protected override void OnRegister()
        {
            UpdateBounds();
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                if (!this) { return; }
                RegisterToMeshScene(renderContext);
                AddWorldMesh(renderContext, movebility);
            });
        }

        protected override void OnTransformChange()
        {
            UpdateBounds();
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                if (!this) { return; }
                UpdateTransformInMeshScene(renderContext);
            });
        }

        protected virtual void OnStateTypeChange(in EStateType LastGeometryState)
        {
        }

        protected virtual void OnStaticMeshChange()
        {
        }

        protected override void EventPlay()
        {
        }

        protected override void EventTick()
        {
            if (!NeedsSync())
            {
                return;
            }

            MarkDirty();
        }

        protected override void UnRegister()
        {
            MeshInstanceId instanceId = m_InstanceId;
            EStateType mobility = movebility;
            MeshComponent self = this;

            // Clear local handles immediately so UnRegister is idempotent across OnDisable/OnDestroy.
            m_InstanceId = MeshInstanceId.Invalid;
            m_TransformId = TransformId.Invalid;
            m_DrawIds = null;
            m_Snapshot = default;
            m_DirtyEnqueued = false;

            FGraphics.AddTask((RenderContext renderContext) =>
            {
                RemoveInstanceById(renderContext, instanceId);
                RemoveWorldMesh(renderContext, mobility, self);
            });
        }

        public void MarkDirty()
        {
            if (m_DirtyEnqueued)
            {
                return;
            }

            m_DirtyEnqueued = true;
            RenderContext.EnqueueDirtyMesh(this);
        }

        public void SyncFromSnapshot(RenderContext renderContext)
        {
            m_DirtyEnqueued = false;

            if (!isActiveAndEnabled)
            {
                if (m_InstanceId.IsValid)
                {
                    UnRegisterFromMeshScene(renderContext);
                }

                m_Snapshot = default;
                return;
            }

            if (meshAsset == null || materials == null)
            {
                if (m_InstanceId.IsValid)
                {
                    UnRegisterFromMeshScene(renderContext);
                }

                m_Snapshot = default;
                return;
            }

            if (!m_InstanceId.IsValid || !m_Snapshot.valid || HasStructuralDiff())
            {
                // Bounds must be current before CreateInstance writes FAABB into MeshScene.
                UpdateBounds();

                bool hadInstance = m_InstanceId.IsValid;
                EStateType previousMobility = m_Snapshot.valid ? m_Snapshot.movebility : movebility;

                RegisterToMeshScene(renderContext);
                SyncWorldListAfterStructural(renderContext, hadInstance, previousMobility);
                return;
            }

            if (!HasLightweightDiff())
            {
                return;
            }

            ApplyLightweightUpdates(renderContext);
            CaptureSnapshot();
        }

#if UNITY_EDITOR
        private void DrawBound()
        {
            #if UNITY_EDITOR
            Geometry.DrawBound(m_BoundBox, Color.blue);

            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.DrawWireDisc(m_BoundBox.center + new float3(512, 0, 512), Vector3.up, m_BoundSphere.radius);
            UnityEditor.Handles.DrawWireDisc(m_BoundBox.center + new float3(512, 0, 512), Vector3.back, m_BoundSphere.radius);
            UnityEditor.Handles.DrawWireDisc(m_BoundBox.center + new float3(512, 0, 512), Vector3.right, m_BoundSphere.radius);
            #endif
        }

        void OnDrawGizmosSelected()
        {
            DrawBound();
            EventUpdate();
        }

        void OnValidate()
        {
            // Recompute geometry fingerprint so in-place mesh edits (vertex/submesh) dirties sync.
            m_GeometryRevision = ComputeGeometryRevision(meshAsset);
            MarkDirty();
        }
#endif

        private void AddWorldMesh(RenderContext renderContext, in EStateType stateType)
        {
            if(stateType == EStateType.Static)
            {
                renderContext.AddWorldStaticMesh(this);
            }

            if (stateType == EStateType.Dynamic)
            {
                renderContext.AddWorldDynamicMesh(this);
            }
        }

        private static void RemoveWorldMesh(RenderContext renderContext, in EStateType stateType, MeshComponent meshComponent)
        {
            // No Unity fake-null check: destroyed components must still leave world lists by reference.
            if (stateType == EStateType.Static)
            {
                renderContext.RemoveWorldStaticMesh(meshComponent);
            }

            if (stateType == EStateType.Dynamic)
            {
                renderContext.RemoveWorldDynamicMesh(meshComponent);
            }
        }

        private void SyncWorldListAfterStructural(RenderContext renderContext, bool hadInstance, EStateType previousMobility)
        {
            // Prefer live world-list membership; fall back to snapshot mobility.
            // UnRegister already RemoveWorldMesh — do not double-remove there.
            EStateType? membership = TryGetWorldListMembership(renderContext);
            EStateType oldMobility = membership ?? previousMobility;

            if (oldMobility == movebility)
            {
                // First MeshScene registration when not yet on a world list (OnRegister may have pre-added).
                if (!hadInstance && membership == null)
                {
                    AddWorldMesh(renderContext, movebility);
                }

                return;
            }

            if (membership != null)
            {
                RemoveWorldMesh(renderContext, membership.Value, this);
            }
            else if (hadInstance)
            {
                RemoveWorldMesh(renderContext, previousMobility, this);
            }

            AddWorldMesh(renderContext, movebility);
            OnStateTypeChange(oldMobility);
        }

        private EStateType? TryGetWorldListMembership(RenderContext renderContext)
        {
            if (renderContext.GetWorldDynamicPrimitive().Contains(this))
            {
                return EStateType.Dynamic;
            }

            if (renderContext.GetWorldStaticMesh().Contains(this))
            {
                return EStateType.Static;
            }

            return null;
        }

        public void UpdateBounds()
        {
            if (!meshAsset) { return; }

            m_BoundBox = Geometry.CaculateWorldBound(meshAsset.bounds, m_LocalToWorldMatrix);
            m_BoundSphere = new FSphere(Geometry.CaculateBoundRadius(m_BoundBox), m_BoundBox.center);
        }

#if UNITY_EDITOR
        public void UpdateMaterial()
        {
            if(materials.Length != 0)
            {
                m_LastMaterials = new Material[materials.Length];
                for (int i = 0; i < m_LastMaterials.Length; ++i)
                {
                    m_LastMaterials[i] = materials[i];
                }
            }

            materials = new Material[meshAsset.subMeshCount];
            for (int i = 0; i < materials.Length; ++i)
            {
                if(i < m_LastMaterials.Length)
                {
                    materials[i] = m_LastMaterials[i];
                } else {
                    materials[i] = Resources.Load<Material>("Materials/M_DefaultLit");
                } 
            }
        }
#endif

        public static void RemoveInstanceById(RenderContext renderContext, MeshInstanceId instanceId)
        {
            if (renderContext == null || !instanceId.IsValid)
            {
                return;
            }

            MeshScene scene = renderContext.GetMeshScene();
            using (MeshSceneUpdate update = scene.BeginUpdate())
            {
                update.RemoveInstance(instanceId);
                update.Commit();
            }
        }

        public void RegisterToMeshScene(RenderContext renderContext)
        {
            if (meshAsset == null || materials == null)
            {
                if (m_InstanceId.IsValid)
                {
                    UnRegisterFromMeshScene(renderContext);
                }

                m_Snapshot = default;
                return;
            }

            MeshScene scene = renderContext.GetMeshScene();
            if (m_InstanceId.IsValid)
            {
                UnRegisterFromMeshScene(renderContext);
            }

            EMeshInstanceFlags flags = BuildInstanceFlags();
            EPassEligibility eligibility = BuildPassEligibility();

            using (MeshSceneUpdate update = scene.BeginUpdate())
            {
                m_TransformId = update.CreateTransform(m_LocalToWorldMatrix);
                m_InstanceId = update.CreateInstance(
                    m_TransformId,
                    m_BoundBox,
                    gameObject.layer >= 0 ? (1 << gameObject.layer) : ~0,
                    (uint)renderingLayer,
                    flags,
                    motionVector,
                    castShadow,
                    EGeometrySourceKind.IndexedMesh);

                int subMeshCount = meshAsset.subMeshCount;
                m_DrawIds = new MeshDrawId[subMeshCount];
                int meshUnityId = meshAsset.GetInstanceID();
                m_GeometryRevision = ComputeGeometryRevision(meshAsset);

                for (int i = 0; i < subMeshCount; ++i)
                {
                    Material material = (materials != null && i < materials.Length && materials[i] != null)
                        ? materials[i]
                        : null;
                    if (material == null)
                    {
                        m_DrawIds[i] = MeshDrawId.Invalid;
                        continue;
                    }

                    int renderQueue = material.renderQueue;
                    int priority = renderPriority + renderQueue;
                    uint staticFlags = movebility == EStateType.Static ? 1u : 0u;
                    m_DrawIds[i] = update.CreateDraw(
                        m_InstanceId,
                        meshUnityId,
                        i,
                        material.GetInstanceID(),
                        eligibility,
                        renderQueue,
                        priority,
                        EGeometrySourceKind.IndexedMesh,
                        m_GeometryRevision,
                        staticFlags);
                }

                update.Commit();
            }

            CaptureSnapshot();
        }

        public void UpdateTransformInMeshScene(RenderContext renderContext)
        {
            if (!m_InstanceId.IsValid || !m_TransformId.IsValid)
            {
                return;
            }

            MeshScene scene = renderContext.GetMeshScene();
            using (MeshSceneUpdate update = scene.BeginUpdate())
            {
                update.SetTransform(m_TransformId, m_LocalToWorldMatrix);
                update.SetBounds(m_InstanceId, m_BoundBox);
                update.Commit();
            }
        }

        public void UnRegisterFromMeshScene(RenderContext renderContext)
        {
            if (!m_InstanceId.IsValid)
            {
                m_TransformId = TransformId.Invalid;
                m_DrawIds = null;
                m_Snapshot = default;
                return;
            }

            MeshInstanceId instanceId = m_InstanceId;
            m_InstanceId = MeshInstanceId.Invalid;
            m_TransformId = TransformId.Invalid;
            m_DrawIds = null;
            m_Snapshot = default;

            RemoveInstanceById(renderContext, instanceId);
        }

        private void ApplyLightweightUpdates(RenderContext renderContext)
        {
            if (!m_InstanceId.IsValid)
            {
                return;
            }

            EMeshInstanceFlags flags = BuildInstanceFlags();
            EPassEligibility eligibility = BuildPassEligibility();
            uint renderingLayerMask = (uint)renderingLayer;

            MeshScene scene = renderContext.GetMeshScene();
            using (MeshSceneUpdate update = scene.BeginUpdate())
            {
                update.SetInstanceFlags(m_InstanceId, flags);
                update.SetInstanceRendering(m_InstanceId, renderingLayerMask, motionVector, castShadow);

                if (m_DrawIds != null)
                {
                    for (int i = 0; i < m_DrawIds.Length; ++i)
                    {
                        MeshDrawId drawId = m_DrawIds[i];
                        if (!drawId.IsValid)
                        {
                            continue;
                        }

                        Material material = (materials != null && i < materials.Length) ? materials[i] : null;
                        int renderQueue = 0;
                        if (material != null)
                        {
                            renderQueue = material.renderQueue;
                            // Updates draw.renderQueue and MaterialData revision (same id, new queue).
                            update.SetMaterial(drawId, material.GetInstanceID(), renderQueue);
                        }

                        update.SetDrawPriority(drawId, renderPriority + renderQueue);
                        update.SetDrawEligibility(drawId, eligibility);
                    }
                }

                update.Commit();
            }
        }

        private EMeshInstanceFlags BuildInstanceFlags()
        {
            EMeshInstanceFlags flags = EMeshInstanceFlags.None;
            if (visible) flags |= EMeshInstanceFlags.Visible;
            if (receiveShadow) flags |= EMeshInstanceFlags.ReceiveShadow;
            if (castShadow != ECastShadowMethod.Off) flags |= EMeshInstanceFlags.CastShadow;
            if (affectIndirectLighting) flags |= EMeshInstanceFlags.AffectIndirect;
            return flags;
        }

        private EPassEligibility BuildPassEligibility()
        {
            EPassEligibility eligibility = EPassEligibility.Depth | EPassEligibility.GBuffer | EPassEligibility.Forward;
            if (motionVector == EMotionType.Object)
            {
                eligibility |= EPassEligibility.Motion;
            }

            if (castShadow != ECastShadowMethod.Off)
            {
                eligibility |= EPassEligibility.Shadow;
            }

            return eligibility;
        }

        private bool NeedsSync()
        {
            if (meshAsset == null || materials == null)
            {
                return m_InstanceId.IsValid || m_Snapshot.valid;
            }

            if (!m_InstanceId.IsValid || !m_Snapshot.valid)
            {
                return true;
            }

            return HasStructuralDiff() || HasLightweightDiff();
        }

        private bool HasStructuralDiff()
        {
            int meshId = meshAsset != null ? meshAsset.GetInstanceID() : 0;
            int subMeshCount = meshAsset != null ? meshAsset.subMeshCount : 0;
            uint geometryRevision = ComputeGeometryRevision(meshAsset);
            if (!m_Snapshot.valid
                || m_Snapshot.movebility != movebility
                || m_Snapshot.meshAssetId != meshId
                || m_Snapshot.subMeshCount != subMeshCount
                || m_Snapshot.geometryRevision != geometryRevision)
            {
                return true;
            }

            int materialCount = materials != null ? materials.Length : 0;
            int snapshotCount = m_Snapshot.materialInstanceIds != null ? m_Snapshot.materialInstanceIds.Length : 0;
            if (materialCount != snapshotCount)
            {
                return true;
            }

            for (int i = 0; i < materialCount; ++i)
            {
                int materialId = materials[i] != null ? materials[i].GetInstanceID() : 0;
                if (m_Snapshot.materialInstanceIds[i] != materialId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasLightweightDiff()
        {
            if (!m_Snapshot.valid
                || m_Snapshot.visible != visible
                || m_Snapshot.renderingLayer != renderingLayer
                || m_Snapshot.renderPriority != renderPriority
                || m_Snapshot.castShadow != castShadow
                || m_Snapshot.receiveShadow != receiveShadow
                || m_Snapshot.affectIndirect != affectIndirectLighting
                || m_Snapshot.motionVector != motionVector)
            {
                return true;
            }

            int materialCount = materials != null ? materials.Length : 0;
            int queueCount = m_Snapshot.materialRenderQueues != null ? m_Snapshot.materialRenderQueues.Length : 0;
            if (materialCount != queueCount)
            {
                return true;
            }

            for (int i = 0; i < materialCount; ++i)
            {
                int renderQueue = materials[i] != null ? materials[i].renderQueue : 0;
                if (m_Snapshot.materialRenderQueues[i] != renderQueue)
                {
                    return true;
                }
            }

            return false;
        }

        private void CaptureSnapshot()
        {
            int materialCount = materials != null ? materials.Length : 0;
            int[] materialIds = new int[materialCount];
            int[] materialQueues = new int[materialCount];
            for (int i = 0; i < materialCount; ++i)
            {
                if (materials[i] != null)
                {
                    materialIds[i] = materials[i].GetInstanceID();
                    materialQueues[i] = materials[i].renderQueue;
                }
                else
                {
                    materialIds[i] = 0;
                    materialQueues[i] = 0;
                }
            }

            m_GeometryRevision = ComputeGeometryRevision(meshAsset);
            m_Snapshot = new MeshComponentSnapshot
            {
                valid = m_InstanceId.IsValid,
                visible = visible,
                movebility = movebility,
                meshAssetId = meshAsset != null ? meshAsset.GetInstanceID() : 0,
                subMeshCount = meshAsset != null ? meshAsset.subMeshCount : 0,
                geometryRevision = m_GeometryRevision,
                materialInstanceIds = materialIds,
                materialRenderQueues = materialQueues,
                renderingLayer = renderingLayer,
                renderPriority = renderPriority,
                castShadow = castShadow,
                receiveShadow = receiveShadow,
                affectIndirect = affectIndirectLighting,
                motionVector = motionVector
            };
        }

        private static uint ComputeGeometryRevision(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = mesh.subMeshCount;
                hash = (hash * 397) ^ mesh.vertexCount;
                return (uint)hash;
            }
        }

        public void SetCustomPrimitiveData(int offset, float data)
        {
        }

        public void SetCustomPrimitiveData(int offset, float2 data)
        {
            SetCustomPrimitiveData(offset, data.x);
            SetCustomPrimitiveData(offset + 1, data.y);
        }

        public void SetCustomPrimitiveData(int offset, float3 data)
        {
            SetCustomPrimitiveData(offset, data.x);
            SetCustomPrimitiveData(offset + 1, data.y);
            SetCustomPrimitiveData(offset + 2, data.z);
        }

        public void SetCustomPrimitiveData(int offset, float4 data)
        {
            SetCustomPrimitiveData(offset, data.x);
            SetCustomPrimitiveData(offset + 1, data.y);
            SetCustomPrimitiveData(offset + 2, data.z);
            SetCustomPrimitiveData(offset + 3, data.w);
        }
    }
}
