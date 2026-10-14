using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Shadowmask-mode Mixed lights draw dynamic casters only: MeshScene views exclude ShadowmaskStatic instances.
    public class ShadowmaskCasterFilterTests
    {
        [Test]
        [Category("Unverified-0.5")]
        public void ExcludedFlags_CullStaticLightmappedInstancesOnly()
        {
            var scene = new MeshScene(16);
            try
            {
                MeshInstanceId dynamicInstance, staticInstance;
                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    TransformId a = update.CreateTransform(float4x4.identity);
                    TransformId b = update.CreateTransform(float4x4.identity);
                    var bounds = new FBound(float3.zero, new float3(1));
                    dynamicInstance = update.CreateInstance(a, bounds, ~0, 1u,
                        EMeshInstanceFlags.Visible | EMeshInstanceFlags.CastShadow, EMotionType.Object, ECastShadowMethod.Dynamic);
                    staticInstance = update.CreateInstance(b, bounds, ~0, 1u,
                        EMeshInstanceFlags.Visible | EMeshInstanceFlags.CastShadow | EMeshInstanceFlags.ShadowmaskStatic, EMotionType.Object, ECastShadowMethod.Dynamic);
                    update.Commit();
                }

                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Ortho(-10, 10, -10, 10, -10, 10));
                MeshViewCullingResult all = MeshVisibilityUtility.CullInstances(scene, planes, true);
                MeshViewCullingResult dynamicOnly = MeshVisibilityUtility.CullInstances(scene, planes, true,
                    excludeInstanceFlags: (uint)EMeshInstanceFlags.ShadowmaskStatic);
                try
                {
                    Assert.AreEqual(1, all.instanceVisibility[(int)dynamicInstance.Index]);
                    Assert.AreEqual(1, all.instanceVisibility[(int)staticInstance.Index]);
                    Assert.AreEqual(1, dynamicOnly.instanceVisibility[(int)dynamicInstance.Index]);
                    Assert.AreEqual(0, dynamicOnly.instanceVisibility[(int)staticInstance.Index]);
                }
                finally
                {
                    all.Release();
                    dynamicOnly.Release();
                }
            }
            finally
            {
                scene.Dispose();
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void ExcludedFlags_SplitVisibilitySignatures()
        {
            var view = new MeshView { viewKey = 7, kind = EMeshViewKind.LocalShadow, layerMask = ~0, renderingLayerMask = 1 };
            var filtered = view;
            filtered.excludeInstanceFlags = (uint)EMeshInstanceFlags.ShadowmaskStatic;
            Assert.AreNotEqual(new MeshGpuVisibilityKey(view), new MeshGpuVisibilityKey(filtered));
        }
    }
}
