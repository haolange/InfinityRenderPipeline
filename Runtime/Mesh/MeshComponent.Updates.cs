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

        internal void CompleteRegistration(uint epoch)
        {
            if (!isActiveAndEnabled || epoch != m_RegistrationEpoch)
            {
                return;
            }
            MarkDirty();
        }

        public EMeshMobility Mobility
        {
            get => mobility;
            set
            {
                if (mobility == value)
                {
                    return;
                }
                mobility = value;
                MarkDirty();
            }
        }
        /// <summary>The mesh and LOD chain this component draws. Null draws nothing.</summary>
        public MeshAsset MeshAsset
        {
            get => asset;
            set
            {
                if (asset == value)
                {
                    return;
                }
                asset = value;
                RefreshGeometryReference();
                MarkDirty();
            }
        }
        public ReadOnlySpan<Material> Materials => materials;
        public ECastShadowMethod CastShadow
        {
            get => castShadow;
            set
            {
                if (castShadow == value)
                {
                    return;
                }
                castShadow = value;
                MarkDirty();
            }
        }
        public bool ReceiveShadow
        {
            get => receiveShadow;
            set
            {
                if (receiveShadow == value)
                {
                    return;
                }
                receiveShadow = value;
                MarkDirty();
            }
        }
        public bool AffectIndirectLighting
        {
            get => affectIndirectLighting;
            set
            {
                if (affectIndirectLighting == value)
                {
                    return;
                }
                affectIndirectLighting = value;
                MarkDirty();
            }
        }
        public bool Visible
        {
            get => visible;
            set
            {
                if (visible == value)
                {
                    return;
                }
                visible = value;
                MarkDirty();
            }
        }
        public ERenderingLayer RenderingLayer
        {
            get => renderingLayer;
            set
            {
                if (renderingLayer == value)
                {
                    return;
                }
                renderingLayer = value;
                MarkDirty();
            }
        }
        public int RenderPriority
        {
            get => renderPriority;
            set
            {
                if (renderPriority == value)
                {
                    return;
                }
                renderPriority = value;
                MarkDirty();
            }
        }
        public EMotionType MotionVector
        {
            get => motionVector;
            set
            {
                if (motionVector == value)
                {
                    return;
                }
                motionVector = value;
                MarkDirty();
            }
        }

        public void SetMaterials(Material[] value)
        {
            int count = value?.Length ?? 0;
            bool equal = materials != null && materials.Length == count;
            if (equal) for (int i = 0; i < count; ++i) if (materials[i] != value[i]) { equal = false; break; }
            if (equal)
            {
                return;
            }
            materials = count == 0 ? Array.Empty<Material>() : (Material[])value.Clone();
            MarkDirty();
        }

        /// <summary>Re-reads this component's <see cref="MeshAsset"/> for every component that shares it.</summary>
        public void NotifyGeometryChanged()
        {
            RefreshGeometryReference();
            if (m_Geometry != null)
            {
                InvalidateGeometry(m_Geometry);
            }
            MarkDirty();
        }

        /// <summary>Call after an in-place edit of <paramref name="mesh"/>: every asset that draws it is read again.</summary>
        public static void NotifyGeometryChanged(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }
            foreach (GeometryState state in s_Geometry.Values)
            {
                if (state.stale || state.asset == null)
                {
                    continue;
                }
                ReadOnlySpan<Mesh> meshes = state.asset.Meshes;
                for (int i = 0; i < meshes.Length; ++i)
                    if (ReferenceEquals(meshes[i], mesh))
                    {
                        InvalidateGeometry(state);
                        break;
                    }
            }
        }

        /// <summary>Call after <paramref name="asset"/> changed: the components that reference it are synced again.</summary>
        public static void NotifyGeometryChanged(MeshAsset asset)
        {
            if (!ReferenceEquals(asset, null) && s_Geometry.TryGetValue(asset, out GeometryState state))
            {
                InvalidateGeometry(state);
            }
        }

        public static void NotifyMaterialRoutingChanged(Material material) => MaterialRouteUtility.NotifyRoutingChanged(material);

        internal static void InvalidateMaterialRouting(Material material)
        {
            foreach (var component in s_Registered)
                for (int i = 0; i < component.Materials.Length; ++i)
                    if (component.materials[i] == material)
                    {
                        component.MarkDirty();
                        break;
                    }
        }

        /// <summary>Explicit notification for static poses, layers and entity settings. Dynamic poses are read every frame.</summary>
        public void NotifyEntityChanged()
        {
            MarkTransformDirty();
            MarkDirty();
        }

        /// <summary>
        /// A new world (pipeline creation) knows none of the registered components: every one syncs again from scratch.
        /// </summary>
        internal static void ResyncAll()
        {
            foreach (var component in s_Registered)
            {
                component.ResetSyncState();
                component.m_DirtyEnqueued = false;
                component.transformPending = false;
                component.MarkDirty();
            }
        }

        /// <summary>
        /// A shader import can renumber or remove passes: pass caches resolve their pass indices again, and every component
        /// validates its materials again in the next frame transaction.
        /// </summary>
        internal static void NotifyShadersChanged()
        {
            MeshPassShaderUtility.NotifyShadersChanged();
            foreach (var component in s_Registered)
            {
                component.MarkDirty();
            }
        }

        /// <summary>Refreshes the pose of every registered static component (asset proxy update or the first runtime frame).</summary>
        internal static void NotifyAllStaticPoses()
        {
            foreach (var component in s_Registered)
                if (component.mobility == EMeshMobility.Static)
                {
                    component.NotifyEntityChanged();
                }
        }

        internal void MarkTransformDirty()
        {
            if (transformPending)
            {
                return;
            }
            transformPending = true;
            RenderContext.EnqueueMeshTransform(this);
        }

        /// <summary>Writes the current static pose outside the structural transaction.</summary>
        internal void ApplyStaticPose(MeshWorld world)
        {
            transformPending = false;
            if (!isActiveAndEnabled || !IsPublishedIn(world, m_State) || world.IsDynamic(m_State.entity))
            {
                return;
            }
            float4x4 localToWorld = transform.localToWorldMatrix;
            m_BoundBox = Geometry.CaculateWorldBound(m_State.localBounds, localToWorld);
            world.WriteEntityPose(m_State.entity, localToWorld, m_BoundBox);
        }
    }
}
