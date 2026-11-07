using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshDrawListTests
    {
        [Test]
        public void SortKeySegments_Saturate_WithoutTruncation()
        {
            Assert.AreEqual(0xFFFFul, SortKeyPacking.SaturateUnsigned(0x10001, 16), "Truncation would wrap 0x10001 to 1.");
            Assert.AreEqual(0xFFFFul, SortKeyPacking.SaturateUnsigned(int.MaxValue, 16));
            Assert.AreEqual(0ul, SortKeyPacking.SaturateUnsigned(-5, 16));
            Assert.AreEqual(0xFFFFFFul, SortKeyPacking.SaturateUnsigned(1 << 24, 24));

            int[] priorities = { int.MinValue, -32769, -32768, -1, 0, 1, 32767, 32768, int.MaxValue };
            for (int i = 1; i < priorities.Length; ++i)
                Assert.LessOrEqual(SortKeyPacking.SaturateSigned(priorities[i - 1], 16), SortKeyPacking.SaturateSigned(priorities[i], 16),
                    $"Signed priority order {priorities[i - 1]} <= {priorities[i]}");
            Assert.Less(SortKeyPacking.SaturateSigned(-1, 16), SortKeyPacking.SaturateSigned(0, 16));
            Assert.AreEqual(0ul, SortKeyPacking.SaturateSigned(int.MinValue, 16));
            Assert.AreEqual(0xFFFFul, SortKeyPacking.SaturateSigned(int.MaxValue, 16));

            Assert.AreEqual(0ul, SortKeyPacking.QuantizeDepth(float.NaN, 16));
            Assert.AreEqual(0ul, SortKeyPacking.QuantizeDepth(-3f, 16));
            Assert.Less(SortKeyPacking.QuantizeDepth(1000f, 16), SortKeyPacking.QuantizeDepth(2000f, 16), "Decimeters keep kilometers distinct.");
            Assert.AreEqual(0xFFFFul, SortKeyPacking.QuantizeDepth(1e9f, 16));
            Assert.AreEqual(0xFFFFul, SortKeyPacking.QuantizeDepth(float.PositiveInfinity, 16));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Comparer_IsATotalOrder(bool backToFront)
        {
            var random = new System.Random(5);
            var items = new MeshDrawItem[64];
            for (int i = 0; i < items.Length; ++i)
            {
                // Translucent (back-to-front) passes never fade; other modes mix stable and fading items.
                bool fading = !backToFront && random.Next(3) == 0;
                float fade = fading ? (random.Next(2) == 0 ? 1 : -1) * (0.05f + 0.9f * (float)random.NextDouble()) : 0;
                items[i] = new MeshDrawItem { key = (ulong)random.Next(3), command = random.Next(3), entity = i, depth = random.Next(4), fade = fade };
            }
            var comparer = new MeshDrawItemComparer { backToFront = backToFront };
            foreach (MeshDrawItem a in items)
                foreach (MeshDrawItem b in items)
                    Assert.AreEqual(Math.Sign(comparer.Compare(a, b)), -Math.Sign(comparer.Compare(b, a)));

            MeshDrawItem[] first = items.OrderBy(_ => random.Next()).ToArray();
            MeshDrawItem[] second = items.OrderBy(_ => random.Next()).ToArray();
            Array.Sort(first, comparer);
            Array.Sort(second, comparer);
            CollectionAssert.AreEqual(first.Select(item => item.entity), second.Select(item => item.entity), "A total order has one sorted sequence.");
            for (int i = 1; i < first.Length; ++i)
            {
                MeshDrawItem a = first[i - 1], b = first[i];
                if (!backToFront)
                {
                    Assert.IsTrue(!a.fading || b.fading, "Stable items precede fading ones, so a list changes the dither keyword once.");
                    if (a.fading != b.fading) continue;
                }
                Assert.LessOrEqual(a.key, b.key);
                if (a.key != b.key) continue;
                if (backToFront) Assert.GreaterOrEqual(a.depth, b.depth, "Back-to-front: farther first within a priority.");
                else Assert.LessOrEqual(a.command, b.command, "State order groups commands within a key.");
            }
        }

        [Test]
        public void Merge_EmitsOneRunPerConsecutiveCommand_InSortedInstanceOrder()
        {
            var items = new NativeList<MeshDrawItem>(6, Allocator.Temp);
            var draws = new NativeList<MeshInstancedDraw>(6, Allocator.Temp);
            var instanceToEntity = new NativeArray<uint>(6, Allocator.Temp);
            try
            {
                int[] commands = { 2, 2, 5, 2, 7, 7 };
                for (int i = 0; i < commands.Length; ++i) items.Add(new MeshDrawItem { command = commands[i], entity = 10 + i });
                new MeshDrawMergeJob { items = items, draws = draws, instanceToEntity = instanceToEntity }.Execute();

                CollectionAssert.AreEqual(new uint[] { 10, 11, 12, 13, 14, 15 }, instanceToEntity.ToArray());
                var expected = new[] { (2, 0, 2), (5, 2, 1), (2, 3, 1), (7, 4, 2) };
                Assert.AreEqual(expected.Length, draws.Length);
                for (int i = 0; i < expected.Length; ++i)
                    Assert.AreEqual(expected[i], (draws[i].command, draws[i].instanceOffset, draws[i].instanceCount));
            }
            finally
            {
                items.Dispose();
                draws.Dispose();
                instanceToEntity.Dispose();
            }
        }

        struct DrawSpec
        {
            public int material, priority;
            public float depth;
            public bool visible;
            public DrawSpec(int material, int priority, float depth, bool visible = true)
            {
                this.material = material; this.priority = priority; this.depth = depth; this.visible = visible;
            }
        }

        // Builds the world's draw list for one pass from hand-written visibility and returns (entity order, runs).
        static void Build(MeshWorldFixture fixture, in MeshPassDesc desc, Material[] materials, DrawSpec[] specs,
            out int[] entities, out (int command, int offset, int count)[] runs, out int[] commandOfMaterial)
        {
            MeshWorld world = fixture.world;
            Mesh mesh = fixture.Mesh(1);
            var ids = new MeshEntityId[specs.Length];
            var elements = new MeshElementId[specs.Length];
            using (MeshWorldUpdate update = world.BeginUpdate())
            {
                for (int i = 0; i < specs.Length; ++i)
                {
                    ids[i] = MeshTestUtility.CreateEntity(update, new float3(i, 0, 0));
                    Material material = materials[specs[i].material];
                    elements[i] = update.AddElement(ids[i], MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), specs[i].priority);
                }
                update.Commit();
            }
            MeshPassCache cache = world.GetPassCache(desc);
            commandOfMaterial = new int[materials.Length];
            for (int i = 0; i < specs.Length; ++i)
                commandOfMaterial[specs[i].material] = (int)(cache.candidates[(int)elements[i].Index].y & MeshPassCache.CommandMask);

            var visibility = new MeshViewVisibility
            {
                visible = new NativeArray<byte>(world.entityHighWater, Allocator.TempJob),
                depth = new NativeArray<float>(world.entityHighWater, Allocator.TempJob),
                lod = new NativeArray<byte>(world.entityHighWater, Allocator.TempJob),
                fade = new NativeArray<float>(world.entityHighWater, Allocator.TempJob)
            };
            MeshDrawListBuild build = default;
            try
            {
                for (int i = 0; i < specs.Length; ++i)
                {
                    visibility.visible[(int)ids[i].Index] = (byte)(specs[i].visible ? 1 : 0);
                    visibility.depth[(int)ids[i].Index] = specs[i].depth;
                    visibility.fade[(int)ids[i].Index] = 1;
                }
                var specOfEntity = new Dictionary<uint, int>();
                for (int i = 0; i < ids.Length; ++i) specOfEntity.Add(ids[i].Index, i);
                build = MeshDrawListBuild.Schedule(cache, world, visibility);
                build.job.Complete();
                entities = new int[build.instanceCount];
                for (int i = 0; i < entities.Length; ++i)
                {
                    Assert.AreEqual((uint)build.items[i].entity, build.instanceToEntity[i]);
                    entities[i] = specOfEntity[build.instanceToEntity[i]];
                }
                runs = new (int, int, int)[build.draws.Length];
                for (int i = 0; i < runs.Length; ++i) runs[i] = (build.draws[i].command, build.draws[i].instanceOffset, build.draws[i].instanceCount);
            }
            finally
            {
                build.Dispose();
                visibility.visible.Dispose();
                visibility.depth.Dispose();
                visibility.lod.Dispose();
                visibility.fade.Dispose();
            }
        }

        [Test]
        public void BackToFrontBuild_OrdersByPriorityThenFarthest_AndSkipsInvisibleMembers()
        {
            using (var fixture = new MeshWorldFixture())
            {
                Material[] materials = { fixture.Material(0, 1), fixture.Material(1, 1) };
                DrawSpec[] specs =
                {
                    new DrawSpec(0, 0, 5f), new DrawSpec(1, 0, 20f), new DrawSpec(0, 0, 10f),
                    new DrawSpec(0, -1, 1f), new DrawSpec(0, 1, 50f), new DrawSpec(0, 0, 30f, visible: false)
                };
                Build(fixture, MeshPassPresets.TranslucentT0, materials, specs, out int[] order, out var runs, out int[] commands);

                CollectionAssert.AreEqual(new[] { 3, 1, 2, 0, 4 }, order);
                CollectionAssert.AreEqual(new[] { (commands[0], 0, 1), (commands[1], 1, 1), (commands[0], 2, 2), (commands[0], 4, 1) }, runs);
            }
        }

        [Test]
        public void FrontToBackBuild_OrdersByCoarseDistanceThenState()
        {
            using (var fixture = new MeshWorldFixture())
            {
                Material[] materials = { fixture.Material(0, 0), fixture.Material(0, 0) };
                // 2.0 m and 2.05 m share a decimeter bucket, so state (the first-created material) decides inside it.
                DrawSpec[] specs = { new DrawSpec(0, 0, 12f), new DrawSpec(1, 0, 2f), new DrawSpec(0, 0, 2.05f), new DrawSpec(1, 0, 0.5f) };
                Build(fixture, MeshPassPresets.Depth, materials, specs, out int[] order, out var runs, out _);

                CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, order);
                Assert.AreEqual(4, runs.Length);
            }
        }

        [Test]
        public void GpuCompaction_MatchesTheCpuBuildPerCommandRange()
        {
            MeshTestUtility.RequireComputeReadback();
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshTestUtility.RequireGpuIndirect(world);
                Mesh mesh = fixture.Mesh(2);
                Material[] materials = { fixture.Material(0, 0), fixture.Material(1, 0) };
                // Bands [0.3, 0.44] and [0.1, 0.14]; the bound's largest half-extent is 0.5, so the screen size is 0.5 / distance.
                // The distances cover stable and fading levels 0 and 1, a fading last level and a LOD-culled entity (5.2 m).
                // The same mesh fills both levels, so a fading command holds the outgoing and the incoming instance of one entity.
                MeshLodChain chain = MeshLodChain.Create(new[] { 0.3f, 0.1f }, 0.2f);
                float[] distances = { 0.6f, 1.25f, 1.45f, 1.2f, 2.3f, 3.8f, 5.2f, 0.85f, 1.3f, 1.75f, 4f, 4.6f };
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    for (int i = 0; i < distances.Length; ++i)
                    {
                        var center = new float3(distances[i], 0, 0);
                        EMeshEntityFlags flags = i % 5 == 3 ? EMeshEntityFlags.None : EMeshEntityFlags.Visible;
                        MeshEntityId id = update.CreateEntity(float4x4.Translate(center), MeshTestUtility.Bound(center), MeshTestUtility.Desc(flags), chain);
                        for (int lod = 0; lod < 2; ++lod)
                        {
                            for (int subMesh = 0; subMesh < 2; ++subMesh)
                            {
                                Material material = materials[(i + subMesh + lod) % materials.Length];
                                update.AddElement(id, MeshTestUtility.Id(mesh), subMesh, lod, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                            }
                        }
                    }
                    update.Commit();
                }
                world.UpdateResidency();
                MeshPassCache cache = world.GetPassCache(MeshPassPresets.GBuffer);
                Assert.IsTrue(cache.desc.ditherLodTransitions);
                Assert.IsNotNull(cache.candidateBuffer.buffer, "Residency uploads the candidate table.");
                int commandCount = cache.commandHighWater, candidateCount = cache.candidates.Length;
                Assert.Greater(commandCount, 1);

                MeshView view = MeshTestUtility.CameraView(31);
                uint[] visibleWords = MeshTestUtility.PackedCullOnCpu(world, view);
                var cpu = new Dictionary<(int command, bool fading), List<(uint entity, float fade)>>();
                MeshVisibilityHandle handle = world.visibilityShare.Acquire(world, view);
                MeshDrawListBuild build = default;
                try
                {
                    build = MeshDrawListBuild.Schedule(cache, world, world.visibilityShare.Get(handle));
                    build.job.Complete();
                    foreach (MeshInstancedDraw draw in build.draws)
                    {
                        var key = (draw.command, draw.fading);
                        if (!cpu.TryGetValue(key, out var list)) cpu.Add(key, list = new List<(uint, float)>());
                        for (int i = 0; i < draw.instanceCount; ++i)
                            list.Add((build.instanceToEntity[draw.instanceOffset + i], build.instanceFade[draw.instanceOffset + i]));
                    }
                }
                finally
                {
                    build.Dispose();
                    world.visibilityShare.Release(handle);
                }
                Assert.IsTrue(cpu.Keys.Any(k => !k.fading) && cpu.Keys.Any(k => k.fading), "The fixture covers stable and fading ranges.");

                ComputeShader shader = Object.Instantiate(fixture.shader);
                NativeArray<uint> meta = cache.GetCommandMeta();
                var metaValues = new uint[commandCount * MeshPassCache.MetaStride];
                for (int i = 0; i < metaValues.Length; ++i) metaValues[i] = meta[i];
                int instanceCapacity = math.max(1, 2 * cache.memberCount);
                var buffers = new List<ComputeBuffer>();
                try
                {
                    ComputeBuffer Upload(Array values, int stride, ComputeBufferType type = ComputeBufferType.Default)
                    {
                        var buffer = new ComputeBuffer(values.Length, stride, type);
                        buffers.Add(buffer);
                        buffer.SetData(values);
                        return buffer;
                    }
                    ComputeBuffer metaBuffer = Upload(metaValues, sizeof(uint));
                    ComputeBuffer counts = Upload(new uint[commandCount * 2], sizeof(uint));
                    ComputeBuffer args = Upload(new uint[commandCount * 2 * 5], sizeof(uint), ComputeBufferType.IndirectArguments);
                    ComputeBuffer instances = Upload(new uint[instanceCapacity], sizeof(uint));
                    ComputeBuffer fades = Upload(new float[instanceCapacity], sizeof(float));
                    ComputeBuffer visibility = Upload(visibleWords, sizeof(uint));
                    int clear = shader.FindKernel("ClearCounts"), compact = shader.FindKernel("Compact"), indirect = shader.FindKernel("BuildIndirectArgs");
                    shader.SetInt("_CommandCount", commandCount);
                    shader.SetInt("_CandidateCount", candidateCount);
                    shader.SetInt("_DitherLod", 1);
                    shader.SetBuffer(clear, "_VisibleCounts", counts);
                    shader.SetBuffer(clear, "_IndirectArgs", args);
                    shader.SetBuffer(compact, "_CandidateTable", cache.candidateBuffer.buffer);
                    shader.SetBuffer(compact, "_Visibility", visibility);
                    shader.SetBuffer(compact, "_CommandMeta", metaBuffer);
                    shader.SetBuffer(compact, "_VisibleCounts", counts);
                    shader.SetBuffer(compact, "_InstanceToEntity", instances);
                    shader.SetBuffer(compact, "_InstanceFade", fades);
                    shader.SetBuffer(indirect, "_CommandMeta", metaBuffer);
                    shader.SetBuffer(indirect, "_VisibleCounts", counts);
                    shader.SetBuffer(indirect, "_IndirectArgs", args);
                    shader.Dispatch(clear, (commandCount + 63) / 64, 1, 1);
                    shader.Dispatch(compact, (candidateCount + 63) / 64, 1, 1);
                    shader.Dispatch(indirect, (commandCount + 63) / 64, 1, 1);

                    var argValues = new uint[commandCount * 2 * 5];
                    var instanceValues = new uint[instanceCapacity];
                    var fadeValues = new float[instanceCapacity];
                    args.GetData(argValues);
                    instances.GetData(instanceValues);
                    fades.GetData(fadeValues);
                    for (int command = 0; command < commandCount; ++command)
                    {
                        if (cache.commands[command].refCount == 0) continue;
                        MeshDrawCommandBinding binding = cache.GetBinding(command);
                        for (int range = 0; range < 2; ++range)
                        {
                            string label = $"command {command}, {(range == 0 ? "stable" : "fading")} range";
                            cpu.TryGetValue((command, range == 1), out var expected);
                            expected ??= new List<(uint, float)>();
                            int argsBase = (command * 2 + range) * 5;
                            Assert.AreEqual(binding.indexCount, argValues[argsBase + 0], label);
                            Assert.AreEqual((uint)expected.Count, argValues[argsBase + 1], label);
                            Assert.AreEqual(binding.indexStart, argValues[argsBase + 2], label);
                            Assert.AreEqual(binding.baseVertex, argValues[argsBase + 3], label);
                            uint first = metaValues[command * MeshPassCache.MetaStride + 3 + range];
                            var gpu = new List<(uint entity, float fade)>();
                            for (int i = 0; i < expected.Count; ++i) gpu.Add((instanceValues[first + i], fadeValues[first + i]));
                            expected.Sort();
                            gpu.Sort();
                            for (int i = 0; i < expected.Count; ++i)
                            {
                                Assert.AreEqual(expected[i].entity, gpu[i].entity, label);
                                Assert.AreEqual(expected[i].fade, gpu[i].fade, 1e-4f, $"{label}, entity {expected[i].entity}");
                            }
                        }
                    }
                }
                finally
                {
                    foreach (ComputeBuffer buffer in buffers) buffer.Release();
                    Object.DestroyImmediate(shader);
                }
            }
        }
    }
}
