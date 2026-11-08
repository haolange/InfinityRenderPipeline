using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshPassCacheTests
    {
        /// <summary>The pass membership rule, written from the world's records independently of the cache.</summary>
        static bool ExpectedMember(MeshWorld world, in MeshPassDesc desc, int elementIndex, out int entity, out MeshDrawCommandKey key)
        {
            entity = -1;
            key = default;
            if (!world.IsElementSlotLive(elementIndex)) return false;
            MeshElement element = world.elements[elementIndex];
            if (!element.material.IsValid) return false;
            MeshMaterialState state = world.materials[(int)element.material.Index].state;
            if (!MaterialRouteUtility.RouteEnablesPass(state.route, state.stage, desc.lightMode)) return false;
            if (state.renderQueue < desc.queueMin || state.renderQueue > desc.queueMax) return false;
            entity = (int)element.entity.Index;
            if ((world.entities[entity].desc.flags & desc.requireEntityFlags) != desc.requireEntityFlags) return false;
            key = new MeshDrawCommandKey((int)element.section.Index, (int)element.material.Index);
            return true;
        }

        static uint2 CandidateAt(MeshPassCache cache, int elementIndex)
        {
            NativeArray<uint2> candidates = cache.candidates;
            return elementIndex < candidates.Length ? candidates[elementIndex] : new uint2(0u, MeshPassCache.NoCommand);
        }

        static int CommandAt(MeshPassCache cache, int elementIndex) => (int)(CandidateAt(cache, elementIndex).y & MeshPassCache.CommandMask);

        static Dictionary<MeshDrawCommandKey, int> LiveCommandRefCounts(MeshPassCache cache)
        {
            var result = new Dictionary<MeshDrawCommandKey, int>();
            NativeArray<MeshDrawCommand> commands = cache.commands;
            for (int i = 0; i < commands.Length; ++i)
                if (commands[i].refCount > 0) result.Add(commands[i].key, commands[i].refCount);
            return result;
        }

        static void AssertMatchesRule(MeshWorld world, MeshPassCache cache)
        {
            int members = 0;
            var memberCounts = new int[world.entityHighWater];
            for (int element = 0; element < world.elementHighWater; ++element)
            {
                uint2 candidate = CandidateAt(cache, element);
                bool expected = ExpectedMember(world, cache.desc, element, out int entity, out MeshDrawCommandKey key);
                Assert.AreEqual(expected, candidate.y != MeshPassCache.NoCommand, $"{cache.desc}: membership of element {element}");
                if (!expected) continue;
                members++;
                memberCounts[entity]++;
                Assert.AreEqual((uint)entity, candidate.x, $"{cache.desc}: entity of element {element}");
                Assert.AreEqual(key, cache.commands[(int)(candidate.y & MeshPassCache.CommandMask)].key, $"{cache.desc}: command of element {element}");
                Assert.AreEqual((uint)world.elements[element].lodIndex, candidate.y >> MeshPassCache.LodShift, $"{cache.desc}: LOD of element {element}");
            }
            Assert.AreEqual(members, cache.memberCount, $"{cache.desc}: memberCount");
            NativeArray<int> entityMembers = cache.entityMemberCount;
            for (int entity = 0; entity < memberCounts.Length; ++entity)
                Assert.AreEqual(memberCounts[entity], entity < entityMembers.Length ? entityMembers[entity] : 0, $"{cache.desc}: members of entity {entity}");
        }

        static void AssertSameContent(MeshWorld world, MeshPassCache incremental, MeshPassCache fresh)
        {
            AssertMatchesRule(world, incremental);
            AssertMatchesRule(world, fresh);
            Assert.AreEqual(fresh.commandCount, incremental.commandCount, $"{incremental.desc}: commandCount");
            CollectionAssert.AreEquivalent(LiveCommandRefCounts(fresh), LiveCommandRefCounts(incremental), $"{incremental.desc}: command references");
        }

        [Test]
        public void Membership_FollowsRouteQueueAndRequiredFlags_NotViewMasks()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                Mesh mesh = fixture.Mesh(1);
                Material deferred = fixture.Material(0, 0);
                Material forward = fixture.Material(1, 0);
                Material translucent = fixture.Material(0, 2);
                Material late = fixture.Material(0, 0);
                ulong meshId = MeshTestUtility.Id(mesh);
                var entities = new MeshEntityId[6];
                MeshElementId lateElement;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    entities[0] = MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.Visible);
                    update.AddElement(entities[0], meshId, 0, 0, MeshTestUtility.Id(deferred), MeshTestUtility.StateOf(deferred), 0);
                    entities[1] = MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.Visible | EMeshEntityFlags.CastShadow | EMeshEntityFlags.ObjectMotion);
                    update.AddElement(entities[1], meshId, 0, 0, MeshTestUtility.Id(forward), MeshTestUtility.StateOf(forward), 0);
                    entities[2] = MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.Visible | EMeshEntityFlags.CastShadow);
                    update.AddElement(entities[2], meshId, 0, 0, MeshTestUtility.Id(translucent), MeshTestUtility.StateOf(translucent), 0);
                    entities[3] = MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.Visible);
                    lateElement = update.AddElement(entities[3], meshId, 0, 0, MeshTestUtility.Id(late), MeshTestUtility.StateOf(late, 3500), 0);
                    entities[4] = MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.Visible);
                    update.AddElement(entities[4], meshId, 0, 0, 0, default, 0);
                    // View masks and the Visible flag belong to visibility, not to pass membership.
                    entities[5] = MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.None, layerMask: 0, renderingLayerMask: 0, sceneCullingMask: 0);
                    update.AddElement(entities[5], meshId, 0, 0, MeshTestUtility.Id(deferred), MeshTestUtility.StateOf(deferred), 0);
                    update.Commit();
                }

                var expected = new Dictionary<MeshPassDesc, int[]>
                {
                    { MeshPassPresets.Depth, new[] { 0, 1, 5 } },
                    { MeshPassPresets.GBuffer, new[] { 0, 1, 5 } },
                    { MeshPassPresets.Forward, new[] { 1 } },
                    { MeshPassPresets.Motion, new[] { 1 } },
                    { MeshPassPresets.Shadow, new[] { 1 } },
                    { MeshPassPresets.TranslucentDepth, new[] { 2 } },
                    { MeshPassPresets.TranslucentT0, new int[0] },
                    { MeshPassPresets.TranslucentT1, new[] { 2 } },
                    { MeshPassPresets.TranslucentT2, new int[0] }
                };
                foreach (KeyValuePair<MeshPassDesc, int[]> pass in expected)
                {
                    MeshPassCache cache = world.GetPassCache(pass.Key);
                    var members = new List<int>();
                    for (int i = 0; i < entities.Length; ++i)
                        if (cache.entityMemberCount[(int)entities[i].Index] > 0) members.Add(i);
                    CollectionAssert.AreEqual(pass.Value, members, pass.Key.ToString());
                    AssertMatchesRule(world, cache);
                }

                MeshPassCache shadow = world.GetPassCache(MeshPassPresets.Shadow);
                MeshPassCache gbuffer = world.GetPassCache(MeshPassPresets.GBuffer);
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.SetEntityDesc(entities[0], MeshTestUtility.Desc(EMeshEntityFlags.Visible | EMeshEntityFlags.CastShadow));
                    update.Commit();
                }
                Assert.Greater(shadow.entityMemberCount[(int)entities[0].Index], 0, "A required flag admits the entity.");

                uint shadowRevision = shadow.revision;
                uint gbufferRevision = gbuffer.revision;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.SetElementMaterial(lateElement, MeshTestUtility.Id(late), MeshTestUtility.StateOf(late, 2000));
                    update.Commit();
                }
                Assert.Greater(gbuffer.entityMemberCount[(int)entities[3].Index], 0, "A queue change into range admits the element.");
                Assert.AreNotEqual(gbufferRevision, gbuffer.revision);
                Assert.AreEqual(shadowRevision, shadow.revision, "An edit outside a pass leaves that pass unchanged.");
                AssertMatchesRule(world, shadow);
                AssertMatchesRule(world, gbuffer);
            }
        }

        [Test]
        public void IncrementalApply_EqualsAFreshBuild_AfterADeterministicEditSequence()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                Mesh[] meshes = { fixture.Mesh(3), fixture.Mesh(2) };
                Material[] materials = { fixture.Material(0, 0), fixture.Material(1, 0), fixture.Material(0, 1), fixture.Material(1, 3) };
                var custom = new MeshPassDesc("RasterDepthPass", 1000, 2500, ESortMode.State, EMeshEntityFlags.CastShadow);
                world.GetPassCache(custom);
                var random = new System.Random(20261107);
                var liveEntities = new List<MeshEntityId>();
                var liveElements = new List<MeshElementId>();
                int commits = 0, rollbacks = 0;

                for (int transaction = 0; transaction < 48; ++transaction)
                {
                    using (MeshWorldUpdate update = world.BeginUpdate())
                    {
                        int operations = 1 + random.Next(4);
                        for (int operation = 0; operation < operations; ++operation)
                        {
                            CollectLive(world, liveEntities, liveElements);
                            switch (liveEntities.Count == 0 ? 0 : random.Next(6))
                            {
                                case 0:
                                {
                                    MeshEntityId entity = MeshTestUtility.CreateEntity(update, new float3(random.Next(8), 0, 0), RandomFlags(random));
                                    int mesh = random.Next(meshes.Length);
                                    int elements = 1 + random.Next(meshes[mesh].subMeshCount);
                                    for (int subMesh = 0; subMesh < elements; ++subMesh)
                                    {
                                        int material = random.Next(materials.Length + 1) - 1;
                                        update.AddElement(entity, MeshTestUtility.Id(meshes[mesh]), subMesh, 0, material < 0 ? 0 : MeshTestUtility.Id(materials[material]),
                                            material < 0 ? default : MeshTestUtility.StateOf(materials[material]), random.Next(3));
                                    }
                                    break;
                                }
                                case 1:
                                    update.RemoveEntity(liveEntities[random.Next(liveEntities.Count)]);
                                    break;
                                case 2:
                                    if (liveElements.Count > 0)
                                    {
                                        int material = random.Next(materials.Length + 1) - 1;
                                        update.SetElementMaterial(liveElements[random.Next(liveElements.Count)], material < 0 ? 0 : MeshTestUtility.Id(materials[material]),
                                            material < 0 ? default : MeshTestUtility.StateOf(materials[material]));
                                    }
                                    break;
                                case 3:
                                    update.SetEntityDesc(liveEntities[random.Next(liveEntities.Count)], MeshTestUtility.Desc(RandomFlags(random)));
                                    break;
                                case 4:
                                    // Moves the shared deferred material out of and back into the opaque queue range.
                                    if (liveElements.Count > 0)
                                        update.SetElementMaterial(liveElements[random.Next(liveElements.Count)], MeshTestUtility.Id(materials[0]),
                                            MeshTestUtility.StateOf(materials[0], random.Next(2) == 0 ? 2000 : 3500));
                                    break;
                                default:
                                    if (liveElements.Count > 0) update.SetElementPriority(liveElements[random.Next(liveElements.Count)], random.Next(5));
                                    break;
                            }
                        }
                        if (random.Next(5) != 0)
                        {
                            update.Commit();
                            commits++;
                        }
                        else rollbacks++;
                    }
                }
                Assert.Greater(commits, 0);
                Assert.Greater(rollbacks, 0);
                Assert.Greater(world.elementCount, 0);

                var descs = new List<MeshPassDesc>(MeshPassPresets.all) { custom };
                foreach (MeshPassDesc desc in descs)
                {
                    var fresh = new MeshPassCache(desc);
                    try
                    {
                        fresh.Build(world);
                        AssertSameContent(world, world.GetPassCache(desc), fresh);
                    }
                    finally { fresh.Dispose(); }
                }
            }
        }

        static EMeshEntityFlags RandomFlags(System.Random random)
        {
            EMeshEntityFlags flags = EMeshEntityFlags.Visible;
            if (random.Next(2) == 0) flags |= EMeshEntityFlags.CastShadow;
            if (random.Next(2) == 0) flags |= EMeshEntityFlags.ObjectMotion;
            return flags;
        }

        static void CollectLive(MeshWorld world, List<MeshEntityId> entities, List<MeshElementId> elements)
        {
            entities.Clear();
            elements.Clear();
            for (int i = 0; i < world.entityHighWater; ++i)
                if (world.IsEntitySlotLive(i)) entities.Add(new MeshEntityId((uint)i, world.entityGenerations[i]));
            for (int i = 0; i < world.elementHighWater; ++i)
                if (world.IsElementSlotLive(i)) elements.Add(new MeshElementId((uint)i, world.elementGenerations[i]));
        }

        [Test]
        public void CommandKey_EqualityIsFieldLevel_UnderHashCollision()
        {
            Assert.IsTrue(TryFindCollision(out MeshDrawCommandKey first, out MeshDrawCommandKey second), "No colliding key pair in the searched range.");
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
            Assert.IsFalse(first.Equals(second));
            var lookup = new NativeParallelHashMap<MeshDrawCommandKey, int>(2, Allocator.Temp);
            try
            {
                lookup.Add(first, 11);
                lookup.Add(second, 22);
                Assert.IsTrue(lookup.TryGetValue(first, out int a));
                Assert.IsTrue(lookup.TryGetValue(second, out int b));
                Assert.AreEqual((11, 22), (a, b));
            }
            finally { lookup.Dispose(); }
        }

        static bool TryFindCollision(out MeshDrawCommandKey first, out MeshDrawCommandKey second)
        {
            var seen = new Dictionary<int, MeshDrawCommandKey>(1 << 16);
            for (int section = 0; section < 1024; ++section)
            {
                for (int material = 0; material < 1024; ++material)
                {
                    var key = new MeshDrawCommandKey(section, material);
                    if (seen.TryGetValue(key.GetHashCode(), out MeshDrawCommandKey prior))
                    {
                        first = prior;
                        second = key;
                        return true;
                    }
                    seen.Add(key.GetHashCode(), key);
                }
            }
            first = second = default;
            return false;
        }

        [Test]
        public void Interning_ReturnsOneCachePerEqualDesc_AndCapsAWorldAt64Descs()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                Assert.AreEqual(MeshPassPresets.all.Length, world.stats.passCacheCount, "Presets are interned at construction.");
                var equal = new MeshPassDesc(new string("RasterGBufferPass".ToCharArray()), 0, MeshPassPresets.OpaqueQueueMax, ESortMode.State);
                int builds = world.stats.passCacheBuilds;
                Assert.AreSame(world.GetPassCache(MeshPassPresets.GBuffer), world.GetPassCache(equal), "Field-level equality is the identity.");
                Assert.AreEqual(builds, world.stats.passCacheBuilds);

                Assert.Throws<ArgumentException>(() => world.GetPassCache(new MeshPassDesc("UnknownLightMode", 0, 10, ESortMode.State)));
                Assert.Throws<ArgumentException>(() => world.GetPassCache(new MeshPassDesc("RasterDepthPass", 20, 10, ESortMode.State)));
                using (MeshWorldUpdate update = world.BeginUpdate())
                    Assert.Throws<InvalidOperationException>(() => world.GetPassCache(new MeshPassDesc("RasterDepthPass", 0, 7, ESortMode.State)));

                for (int queueMax = 0; world.stats.passCacheCount < 64; ++queueMax)
                    world.GetPassCache(new MeshPassDesc("RasterDepthPass", 0, queueMax, ESortMode.State));
                Assert.AreEqual(64, world.passCaches.Count);
                Assert.Throws<InvalidOperationException>(() => world.GetPassCache(new MeshPassDesc("RasterDepthPass", 0, 1000, ESortMode.State)));
                Assert.AreEqual(64, world.passCaches.Count);
                Assert.DoesNotThrow(() => world.GetPassCache(new MeshPassDesc("RasterDepthPass", 0, 0, ESortMode.State)), "Interned descs stay available at the cap.");
            }
        }

        [Test]
        public void Commands_ShareOneSlotPerSectionAndMaterial_AndResolveBindingsWhenCreated()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                Mesh mesh = fixture.Mesh(1);
                Material first = fixture.Material(0, 0);
                Material second = fixture.Material(0, 0);
                ulong meshId = MeshTestUtility.Id(mesh);
                var entities = new MeshEntityId[3];
                var elements = new MeshElementId[3];
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    for (int i = 0; i < 3; ++i)
                    {
                        // Per-entity data does not split identical commands.
                        entities[i] = MeshTestUtility.CreateEntity(update, new float3(i, 0, 0),
                            i == 2 ? EMeshEntityFlags.Visible | EMeshEntityFlags.ReceiveShadow : EMeshEntityFlags.Visible, layerMask: 1 << i);
                        elements[i] = update.AddElement(entities[i], meshId, 0, 0, MeshTestUtility.Id(first), MeshTestUtility.StateOf(first), i * 5);
                    }
                    update.Commit();
                }
                MeshPassCache cache = world.GetPassCache(MeshPassPresets.GBuffer);
                MeshPassCache shadow = world.GetPassCache(MeshPassPresets.Shadow);
                uint shadowRevision = shadow.revision;
                Assert.AreEqual(1, cache.commandCount);
                Assert.AreEqual(3, cache.memberCount);
                int command = CommandAt(cache, (int)elements[0].Index);
                for (int i = 0; i < 3; ++i) Assert.AreEqual(command, CommandAt(cache, (int)elements[i].Index));
                Assert.AreEqual(3, cache.commands[command].refCount);
                MeshDrawCommandBinding binding = cache.GetBinding(command);
                Assert.AreSame(mesh, binding.mesh);
                Assert.AreSame(first, binding.material);
                Assert.AreEqual(MeshPassShaderUtility.FindPassIndex(first, "RasterGBufferPass"), binding.passIndex);
                Assert.GreaterOrEqual(binding.passIndex, 0);
                Assert.AreEqual(3u, binding.indexCount);

                uint revision = cache.revision;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.SetElementPriority(elements[0], 42);
                    update.Commit();
                }
                Assert.AreEqual(revision, cache.revision, "Priority is read at draw-list build; it does not change membership.");

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(entities[0]);
                    update.RemoveEntity(entities[1]);
                    update.Commit();
                }
                Assert.AreEqual(1, cache.commands[command].refCount);
                Assert.AreEqual(1, cache.memberCount);

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.SetElementMaterial(elements[2], MeshTestUtility.Id(second), MeshTestUtility.StateOf(second));
                    update.Commit();
                }
                Assert.AreEqual(1, cache.commandCount);
                Assert.AreEqual(1, cache.commandHighWater, "The freed command slot is reused.");
                Assert.AreEqual(command, CommandAt(cache, (int)elements[2].Index));
                Assert.AreSame(second, cache.GetBinding(command).material, "A reused slot resolves the binding of its new key.");
                Assert.AreEqual((int)world.elements[(int)elements[2].Index].material.Index, cache.commands[command].key.material);

                // A geometry edit recreates the entity in one transaction, as MeshComponent does on a geometry revision.
                mesh.SetTriangles(new[] { 0, 1, 2, 2, 1, 0 }, 0);
                MeshEntityId recreated;
                MeshElementId recreatedElement;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(entities[2]);
                    recreated = MeshTestUtility.CreateEntity(update, float3.zero);
                    recreatedElement = update.AddElement(recreated, meshId, 0, 0, MeshTestUtility.Id(second), MeshTestUtility.StateOf(second), 0);
                    update.Commit();
                }
                command = CommandAt(cache, (int)recreatedElement.Index);
                Assert.AreEqual(1, cache.commandCount);
                Assert.AreEqual(6u, cache.GetBinding(command).indexCount, "A command created again resolves the current submesh range.");
                Assert.AreEqual(6u, cache.GetCommandMeta()[command * 4 + 0]);
                Assert.AreEqual(shadowRevision, shadow.revision, "Elements outside the Shadow pass never touched its cache.");

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(recreated);
                    update.Commit();
                }
                Assert.AreEqual(0, cache.commandCount);
                Assert.AreEqual(0, cache.memberCount);
                Assert.IsNull(cache.GetBinding(command).material, "A freed command keeps no stale binding.");
                AssertMatchesRule(world, cache);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Commit_ThatCannotResolveAnEnabledPass_LeavesWorldAndCachesUnchanged(bool missingSubMesh)
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                Mesh mesh = fixture.Mesh(1);
                Material material = fixture.Material(0, 0);
                Shader fullscreen = Shader.Find("InfinityPipeline/Utility/DrawFullScreen");
                Assert.IsNotNull(fullscreen);
                Material passless = fixture.Own(new Material(fullscreen) { hideFlags = HideFlags.DontSave });
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    MeshEntityId entity = MeshTestUtility.CreateEntity(update, float3.zero);
                    update.AddElement(entity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                    update.Commit();
                }
                MeshPassCache cache = world.GetPassCache(MeshPassPresets.GBuffer);
                uint revision = cache.revision;
                int entities = world.entityCount, structural = world.structuralRevision;

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    MeshEntityId entity = MeshTestUtility.CreateEntity(update, float3.zero);
                    if (missingSubMesh)
                        update.AddElement(entity, MeshTestUtility.Id(mesh), 3, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                    else
                        update.AddElement(entity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(passless),
                            new MeshMaterialState(2000, 0, 0, MeshTestUtility.Id(fullscreen)), 0);
                    Assert.Throws<InvalidOperationException>(() => update.Commit());
                }

                Assert.AreEqual(entities, world.entityCount);
                Assert.AreEqual(structural, world.structuralRevision);
                Assert.AreEqual(revision, cache.revision);
                Assert.AreEqual(1, cache.memberCount);
                AssertMatchesRule(world, cache);
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    MeshTestUtility.CreateEntity(update, float3.zero);
                    update.Commit();
                }
                Assert.AreEqual(entities + 1, world.entityCount, "The failed transaction was closed by its rollback.");
            }
        }
    }
}
