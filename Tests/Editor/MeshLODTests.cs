using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Core.Geometry;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshLodTests
    {
        static readonly MeshLODProjection Perspective = new MeshLODProjection { origin = float3.zero, scale = 1, bias = 1 };

        // A bound whose largest half-extent is exactly 1, so the screen size is 1 / distance under the perspective projection above.
        static BoundingBox UnitBound(float distance) => new BoundingBox(new float3(distance, 0, 0), new float3(1, 0, 0));

        [Test]
        public void LodChainCreate_RejectsChainsThatAreNotLodGroupLowerBounds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshLODChain.Create(ReadOnlySpan<float>.Empty, 0.2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshLODChain.Create(new float[MeshLODChain.MaxLevels + 1], 0.2f));
            Assert.Throws<ArgumentException>(() => MeshLODChain.Create(new[] { 0.5f, 0.5f }, 0.2f), "Bounds strictly decrease.");
            Assert.Throws<ArgumentException>(() => MeshLODChain.Create(new[] { 1.5f }, 0.2f), "Bounds lie in [0, 1].");
            Assert.Throws<ArgumentException>(() => MeshLODChain.Create(new[] { -0.1f }, 0.2f), "Bounds lie in [0, 1].");
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshLODChain.Create(new[] { 0.5f }, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshLODChain.Create(new[] { 0.5f }, 1f));

            MeshLODChain chain = MeshLODChain.Create(new[] { 0.6f, 0.5f, 0.4f, 0.3f, 0.2f, 0.1f, 0.05f, 0f }, 0.25f);
            Assert.AreEqual(MeshLODChain.MaxLevels, chain.count);
            Assert.AreEqual(0.05f, chain.ScreenSize(6));
            Assert.AreEqual(0.25f, chain.fadeWidth);
        }

        [Test]
        public void LodChain_MatchesHlslFieldForField()
        {
            const string path = "Packages/com.infinity.render-pipeline/Shaders/Library/MeshLOD.hlsl";
            Match body = Regex.Match(File.ReadAllText(Path.GetFullPath(path)), @"struct\s+MeshLODChain\s*\{(?<body>[^}]*)\}");
            Assert.IsTrue(body.Success, "MeshLODChain not found in " + path);
            MatchCollection hlsl = Regex.Matches(body.Groups["body"].Value, @"(\w+)\s+(\w+)\s*;");
            FieldInfo[] fields = typeof(MeshLODChain).GetFields(BindingFlags.Instance | BindingFlags.Public);
            Assert.AreEqual(hlsl.Count, fields.Length, "Field count");
            int bytes = 0;
            for (int i = 0; i < fields.Length; ++i)
            {
                Type type = fields[i].FieldType;
                string expected = type == typeof(float4) ? "float4" : type == typeof(float) ? "float" : type == typeof(int) ? "int" : type.Name;
                Assert.AreEqual(hlsl[i].Groups[2].Value, fields[i].Name, $"Field {i} name");
                Assert.AreEqual(hlsl[i].Groups[1].Value, expected, $"Field {fields[i].Name} type");
                bytes += Marshal.SizeOf(type);
            }
            Assert.AreEqual(bytes, Marshal.SizeOf<MeshLODChain>(), "No padding");
        }

        [Test]
        public void LodChain_ThrowIfInvalid_RejectsHandBuiltChains()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeshLODChain { count = 0, fadeWidth = 0.2f }.ThrowIfInvalid());
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeshLODChain { count = 1 }.ThrowIfInvalid(), "fadeWidth 0 would divide by zero in the band.");
            Assert.Throws<ArgumentException>(() => new MeshLODChain { count = 2, fadeWidth = 0.2f }.ThrowIfInvalid(), "Two zero bounds do not decrease.");
            Assert.DoesNotThrow(() => MeshLODChain.Single.ThrowIfInvalid());
            Assert.IsFalse(MeshLODChain.Single.hasFadeBand);
            Assert.IsTrue(MeshLODChain.Create(new[] { 0.1f }, 0.2f).hasFadeBand, "A single level with a positive bound fades out.");
            Assert.IsTrue(MeshLODChain.Create(new[] { 0.5f, 0f }, 0.2f).hasFadeBand);

            Assert.Throws<ArgumentOutOfRangeException>(() => default(MeshLODProjection).ThrowIfInvalid(), "Scale 0 culls every positive bound.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeshLODProjection { scale = float.PositiveInfinity, bias = 1 }.ThrowIfInvalid());
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeshLODProjection { scale = 1, bias = 0 }.ThrowIfInvalid());
            Assert.Throws<ArgumentException>(() => new MeshLODProjection { origin = new float3(float.NaN, 0, 0), scale = 1, bias = 1 }.ThrowIfInvalid());
            Assert.DoesNotThrow(() => Perspective.ThrowIfInvalid());
        }

        [Test]
        public void FadingMembers_AreTheMembersWhoseChainHasABand()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                Mesh mesh = fixture.Mesh(1);
                MeshLODChain[] chains = { MeshLODChain.Single, MeshLODChain.Create(new[] { 0.1f }, 0.2f), MeshLODChain.Create(new[] { 0.5f, 0f }, 0.2f) };
                int[] expected = { 0, 1, 1 };
                var materials = new Material[chains.Length];
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    for (int i = 0; i < chains.Length; ++i)
                    {
                        // One material per entity, so each entity owns one command.
                        materials[i] = fixture.Material(0, 0);
                        MeshEntityId entity = update.CreateEntity(float4x4.identity, MeshTestUtility.Bound(float3.zero), MeshTestUtility.Desc(), chains[i]);
                        update.AddElement(entity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(materials[i]), MeshTestUtility.StateOf(materials[i]), 0);
                    }
                    update.Commit();
                }
                MeshPassCache cache = world.GetPassCache(MeshPassTable.GBuffer);
                for (int i = 0; i < chains.Length; ++i)
                {
                    ulong materialId = MeshTestUtility.Id(materials[i]);
                    int found = -1;
                    for (int command = 0; command < cache.commandHighWater; ++command)
                    {
                        MeshDrawCommand record = cache.commands[command];
                        if (record.refCount > 0 && world.materials[record.key.material].materialId == materialId) found = command;
                    }
                    Assert.GreaterOrEqual(found, 0, $"command of chain {i}");
                    Assert.AreEqual(expected[i], cache.commands[found].fadingMemberCount, $"fading members of chain {i}");
                }
            }
        }

        [Test]
        public void LodSelection_UsesLowerBoundsAndBandsAboveEachBound()
        {
            // Bands: level 0 [0.5, 0.6] (0.2 of 1 - 0.5), level 1 [0.25, 0.3] (0.2 of 0.5 - 0.25). Below 0.25 nothing draws.
            MeshLODChain chain = MeshLODChain.Create(new[] { 0.5f, 0.25f }, 0.2f);
            (float screen, byte level, float fade)[] cases =
            {
                (1.0f, 0, 1), (0.65f, 0, 1), (0.55f, 0, 0.5f), (0.5f, 0, 0),
                (0.4f, 1, 1), (0.275f, 1, 0.5f), (0.25f, 1, 0), (0.2f, MeshLODSelection.None, 1)
            };
            foreach (var (screen, level, fade) in cases)
            {
                MeshLODSelection.Select(chain, UnitBound(1 / screen), Perspective, out byte selected, out float selectedFade);
                Assert.AreEqual(level, selected, $"level at screen size {screen}");
                Assert.AreEqual(fade, selectedFade, 1e-4f, $"fade at screen size {screen}");
            }

            // A zero last bound never culls and never fades.
            MeshLODChain open = MeshLODChain.Create(new[] { 0.5f, 0f }, 0.2f);
            MeshLODSelection.Select(open, UnitBound(1e6f), Perspective, out byte far, out float farFade);
            Assert.AreEqual(1, far);
            Assert.AreEqual(1f, farFade);

            // Orthographic views measure screen size at distance 1, wherever the bound is.
            var orthographic = new MeshLODProjection { origin = float3.zero, scale = 0.35f, bias = 1, orthographic = true };
            MeshLODSelection.Select(chain, UnitBound(1000), orthographic, out byte ortho, out float orthoFade);
            Assert.AreEqual(1, ortho);
            Assert.AreEqual(1f, orthoFade);
        }

        [Test]
        public void LODFactor_IsComplementaryInsideTheBand_AndStableOnlyWithoutDither()
        {
            Assert.IsTrue(MeshDrawLod.Factor(0, 0, 1, true, out float stable));
            Assert.AreEqual(0f, stable, "Stable instances carry no dither factor.");
            Assert.IsFalse(MeshDrawLod.Factor(1, 0, 1, true, out _), "Outside a band the next level does not draw.");

            foreach (float fade in new[] { 0.01f, 0.5f, 0.99f })
            {
                Assert.IsTrue(MeshDrawLod.Factor(0, 0, fade, true, out float outgoing));
                Assert.IsTrue(MeshDrawLod.Factor(1, 0, fade, true, out float incoming));
                Assert.AreEqual(fade, outgoing);
                Assert.AreEqual(-fade, incoming, "LODDitheringTransition keeps complementary pixels for +f and -f.");
                Assert.IsFalse(MeshDrawLod.Factor(2, 0, fade, true, out _));

                Assert.IsTrue(MeshDrawLod.Factor(0, 0, fade, false, out float translucent));
                Assert.AreEqual(0f, translucent);
                Assert.IsFalse(MeshDrawLod.Factor(1, 0, fade, false, out _), "Passes without dither draw the stable level only.");
            }

            Assert.IsFalse(MeshDrawLod.Factor(0, 0, 0, true, out _), "At the bound the outgoing level has faded out.");
            Assert.IsTrue(MeshDrawLod.Factor(1, 0, 0, true, out float entering));
            Assert.Less(entering, 0f, "The incoming level is fully present at the bound.");
        }

        [Test]
        public void TranslucentBuild_DrawsTheSelectedLevelWithoutFadingRuns()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                Mesh mesh = fixture.Mesh(1);
                Material opaque = fixture.Material(0, 0), translucent = fixture.Material(0, 1);
                MeshLODChain chain = MeshLODChain.Create(new[] { 0.5f, 0.25f }, 0.2f);
                MeshEntityId entity;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    entity = update.CreateEntity(float4x4.identity, MeshTestUtility.Bound(float3.zero), MeshTestUtility.Desc(), chain);
                    foreach (Material material in new[] { opaque, translucent })
                        for (int lod = 0; lod < 2; ++lod)
                            update.AddElement(entity, MeshTestUtility.Id(mesh), 0, lod, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                    update.Commit();
                }

                // Screen size 0.55 sits halfway up level 0's band [0.5, 0.6).
                MeshCullingContext context = InBand(0.55f);
                MeshLODSelection.Select(chain, MeshTestUtility.Bound(float3.zero), context.ReadLODProjection(), out byte level, out float fade);
                Assert.AreEqual(0, level);
                Assert.That(fade, Is.EqualTo(0.5f).Within(1e-4f));
                var opaqueDraws = Build(world, MeshPassTable.GBuffer, context);
                CollectionAssert.AreEquivalent(new[] { (true, fade), (true, -fade) }, opaqueDraws, "Opaque passes cross-fade both levels.");
                var translucentDraws = Build(world, MeshPassTable.TranslucentT0, context);
                CollectionAssert.AreEqual(new[] { (false, 0f) }, translucentDraws, "Translucent passes draw the selected level only.");
            }
        }

        // A camera context whose LOD origin sees a half-extent-0.5 bound at the origin with the given screen size.
        static MeshCullingContext InBand(float screen)
        {
            MeshLODProjection lod = MeshTestUtility.Lod;
            lod.origin = new float3(0.5f / screen, 0, 0);
            return MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), ~0, ulong.MaxValue, (uint)ERenderingLayer.Everything, false, lod);
        }

        // Builds one pass under a context on the CPU backend and returns (fading, factor) per instance.
        static List<(bool fading, float fade)> Build(MeshWorld world, in MeshPassDesc desc, in MeshCullingContext context)
        {
            MeshPassCache cache = world.GetPassCache(desc);
            MeshDrawListBuild build = MeshDrawListBuild.Schedule(cache, world, context);
            try
            {
                build.job.Complete();
                var result = new List<(bool, float)>();
                foreach (MeshDrawCall draw in build.draws)
                    for (int i = 0; i < draw.instanceCount; ++i) result.Add((draw.fading, build.instanceFade[draw.instanceOffset + i]));
                return result;
            }
            finally
            {
                build.Dispose();
            }
        }

        [Test]
        public void GpuLodSelection_AgreesWithCpuSelection()
        {
            MeshTestUtility.RequireComputeReadback();
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshTestUtility.RequireGPUIndirect(world);
                MeshLODChain chain = MeshLODChain.Create(new[] { 0.4f, 0.2f, 0.1f }, 0.3f);
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    // Half-extent 0.5: distances from 0.6 m to 5.85 m sweep every level, every band and the LOD-culled range
                    // beyond 5 m; no distance lands within 0.9% of a bound or a band top.
                    for (int i = 0; i < 36; ++i)
                    {
                        var center = new float3(0.6f + i * 0.15f, 0, 0);
                        update.CreateEntity(float4x4.Translate(center), MeshTestUtility.Bound(center), MeshTestUtility.Desc(), chain);
                    }
                    update.Commit();
                }
                world.UpdateResidency();
                ComputeShader shader = Object.Instantiate(fixture.shader);
                int count = world.entityHighWater;
                uint2[] candidates = MeshTestUtility.LevelCandidates(count, 3);
                var run = new MeshProducerRun(shader, MeshEntityColumns.Of(world), candidates, 3);
                try
                {
                    var levels = new HashSet<int>();
                    bool sawBand = false, sawCulled = false;
                    foreach (MeshLODProjection lod in new[]
                    {
                        new MeshLODProjection { origin = float3.zero, scale = 1, bias = 1, orthographic = false },
                        new MeshLODProjection { origin = float3.zero, scale = 0.25f, bias = 1, orthographic = true }
                    })
                    {
                        string mode = lod.orthographic ? "orthographic" : "perspective";
                        MeshCullingContext context = MeshCullingContext.Create(MeshTestUtility.BoxPlanes(), ~0, ulong.MaxValue, (uint)ERenderingLayer.Everything, false, lod);
                        List<MeshDrawnInstance> cpu = MeshTestUtility.GatherOnCpu(world, candidates, 3, context, true);
                        List<MeshDrawnInstance> gpu = run.Build("BuildDrawList", context, dither: true);
                        Assert.AreEqual(cpu.Count, gpu.Count, $"{mode}: instance count");
                        for (int i = 0; i < cpu.Count; ++i)
                        {
                            string label = $"{mode} instance {i} {cpu[i]}";
                            Assert.AreEqual(cpu[i].entity, gpu[i].entity, label);
                            Assert.AreEqual(cpu[i].command, gpu[i].command, $"{label}: level");
                            Assert.AreEqual(cpu[i].factor == 0, gpu[i].factor == 0, $"{label}: stable or in a band");
                            Assert.That(gpu[i].factor, Is.EqualTo(cpu[i].factor).Within(2.0f / 65534.0f), $"{label}: fade");
                            if (cpu[i].factor >= 0) levels.Add(cpu[i].command);
                            sawBand |= cpu[i].factor != 0;
                        }
                        sawCulled |= Array.IndexOf(MeshTestUtility.DrawnEntities(cpu, count), 0u) >= 0;
                    }
                    Assert.AreEqual(3, levels.Count, "The sweep reaches every level.");
                    Assert.IsTrue(sawBand && sawCulled, "The sweep crosses a band and the culled range.");
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
