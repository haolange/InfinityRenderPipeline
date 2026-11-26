using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Spatial;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class SpatialFoundationTests
    {
        [Test]
        public void Volume_BoundsAndSdf_MatchAnalyticalExpectations()
        {
            var sphere = SpatialVolume.CreateSphere(new float3(1, 2, 3), 2.5f);
            Assert.Less(sphere.EvaluateSignedDistance(new float3(1, 2, 3)), -2.4f);
            Assert.AreEqual(0.0f, sphere.EvaluateSignedDistance(new float3(3.5f, 2, 3)), 1e-4f);
            Assert.Greater(sphere.EvaluateSignedDistance(new float3(5, 2, 3)), 1.4f);
            Assert.AreEqual(1.0f, sphere.EvaluateInfluenceWeight(new float3(1, 2, 3), 1.0f), 1e-4f);
            Assert.AreEqual(0.5f, sphere.EvaluateInfluenceWeight(new float3(3.0f, 2, 3), 1.0f), 1e-4f);
            Assert.AreEqual(0.0f, sphere.EvaluateInfluenceWeight(new float3(4.0f, 2, 3), 1.0f), 1e-4f);

            var rot = quaternion.RotateY(math.radians(45.0f));
            var box = SpatialVolume.CreateBox(float3.zero, rot, new float3(2, 1, 2));
            SpatialBounds boxBounds = box.ComputeWorldBounds();
            float expectedXZ = math.sqrt(2.0f) * 2.0f;
            Assert.AreEqual(expectedXZ, boxBounds.halfExtents.x, 1e-4f);
            Assert.AreEqual(1.0f, boxBounds.halfExtents.y, 1e-4f);
            Assert.AreEqual(expectedXZ, boxBounds.halfExtents.z, 1e-4f);

            var rounded = SpatialVolume.CreateRoundedBox(float3.zero, quaternion.identity, new float3(3, 3, 3), 1.0f);
            Assert.AreEqual(0.0f, rounded.EvaluateSignedDistance(new float3(3, 0, 0)), 1e-4f);
            Assert.Greater(rounded.EvaluateSignedDistance(new float3(3, 3, 3)), 0.0f);

            var capsule = SpatialVolume.CreateCapsule(float3.zero, quaternion.identity, 2.0f, 0.5f);
            Assert.AreEqual(0.0f, capsule.EvaluateSignedDistance(new float3(0, 2.5f, 0)), 1e-4f);
            Assert.AreEqual(0.0f, capsule.EvaluateSignedDistance(new float3(0.5f, 1.0f, 0)), 1e-4f);

            var ellipsoid = SpatialVolume.CreateEllipsoid(float3.zero, quaternion.identity, new float3(4, 2, 1));
            Assert.Less(ellipsoid.EvaluateSignedDistance(float3.zero), 0.0f);
            Assert.AreEqual(0.0f, ellipsoid.EvaluateSignedDistance(new float3(4, 0, 0)), 1e-3f);
            Assert.Greater(ellipsoid.EvaluateSignedDistance(new float3(0, 0, 2)), 0.0f);

            Assert.IsTrue(SpatialVolume.TryRayIntersectBox(new float3(1, 0, 0), new float3(1, 0, 0), new float3(4, 2, 2), out float boxExit));
            Assert.AreEqual(3.0f, boxExit, 1e-4f);
            Assert.IsTrue(SpatialVolume.TryRayIntersectSphere(new float3(0, 1, 0), new float3(0, 1, 0), 3.0f, out float sphereExit));
            Assert.AreEqual(2.0f, sphereExit, 1e-4f);
        }

        [Test]
        public void Projection_ZBinsAndBounds_ProjectDeterministically()
        {
            Vector4 zBins = SpatialProjection.ComputeZBinParams(0.1f, 100.0f, 128);
            var bins = new NativeArray<uint>(128, Allocator.Temp);
            var ranges = new NativeArray<float2>(2, Allocator.Temp);
            ranges[0] = new float2(2.0f, 5.0f);
            ranges[1] = new float2(4.0f, 12.0f);
            SpatialZBinTileIndex.BuildZBins(ranges, 2, zBins, bins);

            int binInside0Only = SpatialProjection.DepthToZBin(2.5f, zBins);
            SpatialProjection.UnpackZBin(bins[binInside0Only], out int min0, out int max0);
            Assert.AreEqual(0, min0);
            Assert.AreEqual(0, max0);

            int binOverlap = SpatialProjection.DepthToZBin(4.5f, zBins);
            SpatialProjection.UnpackZBin(bins[binOverlap], out int minOverlap, out int maxOverlap);
            Assert.AreEqual(0, minOverlap);
            Assert.AreEqual(1, maxOverlap);

            ranges.Dispose();
            bins.Dispose();

            Matrix4x4 worldToView = Matrix4x4.identity;
            Matrix4x4 gpuProj = Matrix4x4.Perspective(60.0f, 1.0f, 0.1f, 100.0f);
            var centerBounds = new SpatialBounds(new float3(0, 0, -10), new float3(1, 1, 1));
            Assert.IsTrue(SpatialProjection.TryProjectBoundsToTileRect(in centerBounds, in worldToView, in gpuProj,
                false, 0.1f, new int2(256, 256), 64, out int4 rect, out float2 depthRange));
            Assert.AreEqual(9.0f, depthRange.x, 1e-3f);
            Assert.AreEqual(11.0f, depthRange.y, 1e-3f);
            Assert.LessOrEqual(rect.x, rect.z);
            Assert.LessOrEqual(rect.y, rect.w);

            var behindCamera = new SpatialBounds(new float3(0, 0, 5), new float3(1, 1, 1));
            Assert.IsFalse(SpatialProjection.TryProjectBoundsToTileRect(in behindCamera, in worldToView, in gpuProj,
                false, 0.1f, new int2(256, 256), 64, out _, out _));
        }

        [Test]
        public void ClusterIndex_PreservesPriorityOrder_AndZeroSteadyStateAllocations()
        {
            using var index = new SpatialClusterIndex();
            var screen = new int2(128, 128);

            // Warmup build ensures internal capacity is already steady before measuring GC allocations.
            for (int w = 0; w < 2; ++w)
            {
                index.BeginBuild(screen, 0.1f, 100.0f, 64, 16);
                // Priority 0 is farther than Priority 1 & 2 to prove priority order is decoupled from view depth.
                index.AddItem(new int4(0, 0, 0, 0), new float2(18.0f, 22.0f));
                index.AddItem(new int4(0, 0, 0, 0), new float2(2.0f, 25.0f));
                index.AddItem(new int4(0, 0, 0, 0), new float2(8.0f, 12.0f));
                index.FinalizeBuild();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            index.BeginBuild(screen, 0.1f, 100.0f, 64, 16);
            index.AddItem(new int4(0, 0, 0, 0), new float2(18.0f, 22.0f));
            index.AddItem(new int4(0, 0, 0, 0), new float2(2.0f, 25.0f));
            index.AddItem(new int4(0, 0, 0, 0), new float2(8.0f, 12.0f));
            index.FinalizeBuild();
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0L, after - before, "Steady-state cluster rebuild should not allocate managed memory.");

            int targetSlice = SpatialProjection.DepthToZBin(10.0f, index.Grid.zSliceParams);
            int cluster = (targetSlice * index.Grid.tileCount.y + 0) * index.Grid.tileCount.x + 0;
            uint2 header = index.Headers[cluster];
            Assert.AreEqual(2u, header.y);
            Assert.AreEqual(1u, index.Indices[(int)header.x]);
            Assert.AreEqual(2u, index.Indices[(int)header.x + 1]);
        }

        [Test]
        public void ClusterListGpuContract_ReadsItemsInExactPriorityOrder()
        {
            const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_SpatialClusterContract.compute";
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            Assert.IsNotNull(shader, "Compute_SpatialClusterContract.compute not found.");
            int kernel = shader.FindKernel("ClusterPriorityOrder");

            using var index = new SpatialClusterIndex();
            var screen = new int2(128, 128);
            index.BeginBuild(screen, 0.1f, 100.0f, 64, 16);
            // Item 0 (farther depth) should still precede Item 1 & Item 2 inside overlapping slice at depth 10.
            index.AddItem(new int4(0, 0, 1, 1), new float2(9.0f, 20.0f));
            index.AddItem(new int4(1, 1, 1, 1), new float2(1.0f, 12.0f)); // off tile (0,0)
            index.AddItem(new int4(0, 0, 1, 1), new float2(4.0f, 12.0f));
            index.FinalizeBuild();

            index.HeaderBuffer.SetData(index.Headers);
            index.IndexBuffer.SetData(index.Indices);

            using var resultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));
            shader.SetVector("_ClusterParams", index.Grid.clusterParams);
            shader.SetVector("_ZSliceParams", index.Grid.zSliceParams);
            shader.SetInt("_ItemCount", index.ItemCount);
            shader.SetBuffer(kernel, "SRV_ClusterHeaders", index.HeaderBuffer);
            shader.SetBuffer(kernel, "SRV_ClusterIndices", index.IndexBuffer);
            shader.SetBuffer(kernel, "UAV_Results", resultBuffer);
            shader.Dispatch(kernel, 1, 1, 1);

            AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(resultBuffer);
            req.WaitForCompletion();
            Assert.IsFalse(req.hasError);
            NativeArray<uint> data = req.GetData<uint>();
            Assert.AreEqual(2u, data[0]);
            Assert.AreEqual(1u | (3u << 8), data[1]);
            Assert.AreEqual(4.0f, math.asfloat(data[2]), 1e-4f);
            Assert.AreEqual(3.0f, math.asfloat(data[3]), 1e-4f);
        }
    }
}
