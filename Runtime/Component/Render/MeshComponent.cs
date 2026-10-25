using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
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
    /// MeshScene transaction → world Static/Dynamic list membership → PassBin invalidation.
    /// MarkDirty uses a shared dirty queue (not the Dynamic world list), so Dynamic→Static still syncs.
    /// </summary>
    [ExecuteAlways]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    [AddComponentMenu("InfinityRenderer/Mesh Component")]
    public partial class MeshComponent : EntityComponent
    {
        private struct MeshComponentSnapshot
        {
            public bool valid;
            public bool visible;
            public ulong sceneCullingMask;
            public int unityLayer;
            public EStateType movebility;
            public ulong meshAssetId;
            public int subMeshCount;
            public uint geometryRevision;
            public ulong[] materialInstanceIds;
            public int[] materialRenderQueues;
            public int[] materialSurfaceRoutes;
            public int[] materialTranslucentStages;
            public uint[] materialVersions;
            public ERenderingLayer renderingLayer;
            public int renderPriority;
            public ECastShadowMethod castShadow;
            public bool receiveShadow;
            public bool affectIndirect;
            public EMotionType motionVector;
        }

        [Header("State")]
        [SerializeField] private EStateType movebility = EStateType.Static;

        [Header("Mesh")]
        [SerializeField] private Mesh meshAsset;

        [Header("Material")]
        [SerializeField] private Material[] materials;
#if UNITY_EDITOR
        private Material[] m_LastMaterials;
#endif

        [Header("Lighting")]
        [SerializeField] private Renderer m_BakedLightingSource;
        private FMeshBakedLighting m_LastBakedLighting;
        // Probe sample interpolated by RenderContext's per-frame batch, valid for m_ProbePosition at m_ProbeVersion.
        private bool m_HasProbeSample;
        private Vector3 m_ProbePosition;
        private int m_ProbeVersion;
        private FMeshBakedLighting m_CurrentBakedLighting;
        private UnityEngine.Rendering.SphericalHarmonicsL2 m_ProbeSH;
        private Vector4 m_ProbeOcclusion;
        private bool m_UsesProbes;
        private Transform m_ProbeAnchor;
        internal Vector3 probeWorldPosition;

        private Renderer m_CachedRenderer;
        private Renderer BakedLightingSource => m_BakedLightingSource != null ? m_BakedLightingSource : m_CachedRenderer;

        private FMeshBakedLighting ReadBakedLighting() => m_CurrentBakedLighting;

        internal void RefreshLightingConfiguration(bool probesAvailable)
        {
            Renderer source = BakedLightingSource;
            var layout = MeshBakedLighting.CaptureLayout(source);
            m_UsesProbes = layout.TextureSet == 0 && probesAvailable && (source == null || source.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off);
            m_ProbeAnchor = source != null ? source.probeAnchor : null;
            if (m_UsesProbes && m_HasProbeSample && m_ProbeVersion == MeshBakedLighting.ProbeVersion)
                layout = MeshBakedLighting.WithProbeSample(layout, m_ProbeSH, m_ProbeOcclusion);
            if (!m_CurrentBakedLighting.Equals(layout)) MarkTransformDirty();
            m_CurrentBakedLighting = layout;
        }

        // Batch step 1: the probe position this mesh needs a sample for, or false when its sample is current or it
        // uses no probes.
        internal bool NeedsProbeSample(out Vector3 probePosition)
        {
            Renderer source = BakedLightingSource;
            probePosition = m_ProbeAnchor != null ? m_ProbeAnchor.position : source != null ? source.bounds.center : probeWorldPosition;
            if (!m_UsesProbes) return false;
            return !m_HasProbeSample || m_ProbeVersion != MeshBakedLighting.ProbeVersion || probePosition != m_ProbePosition;
        }

        // Batch step 2: the interpolated sample.
        internal void SetProbeSample(Vector3 probePosition, UnityEngine.Rendering.SphericalHarmonicsL2 sh, Vector4 occlusion)
        {
            m_HasProbeSample = true;
            m_ProbePosition = probePosition;
            m_ProbeVersion = MeshBakedLighting.ProbeVersion;
            m_ProbeSH = sh; m_ProbeOcclusion = occlusion;
            m_CurrentBakedLighting = MeshBakedLighting.WithProbeSample(m_CurrentBakedLighting, sh, occlusion);
            MarkTransformDirty();
        }
        [SerializeField] private ECastShadowMethod castShadow = ECastShadowMethod.Off;
        [SerializeField] private bool receiveShadow = true;
        [SerializeField] private bool affectIndirectLighting = true;

        [Header("Rendering")]
        [SerializeField] private bool visible = true;
        [SerializeField] private ERenderingLayer renderingLayer = ERenderingLayer.LightLayerDefault;
        [SerializeField] private int renderPriority = 0;
        [SerializeField] private EMotionType motionVector = EMotionType.Object;


        private bool m_PreviousForceRenderingOff;
        private MeshInstanceId m_InstanceId;
        private TransformId m_TransformId;
        private MeshDrawId[] m_DrawIds;
        private FAABB m_BoundBox;
        private FSphere m_BoundSphere;
        private MeshComponentSnapshot m_Snapshot;
        private bool m_DirtyEnqueued;
        private uint m_RegistrationEpoch;
        private uint m_GeometryRevision;
        private float4x4 m_LocalToWorldMatrix => transform.localToWorldMatrix;
        private ulong SceneCullingMask
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
            m_CachedRenderer = GetComponent<Renderer>();
            s_Registered.Add(this);
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                // MeshComponent owns MeshDraw; MeshRenderer owns RendererList — exclusive.
                m_PreviousForceRenderingOff = meshRenderer.forceRenderingOff;
                meshRenderer.forceRenderingOff = true;
            }

            UpdateBounds();
            FGraphics.RegisterMesh(this, epoch);
        }

        protected override void OnTransformChange()
        {
            UpdateBounds();
            MarkTransformDirty();
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

        }

        protected override void UnRegister()
        {
            ++m_RegistrationEpoch;
            s_Registered.Remove(this);
            ReleaseGeometryReference();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.forceRenderingOff = m_PreviousForceRenderingOff;
            }

            MeshInstanceId instanceId = m_InstanceId;
            EStateType mobility = dynamicIndex >= 0 ? EStateType.Dynamic : EStateType.Static;
            MeshComponent self = this;

            // Clear local handles immediately so UnRegister is idempotent across OnDisable/OnDestroy.
            m_InstanceId = MeshInstanceId.Invalid;
            m_TransformId = TransformId.Invalid;
            m_DrawIds = null;
            m_Snapshot = default;
            m_DirtyEnqueued = false;

            FGraphics.UnregisterMesh(self, instanceId, mobility);
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
            RefreshGeometryReference();

            if (!isActiveAndEnabled)
            {
                if (m_InstanceId.IsValid)
                {
                    UnRegisterFromMeshScene(renderContext);
                }

                ReleaseGeometryReference();
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

            FMeshBakedLighting baked = ReadBakedLighting();
            if (!baked.Equals(m_LastBakedLighting))
            {
                using (var update = renderContext.GetMeshScene().BeginUpdate())
                { update.SetInstanceBakedLighting(m_InstanceId, baked); update.Commit(); }
                m_LastBakedLighting = baked;
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
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled || !gameObject.scene.IsValid()) return;
            // Inspector edits are an explicit resource notification.
            NotifyGeometryChanged();
            MarkTransformDirty();
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
            if (dynamicIndex >= 0)
            {
                return EStateType.Dynamic;
            }

            if (staticIndex >= 0)
            {
                return EStateType.Static;
            }

            return null;
        }

        public void UpdateBounds()
        {
            if (!meshAsset) { return; }

            cachedTransform = transform;
            probeWorldPosition = cachedTransform.position;
            RefreshGeometryReference();
            localBounds = m_Geometry.bounds;
            m_BoundBox = Geometry.CaculateWorldBound(localBounds, m_LocalToWorldMatrix);
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
                    EGeometrySourceKind.IndexedMesh,
                    sceneCullingMask: SceneCullingMask);

                m_LastBakedLighting = ReadBakedLighting();
                update.SetInstanceBakedLighting(m_InstanceId, m_LastBakedLighting);
                int subMeshCount = m_Geometry.subMeshes;
                m_DrawIds = new MeshDrawId[subMeshCount];
                ulong meshUnityId = m_Geometry.identity;


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

                    EPassEligibility eligibility = BuildPassEligibility(material);
                    MaterialRouteUtility.ReadMeshRoute(material, out _, out _, out int renderQueue, out _);
                    int priority = renderPriority + renderQueue;
                    uint staticFlags = movebility == EStateType.Static ? 1u : 0u;
                    m_DrawIds[i] = update.CreateDraw(
                        m_InstanceId,
                        meshUnityId,
                        i,
                        UnityEntityId.ToUInt64(material),
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
            uint renderingLayerMask = (uint)renderingLayer;

            MeshScene scene = renderContext.GetMeshScene();
            using (MeshSceneUpdate update = scene.BeginUpdate())
            {
                update.SetInstanceFlags(m_InstanceId, flags);
                update.SetInstanceSceneCullingMask(m_InstanceId, SceneCullingMask);
                update.SetInstanceLayerMask(m_InstanceId, 1 << gameObject.layer);
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
                        EPassEligibility eligibility = BuildPassEligibility(material);
                        if (material != null)
                        {
                            MaterialRouteUtility.ReadMeshRoute(material, out _, out _, out renderQueue, out _);
                            // Updates draw.renderQueue and MaterialData revision (same id, new queue).
                            update.SetMaterial(drawId, UnityEntityId.ToUInt64(material), renderQueue);
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
            Renderer bakedSource = BakedLightingSource;
            if (movebility == EStateType.Static && bakedSource != null && bakedSource.lightmapIndex >= 0 && bakedSource.lightmapIndex < 65534)
                flags |= EMeshInstanceFlags.ShadowmaskStatic;
            return flags;
        }

        private EPassEligibility BuildPassEligibility(Material material)
        {
            int surfaceRoute = 0;
            int translucentStage = 0;
            ReadMaterialRoute(material, out surfaceRoute, out translucentStage);

            EPassEligibility eligibility = EPassEligibility.None;
            if (translucentStage > 0)
            {
                eligibility = EPassEligibility.Transparent;
            }
            else if (surfaceRoute == 1)
            {
                eligibility = EPassEligibility.Depth | EPassEligibility.GBuffer | EPassEligibility.Forward;
            }
            else
            {
                eligibility = EPassEligibility.Depth | EPassEligibility.GBuffer;
            }

            if (translucentStage == 0 && motionVector == EMotionType.Object)
            {
                eligibility |= EPassEligibility.Motion;
            }

            if (translucentStage == 0 && castShadow != ECastShadowMethod.Off)
            {
                eligibility |= EPassEligibility.Shadow;
            }

            return eligibility;
        }

        private static void ReadMaterialRoute(Material material, out int surfaceRoute, out int translucentStage)
        {
            surfaceRoute = 0;
            translucentStage = 0;
            if (material == null)
            {
                return;
            }

            MaterialRouteUtility.ReadMeshRoute(material, out surfaceRoute, out translucentStage, out _, out _);
        }

        private bool HasStructuralDiff()
        {
            ulong meshId = m_Geometry != null ? m_Geometry.identity : 0;
            int subMeshCount = m_Geometry != null ? m_Geometry.subMeshes : 0;
            uint geometryRevision = m_GeometryRevision;
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
                ulong materialId = UnityEntityId.ToUInt64(materials[i]);
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
                || m_Snapshot.unityLayer != gameObject.layer
                || m_Snapshot.sceneCullingMask != SceneCullingMask
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
            int routeCount = m_Snapshot.materialSurfaceRoutes != null ? m_Snapshot.materialSurfaceRoutes.Length : 0;
            int stageCount = m_Snapshot.materialTranslucentStages != null ? m_Snapshot.materialTranslucentStages.Length : 0;
            if (materialCount != queueCount || materialCount != routeCount || materialCount != stageCount)
            {
                return true;
            }

            for (int i = 0; i < materialCount; ++i)
            {
                Material material = materials[i];
                int renderQueue = 0; uint version = 0;
                if (material != null) MaterialRouteUtility.ReadMeshRoute(material, out _, out _, out renderQueue, out version);
                ReadMaterialRoute(material, out int surfaceRoute, out int translucentStage);
                if (m_Snapshot.materialVersions[i] != version || m_Snapshot.materialRenderQueues[i] != renderQueue
                    || m_Snapshot.materialSurfaceRoutes[i] != surfaceRoute
                    || m_Snapshot.materialTranslucentStages[i] != translucentStage)
                {
                    return true;
                }
            }

            return false;
        }

        private void CaptureSnapshot()
        {
            int materialCount = materials != null ? materials.Length : 0;
            ulong[] materialIds = m_Snapshot.materialInstanceIds;
            if (materialIds == null || materialIds.Length != materialCount) materialIds = new ulong[materialCount];
            int[] materialQueues = m_Snapshot.materialRenderQueues;
            if (materialQueues == null || materialQueues.Length != materialCount) materialQueues = new int[materialCount];
            int[] materialRoutes = m_Snapshot.materialSurfaceRoutes;
            if (materialRoutes == null || materialRoutes.Length != materialCount) materialRoutes = new int[materialCount];
            int[] materialStages = m_Snapshot.materialTranslucentStages;
            if (materialStages == null || materialStages.Length != materialCount) materialStages = new int[materialCount];
            uint[] versions = m_Snapshot.materialVersions;
            if (versions == null || versions.Length != materialCount) versions = new uint[materialCount];
            for (int i = 0; i < materialCount; ++i)
            {
                if (materials[i] != null)
                {
                    materialIds[i] = UnityEntityId.ToUInt64(materials[i]);
                    MaterialRouteUtility.ReadMeshRoute(materials[i], out _, out _, out materialQueues[i], out versions[i]);
                    ReadMaterialRoute(materials[i], out materialRoutes[i], out materialStages[i]);
                }
                else
                {
                    materialIds[i] = 0;
                    materialQueues[i] = 0;
                    materialRoutes[i] = 0;
                    materialStages[i] = 0;
                }
            }


            m_Snapshot = new MeshComponentSnapshot
            {
                valid = m_InstanceId.IsValid,
                visible = visible,
                sceneCullingMask = SceneCullingMask,
                unityLayer = gameObject.layer,
                movebility = movebility,
                meshAssetId = m_Geometry != null ? m_Geometry.identity : 0,
                subMeshCount = m_Geometry != null ? m_Geometry.subMeshes : 0,
                geometryRevision = m_GeometryRevision,
                materialInstanceIds = materialIds,
                materialRenderQueues = materialQueues,
                materialSurfaceRoutes = materialRoutes,
                materialTranslucentStages = materialStages,
                materialVersions = versions,
                renderingLayer = renderingLayer,
                renderPriority = renderPriority,
                castShadow = castShadow,
                receiveShadow = receiveShadow,
                affectIndirect = affectIndirectLighting,
                motionVector = motionVector
            };
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
