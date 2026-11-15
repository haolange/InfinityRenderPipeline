using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshCullingContextTests
    {
        [Test]
        public void Create_RejectsMorePlanesThanSlots_AndAnInvalidLODProjection()
        {
            var seven = new Plane[MeshCullingContext.MaxPlanes + 1];
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshCullingContext.Create(seven, ~0, ulong.MaxValue, 0xFF, false, MeshTestUtility.Lod));
            var flat = MeshTestUtility.Lod;
            flat.scale = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), ~0, ulong.MaxValue, 0xFF, false, flat));
            var lost = MeshTestUtility.Lod;
            lost.origin = new float3(float.NaN, 0, 0);
            Assert.Throws<ArgumentException>(() => MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), ~0, ulong.MaxValue, 0xFF, false, lost));
        }

        [Test]
        public void Equality_SeparatesEveryCullingInput()
        {
            Plane[] planes = MeshTestUtility.BoxPlanes();
            MeshLODProjection lod = MeshTestUtility.Lod;
            MeshCullingContext Make(Plane[] p = null, int layer = ~0, ulong scene = ulong.MaxValue, uint rendering = 0xFF, bool filter = false, MeshLODProjection? projection = null)
                => MeshCullingContext.Create(p ?? planes, layer, scene, rendering, filter, projection ?? lod);
            MeshCullingContext baseline = Make();
            Assert.IsTrue(baseline.Equals(Make()));
            Assert.AreEqual(baseline.GetHashCode(), Make().GetHashCode());

            var moved = (Plane[])planes.Clone();
            moved[5] = new Plane(Vector3.back, 11f);
            var fewer = new Plane[5];
            Array.Copy(planes, fewer, 5);
            MeshLODProjection origin = lod, scale = lod, bias = lod, orthographic = lod;
            origin.origin = new float3(0, 1, 0);
            scale.scale = 2;
            bias.bias = 2;
            orthographic.orthographic = true;
            var variants = new[]
            {
                Make(moved), Make(fewer), Make(layer: 1), Make(scene: 1), Make(rendering: 1), Make(filter: true),
                Make(projection: origin), Make(projection: scale), Make(projection: bias), Make(projection: orthographic)
            };
            for (int i = 0; i < variants.Length; ++i)
                Assert.IsFalse(baseline.Equals(variants[i]), $"variant {i}");
        }

        [Test]
        public void CpuGather_AppliesFlagsMasksAndFrustum()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                var ids = new MeshEntityId[7];
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    ids[0] = MeshTestUtility.CreateEntity(update, new float3(3, 0, 0));
                    ids[1] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), EMeshEntityFlags.None);
                    ids[2] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), layerMask: 2);
                    ids[3] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), sceneCullingMask: 1ul << 63);
                    ids[4] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), renderingLayerMask: 2);
                    ids[5] = MeshTestUtility.CreateEntity(update, new float3(40, 0, 0));
                    ids[6] = MeshTestUtility.CreateEntity(update, new float3(10.4f, 0, 0));
                    update.Commit();
                }
                uint2[] candidates = MeshTestUtility.LevelCandidates(7, 1);

                MeshCullingContext camera = MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), 1, 1, (uint)ERenderingLayer.Everything, false, MeshTestUtility.Lod);
                // Visible flag, layer and scene masks apply; a camera context ignores rendering layers; bounds touching a plane stay.
                CollectionAssert.AreEqual(new uint[] { 1, 0, 0, 0, 1, 0, 1 },
                    MeshTestUtility.DrawnEntities(MeshTestUtility.GatherOnCpu(world, candidates, 1, camera, false), 7));

                MeshCullingContext light = MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), 1, ulong.MaxValue, 1u, true, MeshTestUtility.Lod);
                CollectionAssert.AreEqual(new uint[] { 1, 0, 0, 1, 0, 0, 1 },
                    MeshTestUtility.DrawnEntities(MeshTestUtility.GatherOnCpu(world, candidates, 1, light, false), 7),
                    "A light context applies the light's rendering layers.");
            }
        }

        [Test]
        public void GpuBuild_AgreesWithCpuGather()
        {
            MeshTestUtility.RequireComputeReadback();
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshTestUtility.RequireGPUIndirect(world);
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    MeshTestUtility.CreateEntity(update, new float3(3, 0, 0));
                    MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.None);
                    MeshTestUtility.CreateEntity(update, float3.zero, layerMask: 2);
                    MeshTestUtility.CreateEntity(update, float3.zero, sceneCullingMask: 1ul << 63);
                    MeshTestUtility.CreateEntity(update, float3.zero, sceneCullingMask: 1ul);
                    MeshTestUtility.CreateEntity(update, float3.zero, renderingLayerMask: 2);
                    MeshTestUtility.CreateEntity(update, new float3(40, 0, 0));
                    MeshTestUtility.CreateEntity(update, new float3(10.4f, 0, -10.4f));
                    update.Commit();
                }
                world.UpdateResidency();
                int count = world.entityHighWater;
                uint2[] candidates = MeshTestUtility.LevelCandidates(count, 1);
                ComputeShader shader = Object.Instantiate(fixture.shader);
                var run = new MeshProducerRun(shader, MeshEntityColumns.Of(world), candidates, 1);
                try
                {
                    var orthographic = new MeshLODProjection { origin = new float3(4, 0, 0), scale = 0.5f, bias = 2f, orthographic = true };
                    foreach (ulong scene in new[] { ulong.MaxValue, 1ul, 1ul << 63, 0ul })
                    {
                        var contexts = new[]
                        {
                            MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), ~0, scene, (uint)ERenderingLayer.Everything, false, MeshTestUtility.Lod),
                            MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), ~0, scene, 1u, true, MeshTestUtility.Lod),
                            MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), 1, scene, 2u, true, orthographic)
                        };
                        for (int c = 0; c < contexts.Length; ++c)
                        {
                            List<MeshDrawnInstance> expected = MeshTestUtility.GatherOnCpu(world, candidates, 1, contexts[c], false);
                            List<MeshDrawnInstance> actual = run.Build("BuildDrawList", contexts[c]);
                            CollectionAssert.AreEqual(expected, actual, $"context {c}, scene mask {scene:X}");
                        }
                    }
                }
                finally
                {
                    run.Dispose();
                    Object.DestroyImmediate(shader);
                }
            }
        }
    }
}
