using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
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
        }

        [Test]
        public void StateSortKey_PutsFadingInTheHighBit_AndKeepsTheStateKey()
        {
            const ulong state = (1ul << 62) | 0xABCDu;
            ulong stable = default(StateSortKey).Eval(new MeshSortKeyInput { stateKey = state, fade = 0f });
            ulong fading = default(StateSortKey).Eval(new MeshSortKeyInput { stateKey = state, fade = -0.25f });
            Assert.AreEqual(state, stable);
            Assert.AreEqual(1ul, fading >> 63);
            Assert.AreEqual(state, fading & ((1ul << 63) - 1ul));
            Assert.Greater(fading, stable);
            ulong masked = default(StateSortKey).Eval(new MeshSortKeyInput { stateKey = state | (1ul << 63), fade = 0f });
            Assert.AreEqual(0ul, masked >> 63);
        }

        [Test]
        public void TranslucentColorSortKey_OrdersPriorityThenFarther_AndPutsInvalidDistanceLast()
        {
            ulong Key(int priority, float distance) => default(TranslucentColorSortKey).Eval(new MeshSortKeyInput { priority = priority, distance = distance });
            Assert.Less(Key(0, 20f), Key(0, 5f), "Farther draws first within one priority.");
            Assert.Less(Key(-1, 1f), Key(0, 20f), "A lower priority draws first.");
            Assert.Less(Key(0, 5f), Key(1, 50f), "A higher priority draws last.");
            ulong nearest = Key(0, 0f);
            Assert.Less(Key(0, 1f), nearest);
            Assert.AreEqual(nearest, Key(0, float.NaN));
            Assert.AreEqual(nearest, Key(0, -3f));
            Assert.Less(Key(0, float.PositiveInfinity), Key(0, 1e9f));
            Assert.AreEqual(0xFFFFFFFFul, TranslucentColorSortKey.DistanceFarFirst(float.NaN));
        }

        [Test]
        public void Comparer_IsATotalOrder()
        {
            var random = new System.Random(5);
            var items = new MeshDrawInstance[64];
            for (int i = 0; i < items.Length; ++i)
            {
                bool fading = random.Next(3) == 0;
                float fade = fading ? (random.Next(2) == 0 ? 1 : -1) * (0.05f + 0.9f * (float)random.NextDouble()) : 0;
                items[i] = new MeshDrawInstance { key = (ulong)random.Next(3), command = random.Next(3), entity = i, fade = fade };
            }
            var comparer = new MeshDrawInstanceComparer();
            foreach (MeshDrawInstance a in items)
                foreach (MeshDrawInstance b in items)
                    Assert.AreEqual(Math.Sign(comparer.Compare(a, b)), -Math.Sign(comparer.Compare(b, a)));

            MeshDrawInstance[] first = items.OrderBy(_ => random.Next()).ToArray();
            MeshDrawInstance[] second = items.OrderBy(_ => random.Next()).ToArray();
            Array.Sort(first, comparer);
            Array.Sort(second, comparer);
            CollectionAssert.AreEqual(first.Select(item => item.entity), second.Select(item => item.entity), "A total order has one sorted sequence.");
            for (int i = 1; i < first.Length; ++i)
            {
                MeshDrawInstance a = first[i - 1], b = first[i];
                Assert.LessOrEqual(a.key, b.key);
                if (a.key != b.key) continue;
                Assert.LessOrEqual(a.command, b.command);
                if (a.command != b.command) continue;
                Assert.LessOrEqual(a.entity, b.entity);
            }
        }

        [Test]
        public void Merge_EmitsOneRunPerConsecutiveCommand_InSortedInstanceOrder()
        {
            var items = new NativeList<MeshDrawInstance>(6, Allocator.Temp);
            var draws = new NativeList<MeshDrawCall>(6, Allocator.Temp);
            var instanceToEntity = new NativeArray<uint>(6, Allocator.Temp);
            var instanceFade = new NativeArray<float>(6, Allocator.Temp);
            try
            {
                int[] commands = { 2, 2, 5, 2, 7, 7 };
                for (int i = 0; i < commands.Length; ++i) items.Add(new MeshDrawInstance { command = commands[i], entity = 10 + i });
                new MeshDrawMergeJob { items = items, draws = draws, instanceToEntity = instanceToEntity, instanceFade = instanceFade }.Execute();

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
                instanceFade.Dispose();
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

        // Builds the world's draw list for one pass and returns (entity order, runs). Entity i sits at distance depth from the
        // LOD origin under a context without planes; an invisible spec has no layer the context accepts.
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
                    ids[i] = MeshTestUtility.CreateEntity(update, new float3(specs[i].depth, 0, 0), layerMask: specs[i].visible ? ~0 : 0);
                    Material material = materials[specs[i].material];
                    elements[i] = update.AddElement(ids[i], MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), specs[i].priority);
                }
                update.Commit();
            }
            MeshPassCache cache = world.GetPassCache(desc);
            commandOfMaterial = new int[materials.Length];
            for (int i = 0; i < specs.Length; ++i)
                commandOfMaterial[specs[i].material] = (int)(cache.candidates[(int)elements[i].Index].y & MeshPassCache.CommandMask);

            MeshCullingContext context = MeshCullingContext.Create(ReadOnlySpan<Plane>.Empty, ~0, ulong.MaxValue, (uint)ERenderingLayer.Everything, false, MeshTestUtility.Lod);
            MeshDrawListBuild build = default;
            try
            {
                var specOfEntity = new Dictionary<uint, int>();
                for (int i = 0; i < ids.Length; ++i) specOfEntity.Add(ids[i].Index, i);
                build = MeshDrawListBuild.Schedule(cache, world, context);
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
                Build(fixture, MeshPassTable.TranslucentT0, materials, specs, out int[] order, out var runs, out int[] commands);

                CollectionAssert.AreEqual(new[] { 3, 1, 2, 0, 4 }, order);
                // Translucent passes do not dither, so the three visible members of command 0 are one run.
                CollectionAssert.AreEqual(new[] { (commands[0], 0, 1), (commands[1], 1, 1), (commands[0], 2, 3) }, runs);
            }
        }

        [Test]
        public void StateBuild_MergesOneCommandAcrossDistances()
        {
            using (var fixture = new MeshWorldFixture())
            {
                Material[] materials = { fixture.Material(0, 0), fixture.Material(0, 0) };
                DrawSpec[] specs = { new DrawSpec(0, 0, 12f), new DrawSpec(1, 0, 2f), new DrawSpec(0, 0, 2.05f), new DrawSpec(1, 0, 0.5f) };
                Build(fixture, MeshPassTable.Depth, materials, specs, out int[] order, out var runs, out int[] commandOfMaterial);
                MeshPassCache cache = fixture.world.GetPassCache(MeshPassTable.Depth);

                Assert.AreEqual(2, runs.Length, "One command is one run across distances.");
                int firstMaterial = cache.commands[commandOfMaterial[0]].stateKey <= cache.commands[commandOfMaterial[1]].stateKey ? 0 : 1;
                int secondMaterial = 1 - firstMaterial;
                Assert.AreEqual(commandOfMaterial[firstMaterial], runs[0].command);
                Assert.AreEqual(commandOfMaterial[secondMaterial], runs[1].command);
                Assert.AreEqual(2, runs[0].count);
                Assert.AreEqual(2, runs[1].count);
                Assert.AreEqual(0ul, cache.commands[commandOfMaterial[0]].stateKey >> 63);
                Assert.AreEqual(0ul, cache.commands[commandOfMaterial[1]].stateKey >> 63);
                // Entity order inside a command, not camera distance.
                Assert.AreEqual(firstMaterial == 0 ? 0 : 1, order[runs[0].offset]);
                Assert.AreEqual(firstMaterial == 0 ? 2 : 3, order[runs[0].offset + 1]);
            }
        }

        [Test]
        public void GpuBuild_MatchesTheCpuBuildPerCommandRange()
        {
            MeshTestUtility.RequireComputeReadback();
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshTestUtility.RequireGPUIndirect(world);
                Mesh mesh = fixture.Mesh(2);
                Material[] materials = { fixture.Material(0, 0), fixture.Material(1, 0) };
                // Bands [0.3, 0.44] and [0.1, 0.14]; the bound's largest half-extent is 0.5, so the screen size is 0.5 / distance.
                // The distances cover stable and fading levels 0 and 1, a fading last level and a LOD-culled entity (5.2 m).
                // The same mesh fills both levels, so a fading command holds the outgoing and the incoming instance of one entity.
                MeshLODChain chain = MeshLODChain.Create(new[] { 0.3f, 0.1f }, 0.2f);
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
                Material retiredMaterial = fixture.Material(0, 0);
                MeshEntityId retired;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    retired = MeshTestUtility.CreateEntity(update, float3.zero);
                    update.AddElement(retired, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(retiredMaterial), MeshTestUtility.StateOf(retiredMaterial), 0);
                    update.Commit();
                }
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(retired);
                    update.Commit();
                }
                world.UpdateResidency();
                MeshPassCache cache = world.GetPassCache(MeshPassTable.GBuffer);
                Assert.IsTrue(cache.desc.ditherLODTransitions);
                Assert.IsNotNull(cache.candidateBuffer.buffer, "Residency uploads the candidate table.");
                int commandCount = cache.commandHighWater, candidateCount = cache.candidates.Length;
                Assert.Greater(commandCount, 1);
                int freeCommand = -1;
                for (int command = 0; command < commandCount; ++command)
                    if (cache.commands[command].refCount == 0) freeCommand = command;
                Assert.GreaterOrEqual(freeCommand, 0, "The fixture contains a free command slot.");

                MeshCullingContext context = MeshTestUtility.CameraContext();
                var cpu = new Dictionary<(int command, bool fading), List<(uint entity, float fade)>>();
                MeshDrawListBuild build = default;
                try
                {
                    build = MeshDrawListBuild.Schedule(cache, world, context);
                    build.job.Complete();
                    foreach (MeshDrawCall draw in build.draws)
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
                }
                Assert.IsTrue(cpu.Keys.Any(k => !k.fading) && cpu.Keys.Any(k => k.fading), "The fixture covers stable and fading ranges.");

                ComputeShader shader = Object.Instantiate(fixture.shader);
                NativeArray<uint> meta = cache.GetCommandMeta();
                var metaValues = new uint[commandCount * MeshPassCache.MetaStride];
                for (int i = 0; i < metaValues.Length; ++i) metaValues[i] = meta[i];
                int instanceCapacity = math.max(1, 2 * (cache.memberCount + 1));
                var buffers = new List<GraphicsBuffer>();
                try
                {
                    GraphicsBuffer Upload(Array values, int stride, GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured)
                    {
                        var buffer = new GraphicsBuffer(target, values.Length, stride);
                        buffers.Add(buffer);
                        buffer.SetData(values);
                        return buffer;
                    }
                    GraphicsBuffer metaBuffer = Upload(metaValues, sizeof(uint));
                    GraphicsBuffer args = Upload(Enumerable.Repeat(uint.MaxValue, commandCount * 2 * 5).ToArray(), sizeof(uint), GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured);
                    GraphicsBuffer instances = Upload(new uint[instanceCapacity], sizeof(uint));
                    GraphicsBuffer fades = Upload(new float[instanceCapacity], sizeof(float));
                    GraphicsBuffer contextBuffer = Upload(new[] { context }, System.Runtime.InteropServices.Marshal.SizeOf<MeshCullingContext>());
                    int initialize = shader.FindKernel("InitializeIndirectArgs"), compact = shader.FindKernel("BuildDrawList");
                    using var commands = new CommandBuffer();
                    commands.SetComputeIntParam(shader, "_CommandCount", commandCount);
                    commands.SetComputeIntParam(shader, "_CandidateCount", candidateCount);
                    commands.SetComputeIntParam(shader, "_DitherLod", 1);
                    commands.SetComputeBufferParam(shader, initialize, "_CommandMeta", metaBuffer);
                    commands.SetComputeBufferParam(shader, initialize, "_IndirectArgs", args);
                    commands.SetComputeBufferParam(shader, compact, "_CandidateTable", cache.candidateBuffer.buffer);
                    commands.SetComputeBufferParam(shader, compact, "_CullingContext", contextBuffer);
                    BindColumns(commands, shader, compact, world);
                    commands.SetComputeBufferParam(shader, compact, "_CommandMeta", metaBuffer);
                    commands.SetComputeBufferParam(shader, compact, "_IndirectArgs", args);
                    commands.SetComputeBufferParam(shader, compact, "_InstanceToEntity", instances);
                    commands.SetComputeBufferParam(shader, compact, "_InstanceFade", fades);
                    commands.DispatchCompute(shader, initialize, (commandCount + 63) / 64, 1, 1);
                    commands.DispatchCompute(shader, compact, (candidateCount + 63) / 64, 1, 1);
                    Graphics.ExecuteCommandBuffer(commands);

                    var argValues = new uint[commandCount * 2 * 5];
                    var instanceValues = new uint[instanceCapacity];
                    var fadeValues = new float[instanceCapacity];
                    args.GetData(argValues);
                    instances.GetData(instanceValues);
                    fades.GetData(fadeValues);
                    for (int command = 0; command < commandCount; ++command)
                    {
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
                            Assert.AreEqual(0u, argValues[argsBase + 4], label);
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
                    // A context no entity passes appends nothing.
                    contextBuffer.SetData(new[] { MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), 0, ulong.MaxValue, (uint)ERenderingLayer.Everything, false, MeshTestUtility.Lod) });
                    Graphics.ExecuteCommandBuffer(commands);
                    args.GetData(argValues);
                    for (int command = 0; command < commandCount; ++command)
                        for (int range = 0; range < 2; ++range)
                        {
                            int argsBase = (command * 2 + range) * 5;
                            int metaBase = command * MeshPassCache.MetaStride;
                            Assert.AreEqual(metaValues[metaBase], argValues[argsBase], "A zero-visible retry restores index count.");
                            Assert.AreEqual(0u, argValues[argsBase + 1], "A zero-visible retry clears the old count.");
                            Assert.AreEqual(metaValues[metaBase + 1], argValues[argsBase + 2], "A zero-visible retry restores start index.");
                            Assert.AreEqual(metaValues[metaBase + 2], argValues[argsBase + 3], "A zero-visible retry restores base vertex.");
                            Assert.AreEqual(0u, argValues[argsBase + 4], "A zero-visible retry restores start instance.");
                        }

                    commands.Clear();
                    Material reusedMaterial = fixture.Material(0, 0);
                    MeshEntityId reusedEntity;
                    MeshElementId reusedElement;
                    using (MeshWorldUpdate update = world.BeginUpdate())
                    {
                        reusedEntity = MeshTestUtility.CreateEntity(update, new float3(0.6f, 0, 0));
                        reusedElement = update.AddElement(reusedEntity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(reusedMaterial), MeshTestUtility.StateOf(reusedMaterial), 0);
                        update.Commit();
                    }
                    world.UpdateResidency();
                    Assert.AreEqual(commandCount, cache.commandHighWater);
                    Assert.AreEqual(freeCommand, (int)(cache.candidates[(int)reusedElement.Index].y & MeshPassCache.CommandMask));
                    NativeArray<uint> reusedMeta = cache.GetCommandMeta();
                    for (int i = 0; i < metaValues.Length; ++i) metaValues[i] = reusedMeta[i];
                    metaBuffer.SetData(metaValues);
                    contextBuffer.SetData(new[] { context });
                    commands.SetComputeIntParam(shader, "_CommandCount", commandCount);
                    commands.SetComputeIntParam(shader, "_CandidateCount", cache.candidates.Length);
                    commands.SetComputeIntParam(shader, "_DitherLod", 1);
                    commands.SetComputeBufferParam(shader, initialize, "_CommandMeta", metaBuffer);
                    commands.SetComputeBufferParam(shader, initialize, "_IndirectArgs", args);
                    commands.SetComputeBufferParam(shader, compact, "_CandidateTable", cache.candidateBuffer.buffer);
                    commands.SetComputeBufferParam(shader, compact, "_CullingContext", contextBuffer);
                    BindColumns(commands, shader, compact, world);
                    commands.SetComputeBufferParam(shader, compact, "_CommandMeta", metaBuffer);
                    commands.SetComputeBufferParam(shader, compact, "_IndirectArgs", args);
                    commands.SetComputeBufferParam(shader, compact, "_InstanceToEntity", instances);
                    commands.SetComputeBufferParam(shader, compact, "_InstanceFade", fades);
                    commands.DispatchCompute(shader, initialize, (commandCount + 63) / 64, 1, 1);
                    commands.DispatchCompute(shader, compact, (cache.candidates.Length + 63) / 64, 1, 1);
                    Graphics.ExecuteCommandBuffer(commands);
                    args.GetData(argValues);
                    instances.GetData(instanceValues);
                    int reusedArgs = freeCommand * 2 * 5;
                    Assert.AreEqual(metaValues[freeCommand * MeshPassCache.MetaStride], argValues[reusedArgs]);
                    Assert.AreEqual(1u, argValues[reusedArgs + 1], "The reused slot receives its new visible member.");
                    Assert.AreEqual((uint)reusedEntity.Index, instanceValues[metaValues[freeCommand * MeshPassCache.MetaStride + 3]]);
                }
                finally
                {
                    foreach (GraphicsBuffer buffer in buffers) buffer.Release();
                    Object.DestroyImmediate(shader);
                }
            }
        }

        // The entity columns the build kernel reads, bound the way MeshDrawGPUBackend.Produce binds them.
        static void BindColumns(CommandBuffer commands, ComputeShader shader, int kernel, MeshWorld world)
        {
            MeshWorldGPU gpu = world.gpu;
            commands.SetComputeBufferParam(shader, kernel, "_EntityBoundsCenter", gpu.boundsCenter.buffer);
            commands.SetComputeBufferParam(shader, kernel, "_EntityBoundsExtent", gpu.boundsExtent.buffer);
            commands.SetComputeBufferParam(shader, kernel, "_EntityFlags", gpu.flags.buffer);
            commands.SetComputeBufferParam(shader, kernel, "_EntityLayerMask", gpu.layerMask.buffer);
            commands.SetComputeBufferParam(shader, kernel, "_EntitySceneCullingMask", gpu.sceneCullingMask.buffer);
            commands.SetComputeBufferParam(shader, kernel, "_EntityRenderingLayer", gpu.renderingLayers.buffer);
            commands.SetComputeBufferParam(shader, kernel, "_EntityLodChains", gpu.lodChains.buffer);
        }
    }
}
