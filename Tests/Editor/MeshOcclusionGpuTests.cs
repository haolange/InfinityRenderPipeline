using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshOcclusionGpuTests
    {
        const int Count = 11, Width = 63, Height = 41;
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PredictionAndRecovery_PreserveConservativeVisibility(bool padded, bool currentHole)
        {
            bool reversed = SystemInfo.usesReversedZBuffer;
            float behind = reversed ? .2f : .8f, front = reversed ? .9f : .1f;
            var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute");
            Assert.IsNotNull(asset);
            var shader = UnityEngine.Object.Instantiate(asset);
            var allocations = new List<ComputeBuffer>();
            var previous = Furthest(padded, false, false);
            var current = Furthest(padded, currentHole, !currentHole);
            try
            {
                var centers = new Vector4[Count]; var extents = new Vector4[Count];
                var flags = new uint[Count]; var layers = new uint[Count]; var rendering = new uint[Count];
                var scenes = new Vector2Int[Count]; var indices = new uint[Count]; var generations = new uint[Count];
                var previousGenerations = new uint[Count]; var matrices = new Matrix4x4[Count]; var oldMatrices = new Matrix4x4[Count];
                for (int i = 0; i < Count; ++i)
                {
                    centers[i] = new Vector4(0, 0, behind, 0); extents[i] = new Vector4(.05f, .05f, .02f, 0);
                    flags[i] = 4; layers[i] = rendering[i] = 1; scenes[i] = new Vector2Int(-1, -1); indices[i] = (uint)i;
                    generations[i] = previousGenerations[i] = 1; matrices[i] = oldMatrices[i] = Matrix4x4.identity;
                }
                centers[1].z = front;
                centers[2].z = reversed ? .99f : .01f; extents[2].z = .03f; // Crossing the near plane stays visible.
                centers[3].x = .99f; // Partially outside the image stays visible.
                matrices[4] = Matrix4x4.Translate(Vector3.right * .25f); // Moving objects do not rely on historical bounds.
                generations[5] = 2; // Reused slot.
                flags[6] = 0; layers[7] = 0;
                indices[8] = Count; // Invalid historical range must not be indexed.
                centers[9].x = .94f; extents[9].x = .025f; // Last logical quad adjacent to poisoned padding.
                centers[10].z = reversed ? .7f : .3f; extents[10] = Vector4.zero; // Co-planar bounds must not self-occlude.
                var first = Buffer(new uint[Count], 4); var deferred = Buffer(new uint[Count], 4);
                var final = Buffer(new uint[Count], 4); var recovered = Buffer(new uint[Count], 4);
                var boundCenters = Buffer(centers, 16); var boundExtents = Buffer(extents, 16);
                var boundFlags = Buffer(flags, 4); var boundLayers = Buffer(layers, 4);
                var boundRendering = Buffer(rendering, 4); var boundScenes = Buffer(scenes, 8);
                int noHistory = shader.FindKernel("CullFirstNoHistory"), history = shader.FindKernel("CullFirstWithHistory"), recovery = shader.FindKernel("CullOcclusionRecovery");
                foreach (int kernel in new[] { noHistory, history })
                {
                    shader.SetBuffer(kernel, "_InstanceBoundsCenter", boundCenters); shader.SetBuffer(kernel, "_InstanceBoundsExtent", boundExtents);
                    shader.SetBuffer(kernel, "_InstanceFlags", boundFlags); shader.SetBuffer(kernel, "_InstanceLayerMask", boundLayers);
                    shader.SetBuffer(kernel, "_InstanceRenderingLayer", boundRendering); shader.SetBuffer(kernel, "_InstanceSceneCullingMask", boundScenes);
                    shader.SetBuffer(kernel, "_Visibility", first); shader.SetBuffer(kernel, "_DeferredVisibility", deferred); shader.SetBuffer(kernel, "_FinalVisibility", final);
                }
                shader.SetInt("_InstanceCount", Count); shader.SetInt("_ViewLayerMask", 1);
                shader.SetInt("_FilterRenderingLayers", 0); shader.SetInt("_ViewRenderingLayerMask", 1); shader.SetInt("_ViewExcludeFlags", 0);
                shader.SetInt("_ViewSceneCullingMaskLow", -1); shader.SetInt("_ViewSceneCullingMaskHigh", -1);
                shader.SetVectorArray("_FrustumPlanes", new[] {new Vector4(1,0,0,1),new Vector4(-1,0,0,1),new Vector4(0,1,0,1),new Vector4(0,-1,0,1),new Vector4(0,0,1,0),new Vector4(0,0,-1,1)});
                // Intentionally no history texture, matrix or generation bindings for this kernel.
                shader.Dispatch(noHistory, 1, 1, 1);
                CollectionAssert.AreEqual(new uint[] {1,1,1,1,1,1,0,0,1,1,1}, Read(first));
                CollectionAssert.AreEqual(new uint[Count], Read(deferred));
                shader.SetBuffer(history, "_InstanceToTransform", Buffer(indices, 4));
                shader.SetBuffer(history, "_MeshCullCurrentTransforms", Buffer(matrices, 64));
                shader.SetBuffer(history, "_MeshCullPreviousTransforms", Buffer(oldMatrices, 64));
                shader.SetBuffer(history, "_MeshCullCurrentGenerations", Buffer(generations, 4));
                shader.SetBuffer(history, "_MeshCullPreviousGenerations", Buffer(previousGenerations, 4));
                shader.SetInt("_MeshCullCurrentCount", Count); shader.SetInt("_MeshCullPreviousCount", Count);
                shader.SetMatrix("_MeshCullProjection", Matrix4x4.identity);
                shader.SetVector("_MeshCullFullExtent", new Vector4(Width, Height, 0, 0));
                shader.SetInt("_MeshCullMipCount", PyramidMipBatch.MipCount((Width + 1) / 2, (Height + 1) / 2));
                shader.SetTexture(history, "_MeshCullFurthest", previous);
                shader.Dispatch(history, 1, 1, 1);
                CollectionAssert.AreEqual(new uint[] {0,1,1,1,1,1,0,0,1,0,1}, Read(first));
                CollectionAssert.AreEqual(new uint[] {1,0,0,0,0,0,0,0,0,1,0}, Read(deferred));
                shader.SetBuffer(recovery, "_InstanceBoundsCenter", boundCenters); shader.SetBuffer(recovery, "_InstanceBoundsExtent", boundExtents);
                shader.SetBuffer(recovery, "_OcclusionDeferred", deferred); shader.SetBuffer(recovery, "_RecoveredVisibility", recovered);
                shader.SetBuffer(recovery, "_FinalVisibility", final); shader.SetTexture(recovery, "_MeshCullFurthest", current);
                shader.Dispatch(recovery, 1, 1, 1);
                var expectedRecovered = new uint[Count]; expectedRecovered[0] = 1;
                expectedRecovered[9] = currentHole ? 0u : 1u;
                CollectionAssert.AreEqual(expectedRecovered, Read(recovered));
                var expectedFinal = new uint[] {1,1,1,1,1,1,0,0,1,currentHole ? 0u : 1u,1};
                CollectionAssert.AreEqual(expectedFinal, Read(final));
                // A projection wholly behind the camera cannot prove occlusion.
                foreach (float w in new[] { -1f, 0f })
                {
                    var invalidProjection = Matrix4x4.identity; invalidProjection.m33 = w;
                    shader.SetMatrix("_MeshCullProjection", invalidProjection);
                    shader.Dispatch(history, 1, 1, 1);
                    CollectionAssert.AreEqual(new uint[] {1,1,1,1,1,1,0,0,1,1,1}, Read(first));
                    CollectionAssert.AreEqual(new uint[Count], Read(deferred));
                }
                shader.SetMatrix("_MeshCullProjection", Matrix4x4.identity);
                shader.SetInt("_MeshCullMipCount", 0);
                shader.Dispatch(history, 1, 1, 1);
                CollectionAssert.AreEqual(new uint[] {1,1,1,1,1,1,0,0,1,1,1}, Read(first));
                CollectionAssert.AreEqual(new uint[Count], Read(deferred));
                shader.SetInt("_MeshCullMipCount", PyramidMipBatch.MipCount((Width + 1) / 2, (Height + 1) / 2));
                // Cancellation in a translated VP must not turn a co-planar surface into an occlusion proof.
                for (int i = 0; i < Count; ++i) centers[i] += new Vector4(2048, 2048, 2048, 0);
                boundCenters.SetData(centers);
                var shifted = Matrix4x4.identity; shifted.m03 = shifted.m13 = shifted.m23 = -2048;
                shader.SetMatrix("_MeshCullProjection", shifted);
                shader.SetVectorArray("_FrustumPlanes", new[] {new Vector4(1,0,0,-2047),new Vector4(-1,0,0,2049),new Vector4(0,1,0,-2047),new Vector4(0,-1,0,2049),new Vector4(0,0,1,-2048),new Vector4(0,0,-1,2049)});
                shader.Dispatch(history, 1, 1, 1);
                CollectionAssert.AreEqual(new uint[] {0,1,1,1,1,1,0,0,1,0,1}, Read(first));
                CollectionAssert.AreEqual(new uint[] {1,0,0,0,0,0,0,0,0,1,0}, Read(deferred));

                ComputeBuffer Buffer(Array values, int stride)
                {
                    var buffer = new ComputeBuffer(values.Length, stride); allocations.Add(buffer); buffer.SetData(values); return buffer;
                }
            }
            finally
            {
                foreach (var buffer in allocations) buffer.Release();
                UnityEngine.Object.DestroyImmediate(previous); UnityEngine.Object.DestroyImmediate(current); UnityEngine.Object.DestroyImmediate(shader);
            }
        }
        static uint[] Read(ComputeBuffer buffer)
        {
            var read = AsyncGPUReadback.Request(buffer); read.WaitForCompletion(); Assert.IsFalse(read.hasError);
            return read.GetData<uint>().ToArray();
        }
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
