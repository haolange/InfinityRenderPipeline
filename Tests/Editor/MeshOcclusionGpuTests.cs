using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshOcclusionGpuTests
    {
        const int Count = 11, Width = 63, Height = 41;
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void HistoryThenCurrentOccluder_PreserveConservativeVisibility(bool padded, bool currentHole)
        {
            MeshTestUtility.RequireComputeReadback();
            bool reversed = SystemInfo.usesReversedZBuffer;
            float behind = reversed ? .2f : .8f, front = reversed ? .9f : .1f;
            var asset = MeshTestUtility.LoadMeshDrawShader();
            var shader = UnityEngine.Object.Instantiate(asset);
            var allocations = new List<GraphicsBuffer>();
            var previous = Furthest(padded, false, false);
            var current = Furthest(padded, currentHole, !currentHole);
            MeshProducerRun run = null;
            try
            {
                var centers = new Vector4[Count]; var extents = new Vector4[Count];
                var flags = new uint[Count]; var layers = new uint[Count]; var rendering = new uint[Count];
                var scenes = new Vector2Int[Count]; var generations = new uint[Count];
                var previousGenerations = new uint[Count]; var matrices = new Matrix4x4[Count]; var oldMatrices = new Matrix4x4[Count];
                for (int i = 0; i < Count; ++i)
                {
                    centers[i] = new Vector4(0, 0, behind, 0); extents[i] = new Vector4(.05f, .05f, .02f, 0);
                    flags[i] = 4; layers[i] = rendering[i] = 1; scenes[i] = new Vector2Int(-1, -1);
                    generations[i] = previousGenerations[i] = 1; matrices[i] = oldMatrices[i] = Matrix4x4.identity;
                }
                centers[1].z = front;
                centers[2].z = reversed ? .99f : .01f; extents[2].z = .03f; // Crossing the near plane stays visible.
                centers[3].x = .99f; // Partially outside the image stays visible.
                matrices[4] = Matrix4x4.Translate(Vector3.right * .25f); // Moving objects do not rely on historical bounds.
                generations[5] = 3; // Reused slot.
                flags[6] = 0; layers[7] = 0;
                previousGenerations[8] = 0; // A slot absent from the previous version has no history.
                centers[9].x = .94f; extents[9].x = .025f; // Last logical quad adjacent to poisoned padding.
                centers[10].z = reversed ? .7f : .3f; extents[10] = Vector4.zero; // Co-planar bounds must not self-occlude.
                var boundCenters = Buffer(centers, 16);
                // A small positive bound: with a missing or zero LOD scale every such entity would be LOD-culled. Entity 1 keeps
                // a zero bound (always stable) and entity 10 has zero extent, so its screen size is 0.
                var chainValues = new MeshLODChain[Count];
                for (int i = 0; i < Count; ++i) chainValues[i] = i == 1 || i == 10 ? MeshLODChain.Single : MeshLODChain.Create(new[] { 1e-3f }, 0.2f);
                var columns = new MeshEntityColumns
                {
                    center = boundCenters, extent = Buffer(extents, 16), flags = Buffer(flags, 4), layers = Buffer(layers, 4),
                    scenes = Buffer(scenes, 8), renderingLayers = Buffer(rendering, 4),
                    lodChains = Buffer(chainValues, System.Runtime.InteropServices.Marshal.SizeOf<MeshLODChain>())
                };
                run = new MeshProducerRun(shader, columns, MeshTestUtility.LevelCandidates(Count, 1), 1);
                MeshCullingContext context = Context(MeshTestUtility.Lod, 0);
                int mips = PyramidMipBatch.MipCount((Width + 1) / 2, (Height + 1) / 2);

                List<MeshDrawnInstance> plain = run.Build("BuildDrawList", context);
                CollectionAssert.AreEqual(new uint[] {1,1,1,1,1,1,0,0,1,1,1}, Drawn(plain));
                Assert.IsTrue(plain.Contains(new MeshDrawnInstance(1, 0, 0)), "A zero-bound chain draws level 0 in the stable range.");

                run.SetHistory(Buffer(matrices, 64), Buffer(generations, 4), Buffer(oldMatrices, 64), Buffer(previousGenerations, 4), Count, Count);
                run.SetOccluder(previous, Matrix4x4.identity, Width, Height, mips);
                CollectionAssert.AreEqual(new uint[] {0,1,1,1,1,1,0,0,1,0,1}, Drawn(run.Build("BuildDrawListOccludedHistory", context)));
                CollectionAssert.AreEqual(new uint[] {0,1,1,1,1,1,0,0,1,0,1}, run.DrawnCandidates(), "The occluded list records what it drew.");

                // A later list of the same context and pass draws what the history occluder hid and the current one does not.
                run.SetOccluder(current, Matrix4x4.identity, Width, Height, mips);
                var expectedRemainder = new uint[Count]; expectedRemainder[0] = 1;
                expectedRemainder[9] = currentHole ? 0u : 1u;
                CollectionAssert.AreEqual(expectedRemainder, Drawn(run.Build("BuildDrawListOccluded", context, readDrawn: true)));

                // Another pass tests its first list against the current occluder alone.
                var expectedCurrent = new uint[] {1,1,1,1,1,1,0,0,1,currentHole ? 0u : 1u,1};
                CollectionAssert.AreEqual(expectedCurrent, Drawn(run.Build("BuildDrawListOccluded", context)));

                // A projection wholly behind the camera cannot prove occlusion.
                foreach (float w in new[] { -1f, 0f })
                {
                    var invalidProjection = Matrix4x4.identity; invalidProjection.m33 = w;
                    run.SetOccluder(previous, invalidProjection, Width, Height, mips);
                    CollectionAssert.AreEqual(new uint[] {1,1,1,1,1,1,0,0,1,1,1}, Drawn(run.Build("BuildDrawListOccludedHistory", context)));
                }
                run.SetOccluder(previous, Matrix4x4.identity, Width, Height, 0);
                CollectionAssert.AreEqual(new uint[] {1,1,1,1,1,1,0,0,1,1,1}, Drawn(run.Build("BuildDrawListOccludedHistory", context)));

                // Cancellation in a translated VP must not turn a co-planar surface into an occlusion proof.
                for (int i = 0; i < Count; ++i) centers[i] += new Vector4(2048, 2048, 2048, 0);
                boundCenters.SetData(centers);
                var shifted = Matrix4x4.identity; shifted.m03 = shifted.m13 = shifted.m23 = -2048;
                run.SetOccluder(previous, shifted, Width, Height, mips);
                var shiftedLod = new MeshLODProjection { origin = new Unity.Mathematics.float3(2048, 2048, 2048), scale = 1, bias = 1 };
                CollectionAssert.AreEqual(new uint[] {0,1,1,1,1,1,0,0,1,0,1}, Drawn(run.Build("BuildDrawListOccludedHistory", Context(shiftedLod, 2048))));

                GraphicsBuffer Buffer(Array values, int stride)
                {
                    var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, values.Length, stride); allocations.Add(buffer); buffer.SetData(values); return buffer;
                }
            }
            finally
            {
                run?.Dispose();
                foreach (var buffer in allocations) buffer.Release();
                UnityEngine.Object.DestroyImmediate(previous); UnityEngine.Object.DestroyImmediate(current); UnityEngine.Object.DestroyImmediate(shader);
            }
        }

        // The clip-space box [-1, 1] x [-1, 1] x [0, 1], translated by offset on every axis.
        static MeshCullingContext Context(in MeshLODProjection lod, float offset)
        {
            var planes = new[]
            {
                new Plane(new Vector3(1, 0, 0), 1 - offset), new Plane(new Vector3(-1, 0, 0), 1 + offset),
                new Plane(new Vector3(0, 1, 0), 1 - offset), new Plane(new Vector3(0, -1, 0), 1 + offset),
                new Plane(new Vector3(0, 0, 1), -offset), new Plane(new Vector3(0, 0, -1), 1 + offset)
            };
            return MeshCullingContext.Create(planes, 1, ulong.MaxValue, 1u, false, lod);
        }

        static uint[] Drawn(List<MeshDrawnInstance> drawn) => MeshTestUtility.DrawnEntities(drawn, Count);
        static Texture2D Furthest(bool padded, bool hole, bool clear)
        {
            bool reversed = SystemInfo.usesReversedZBuffer;
            int bw = (Width + 1) / 2, bh = (Height + 1) / 2;
            int pw = padded ? 64 : bw, ph = padded ? 64 : bh;
            var texture = new Texture2D(pw, ph, TextureFormat.RFloat, true, true);
            var source = new float[Width * Height];
            Array.Fill(source, clear ? reversed ? 0 : 1 : reversed ? .7f : .3f);
            if (hole) source[(Height / 2) * Width + Width / 2] = reversed ? 0 : 1;
            int sw = Width, sh = Height;
            for (int mip = 0; mip < texture.mipmapCount; ++mip)
            {
                int dw = Math.Max(1, bw >> mip), dh = Math.Max(1, bh >> mip);
                var reduced = new float[dw * dh];
                for (int y = 0; y < dh; ++y) for (int x = 0; x < dw; ++x)
                {
                    float value = reversed ? 1 : 0;
                    int endX = x == dw - 1 ? sw : Math.Min(sw, x * 2 + 2), endY = y == dh - 1 ? sh : Math.Min(sh, y * 2 + 2);
                    for (int sy = Math.Min(y * 2, sh - 1); sy < endY; ++sy) for (int sx = Math.Min(x * 2, sw - 1); sx < endX; ++sx)
                        value = reversed ? Math.Min(value, source[sy * sw + sx]) : Math.Max(value, source[sy * sw + sx]);
                    reduced[y * dw + x] = value;
                }
                int mw = Math.Max(1, pw >> mip), mh = Math.Max(1, ph >> mip);
                var physical = new float[mw * mh]; Array.Fill(physical, reversed ? 1 : 0);
                for (int y = 0; y < dh; ++y) Array.Copy(reduced, y * dw, physical, y * mw, dw);
                texture.SetPixelData(physical, mip); source = reduced; sw = dw; sh = dh;
            }
            texture.Apply(false, false); return texture;
        }
    }
}
