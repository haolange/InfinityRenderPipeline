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
        public void CPUVisibility_AppliesFlagsMasksAndFrustum_AndTheListFiltersIt()
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
                uint[] cameraVisibility = MeshTestUtility.CullOnCPU(world, camera);
                CollectionAssert.AreEqual(new uint[] { 1, 0, 0, 0, 1, 0, 1 }, MeshProducerRun.Visible(cameraVisibility));
                CollectionAssert.AreEqual(new uint[] { 1, 0, 0, 0, 1, 0, 1 },
                    MeshTestUtility.DrawnEntities(MeshTestUtility.GatherOnCPU(world, candidates, 1, cameraVisibility, camera.lodOrigin, false), 7),
                    "The list draws what its view sees.");

                MeshCullingContext light = MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), 1, ulong.MaxValue, 1u, true, MeshTestUtility.Lod);
                uint[] lightVisibility = MeshTestUtility.CullOnCPU(world, light);
                CollectionAssert.AreEqual(new uint[] { 1, 0, 0, 1, 0, 0, 1 }, MeshProducerRun.Visible(lightVisibility),
                    "A light context applies the light's rendering layers.");
                CollectionAssert.AreEqual(new uint[] { 1, 0, 0, 1, 0, 0, 1 },
                    MeshTestUtility.DrawnEntities(MeshTestUtility.GatherOnCPU(world, candidates, 1, lightVisibility, light.lodOrigin, false), 7));
            }
        }

        [Test]
        public void GPUView_AgreesWithCPUView_AndTheirListsAgree()
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
                var run = new MeshProducerRun(shader, MeshEntityColumns.Of(world), candidates, 1, count);
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
                            // One level per chain, so every visible word is stable and the two sides agree bit for bit.
                            uint[] cpuVisibility = MeshTestUtility.CullOnCPU(world, contexts[c]);
                            uint[] gpuVisibility = run.Cull(MeshProducerRun.CullView, contexts[c]);
                            CollectionAssert.AreEqual(cpuVisibility, gpuVisibility, $"view visibility, context {c}, scene mask {scene:X}");
                            List<MeshDrawnInstance> expected = MeshTestUtility.GatherOnCPU(world, candidates, 1, cpuVisibility, contexts[c].lodOrigin, false);
                            List<MeshDrawnInstance> actual = run.BuildList();
                            // Every entity has one level-0 element, so each list draws exactly its view's visible entities.
                            CollectionAssert.AreEqual(MeshProducerRun.Visible(cpuVisibility), MeshTestUtility.DrawnEntities(expected, count),
                                $"CPU list, context {c}, scene mask {scene:X}");
                            CollectionAssert.AreEqual(expected, actual, $"list, context {c}, scene mask {scene:X}");
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
