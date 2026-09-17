using NUnit.Framework;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class GpuSceneDirtyPageTests
    {
        [Test]
        public void TwoFarDirtyInstances_UploadAtMostTwoPages()
        {
            using (var scene = new MeshScene(256))
            {
                var pool = new ResourcePool();
                var gpu = new GpuScene(pool, scene);
                try
                {
                    MeshInstanceId first;
                    MeshInstanceId last;
                    using (MeshSceneUpdate update = scene.BeginUpdate())
                    {
                        first = Create(update);
                        for (int i = 1; i < 128; ++i)
                        {
                            Create(update);
                        }

                        last = Create(update);
                        update.Commit();
                    }

                    Assert.Greater((int)last.Index - (int)first.Index, 64);
                    gpu.Update();
                    MeshPipelineDiagnostics.Reset();

                    using (MeshSceneUpdate update = scene.BeginUpdate())
                    {
                        update.SetInstanceFlags(first, EMeshInstanceFlags.Visible | EMeshInstanceFlags.CastShadow);
                        update.SetInstanceFlags(last, EMeshInstanceFlags.Visible | EMeshInstanceFlags.CastShadow);
                        update.Commit();
                    }

                    gpu.Update();
                    Assert.LessOrEqual(MeshPipelineDiagnostics.GpuSceneUploadedSlots, 128);
                    Assert.Less(MeshPipelineDiagnostics.GpuSceneUploadedSlots, (int)last.Index + 1);
                    Assert.Greater(MeshPipelineDiagnostics.GpuSceneUploadedSlots, 0);
                    Assert.Greater(MeshPipelineDiagnostics.GpuSceneUploadedBytes, 0);

                    MeshPipelineDiagnostics.Reset();
                    gpu.Update();
                    Assert.AreEqual(0, MeshPipelineDiagnostics.GpuSceneUploadedSlots);
                    Assert.AreEqual(0, MeshPipelineDiagnostics.GpuSceneUploadedBytes);
                }
                finally
                {
                    gpu.Dispose();
                    pool.Dispose();
                }
            }
        }

        [Test]
        public void Rollback_RestoresDirtyPages()
        {
            using (var scene = new MeshScene(16))
            {
                MeshSceneUpdate seed = scene.BeginUpdate();
                MeshInstanceId instance = Create(seed);
                seed.Commit();

                scene.ClearBoundsDirtyRange();
                scene.ClearTransformDirtyRange();
                Assert.IsFalse(scene.HasBoundsDirtyRange);
                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    update.SetInstanceFlags(instance, EMeshInstanceFlags.None);
                    Assert.IsTrue(scene.HasBoundsDirtyRange);
                    update.Rollback();
                }

                Assert.IsFalse(scene.HasBoundsDirtyRange);
            }
        }

        static MeshInstanceId Create(MeshSceneUpdate update)
        {
            TransformId transform = update.CreateTransform(float4x4.identity);
            return update.CreateInstance(
                transform,
                new FBound(float3.zero, new float3(1, 1, 1)),
                layerMask: ~0,
                renderingLayerMask: 1,
                flags: EMeshInstanceFlags.Visible,
                motionType: EMotionType.Object,
                castShadow: ECastShadowMethod.Off);
        }
    }
}
