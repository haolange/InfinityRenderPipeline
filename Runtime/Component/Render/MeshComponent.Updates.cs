using System;
using System.Collections.Generic;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Pipeline;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Component
{
    public partial class MeshComponent
    {
        private static readonly HashSet<MeshComponent> s_Registered = new HashSet<MeshComponent>();
        internal bool transformPending;
        internal int dynamicIndex = -1;
        internal int staticIndex = -1;
        internal Bounds localBounds;
        internal Transform cachedTransform;
        private FAABB m_PendingBounds;

        internal void CompleteRegistration(RenderContext context, uint epoch)
        {
            if (!isActiveAndEnabled || epoch != m_RegistrationEpoch) return;
            AddWorldMesh(context, movebility);
            MarkDirty();
        }

        internal static void CompleteUnregistration(RenderContext context, MeshComponent component, MeshInstanceId instance, EStateType mobility)
        {
            RemoveInstanceById(context, instance);
            RemoveWorldMesh(context, mobility, component);
        }

        public EStateType Mobility { get => movebility; set { if (movebility == value) return; movebility = value; MarkDirty(); } }
        public Mesh MeshAsset { get => meshAsset; set { if (meshAsset == value) return; meshAsset = value; RefreshGeometryReference(); MarkTransformDirty(); MarkDirty(); } }
        public ReadOnlySpan<Material> Materials => materials;
        public ECastShadowMethod CastShadow { get => castShadow; set { if (castShadow == value) return; castShadow = value; MarkDirty(); } }
        public bool ReceiveShadow { get => receiveShadow; set { if (receiveShadow == value) return; receiveShadow = value; MarkDirty(); } }
        public bool AffectIndirectLighting { get => affectIndirectLighting; set { if (affectIndirectLighting == value) return; affectIndirectLighting = value; MarkDirty(); } }
        public bool Visible { get => visible; set { if (visible == value) return; visible = value; MarkDirty(); } }
        public ERenderingLayer RenderingLayer { get => renderingLayer; set { if (renderingLayer == value) return; renderingLayer = value; MarkDirty(); } }
        public int RenderPriority { get => renderPriority; set { if (renderPriority == value) return; renderPriority = value; MarkDirty(); } }
        public EMotionType MotionVector { get => motionVector; set { if (motionVector == value) return; motionVector = value; MarkDirty(); } }

        public void SetMaterials(Material[] value)
        {
            int count = value?.Length ?? 0;
            bool equal = materials != null && materials.Length == count;
            if (equal) for (int i = 0; i < count; ++i) if (materials[i] != value[i]) { equal = false; break; }
            if (equal) return;
            materials = count == 0 ? Array.Empty<Material>() : (Material[])value.Clone();
            MarkDirty();
        }

        public void NotifyGeometryChanged()
        {
            RefreshGeometryReference();
            RefreshSharedGeometry(meshAsset);
            if (m_Geometry != null) { m_GeometryRevision = m_Geometry.revision; localBounds = m_Geometry.bounds; }
            MarkTransformDirty(); MarkDirty();
        }

        public static void NotifyGeometryChanged(Mesh mesh) => RefreshSharedGeometry(mesh);

        public static void NotifyMaterialRoutingChanged(Material material) => MaterialRouteUtility.NotifyRoutingChanged(material);

        internal static void InvalidateMaterialRouting(Material material)
        {
            foreach (var component in s_Registered)
                for (int i = 0; i < component.Materials.Length; ++i)
                    if (component.materials[i] == material) { component.MarkDirty(); break; }
        }

        // Explicit notification for static transforms, layers and instance settings.
        public void NotifyInstanceChanged()
        {
            MarkTransformDirty();
            MarkDirty();
        }

        internal void MarkTransformDirty()
        {
            if (transformPending) return;
            transformPending = true;
            RenderContext.EnqueueMeshTransform(this);
        }

        internal void AcceptTransform()
        {
            transformPending = false;
            if (!isActiveAndEnabled || !m_InstanceId.IsValid) return;
            m_BoundBox = m_PendingBounds;
            m_BoundSphere = new FSphere(Geometry.CaculateBoundRadius(m_BoundBox), m_BoundBox.center);
        }

        internal void ApplyTransform(in MeshSceneUpdate update, in float4x4 matrix, in FAABB bounds)
        {
            if (!m_InstanceId.IsValid || !m_TransformId.IsValid) return;
            m_PendingBounds = bounds;
            update.SetTransform(m_TransformId, matrix);
            update.SetBounds(m_InstanceId, bounds);
        }
    }
}
