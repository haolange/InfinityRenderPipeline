using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Reflection probe list: record layout, blending priority and the slot state of the probe atlas. No cube is filtered.
    public class ReflectionProbeTests
    {
        const string ProbePath = "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/Lighting/ReflectionProbes.hlsl";

        [Test]
        public void ProbeRecord_MatchesHlslFieldForField()
        {
            string text = File.ReadAllText(Path.GetFullPath(ProbePath));
            text = Regex.Replace(text, @"//[^\r\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            Match body = Regex.Match(text, @"struct\s+FReflectionProbeRecord\s*\{(?<body>[^}]*)\}");
            Assert.IsTrue(body.Success, "FReflectionProbeRecord not found in " + ProbePath);
            var hlsl = new List<(string type, string name)>();
            foreach (Match field in Regex.Matches(body.Groups["body"].Value, @"(\w+)\s+(\w+)\s*;"))
                hlsl.Add((field.Groups[1].Value, field.Groups[2].Value));

            FieldInfo[] fields = typeof(FReflectionProbeRecord).GetFields(BindingFlags.Instance | BindingFlags.Public);
            Assert.AreEqual(hlsl.Count, fields.Length, "Field count");
            for (int i = 0; i < fields.Length; ++i)
            {
                Assert.AreEqual(hlsl[i].name, fields[i].Name, $"Field {i} name");
                Assert.AreEqual(typeof(Vector4), fields[i].FieldType, $"Field {fields[i].Name} type");
                Assert.AreEqual("float4", hlsl[i].type, $"Field {hlsl[i].name} HLSL type");
            }
            Assert.AreEqual(fields.Length * 16, Marshal.SizeOf<FReflectionProbeRecord>(), "No padding");
        }

        [Test]
        public void Priority_HigherImportanceFirst_ThenSmallerVolume()
        {
            var probes = new List<(int importance, float volume, string name)>
            {
                (1, 1000, "room"), (1, 8, "closet"), (2, 5000, "hall"), (0, 1, "tiny")
            };
            probes.Sort((a, b) => ReflectionProbePriority.Compare(a.importance, a.volume, b.importance, b.volume));
            CollectionAssert.AreEqual(new[] { "hall", "closet", "room", "tiny" }, probes.ConvertAll(p => p.name));
            Assert.AreEqual(24.0f, ReflectionProbePriority.Volume(new Bounds(Vector3.zero, new Vector3(2, 3, 4))), 1e-5f);
        }

        static Cubemap CreateCube() => new Cubemap(16, TextureFormat.RGBAHalf, false) { hideFlags = HideFlags.HideAndDontSave };

        [Test]
        public void Atlas_FiltersOnce_RefiltersOnChange_RollsBackOnFailure()
        {
            var atlas = new ReflectionProbeAtlas();
            Cubemap cube = CreateCube();
            try
            {
                atlas.Configure(128, 4);
                Assert.AreEqual(8, atlas.MipCount);
                var key = new FProbeCubeKey(1, 2);

                atlas.BeginFrame();
                Assert.IsTrue(atlas.Acquire(key, cube, 1, false, 1, out int slot));
                Assert.AreEqual(1, atlas.Uploads.Count);
                Assert.AreEqual(slot, atlas.Uploads[0].slot);
                atlas.MarkProduced(new[] { key }, 1);
                atlas.CommitFrame();
                Assert.IsTrue(atlas.IsValid(key));

                atlas.BeginFrame();
                Assert.IsTrue(atlas.Acquire(key, cube, 1, false, 2, out int again));
                Assert.AreEqual(slot, again, "same slot while the cube is unchanged");
                Assert.AreEqual(0, atlas.Uploads.Count, "an unchanged baked cube is not refiltered");
                atlas.CommitFrame();

                atlas.BeginFrame();
                atlas.Acquire(key, cube, 2, false, 3, out _);
                Assert.AreEqual(1, atlas.Uploads.Count, "a changed cube is refiltered");
                atlas.RollbackPending();
                Assert.IsFalse(atlas.IsValid(key), "a failed refilter leaves the slot invalid");

                atlas.BeginFrame();
                atlas.Acquire(key, cube, 2, false, 4, out _);
                Assert.AreEqual(1, atlas.Uploads.Count, "an invalid slot is filtered again");
                atlas.CommitFrame();
                Assert.IsFalse(atlas.IsValid(key), "a filter whose pass was never queued does not validate the slot");
            }
            finally
            {
                atlas.Dispose();
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void Atlas_RealtimeProbe_FiltersOncePerFrameAcrossCameras()
        {
            var atlas = new ReflectionProbeAtlas();
            Cubemap cube = CreateCube();
            try
            {
                atlas.Configure(64, 2);
                var key = new FProbeCubeKey(5, 6);
                for (int frame = 1; frame <= 2; ++frame)
                {
                    atlas.BeginFrame();
                    atlas.Acquire(key, cube, 1, true, frame, out _);
                    atlas.Acquire(key, cube, 1, true, frame, out _);
                    Assert.AreEqual(1, atlas.Uploads.Count, $"frame {frame}: one filter for two cameras");
                    atlas.MarkProduced(new[] { key }, 1);
                    atlas.CommitFrame();
                }
            }
            finally
            {
                atlas.Dispose();
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void Atlas_EvictsLeastRecentlyUsed_NeverThisFrame()
        {
            var atlas = new ReflectionProbeAtlas();
            Cubemap cube = CreateCube();
            try
            {
                atlas.Configure(32, 2);
                var a = new FProbeCubeKey(1, 1);
                var b = new FProbeCubeKey(2, 2);
                var c = new FProbeCubeKey(3, 3);

                atlas.BeginFrame();
                Assert.IsTrue(atlas.Acquire(a, cube, 1, false, 1, out int slotA));
                Assert.IsTrue(atlas.Acquire(b, cube, 1, false, 1, out _));
                Assert.IsFalse(atlas.Acquire(c, cube, 1, false, 1, out _), "both slots are in use this frame");
                Assert.AreEqual(1, atlas.RejectedThisFrame);
                atlas.MarkProduced(new[] { a, b }, 2);
                atlas.CommitFrame();

                atlas.BeginFrame();
                Assert.IsTrue(atlas.Acquire(b, cube, 1, false, 2, out _));
                atlas.CommitFrame();

                atlas.BeginFrame();
                Assert.IsTrue(atlas.Acquire(c, cube, 1, false, 3, out int slotC));
                Assert.AreEqual(slotA, slotC, "the least recently used probe gives up its slot");
                Assert.IsFalse(atlas.IsValid(a));
                atlas.CommitFrame();
            }
            finally
            {
                atlas.Dispose();
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void Atlas_DisabledOrReconfigured()
        {
            var atlas = new ReflectionProbeAtlas();
            try
            {
                atlas.Configure(0, 8);
                Assert.IsFalse(atlas.Enabled);
                Assert.IsFalse(atlas.Acquire(new FProbeCubeKey(1, 1), null, 1, false, 1, out _));

                atlas.Configure(100, 8);
                Assert.AreEqual(128, atlas.Resolution, "resolution rounds to a power of two");
                atlas.BeginFrame();
                Assert.IsTrue(atlas.Acquire(new FProbeCubeKey(1, 1), null, 1, false, 1, out _));
                atlas.CommitFrame();
                Assert.AreEqual(1, atlas.EntryCount);
                atlas.Configure(256, 8);
                Assert.AreEqual(0, atlas.EntryCount, "a new resolution empties the atlas");
            }
            finally
            {
                atlas.Dispose();
            }
        }
    }
}
