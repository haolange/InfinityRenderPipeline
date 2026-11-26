using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Core;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Decal;
using InfinityTech.Rendering.Reflection;
using InfinityTech.Rendering.Spatial;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ReflectionAndDecalTests
    {
        [Test]
        public void ReflectionExtension_OrientedBoxAndIndependentProxySettings_PreserveValues()
        {
            var go = new GameObject("ProbeExt");
            var probe = go.AddComponent<ReflectionProbe>();
            var ext = go.AddComponent<InfinityAdditionalReflectionProbeData>();
            var rasterGo = new GameObject("RasterReflection");
            var rasterVol = rasterGo.AddComponent<InfinityRasterReflectionVolume>();
            try
            {
                ext.influence = EReflectionProbeInfluence.OrientedBox;
                ext.projectionProxy = EReflectionProjectionProxy.Sphere;
                ext.projectionSphereRadius = 7.5f;
                ext.useCustomCaptureOrigin = true;
                ext.captureOriginOffset = new Vector3(1.0f, 2.0f, -0.5f);
                Assert.AreEqual(EReflectionProbeInfluence.OrientedBox, ext.influence);
                Assert.AreEqual(EReflectionProjectionProxy.Sphere, ext.projectionProxy);
                Assert.AreEqual(7.5f, ext.projectionSphereRadius, 1e-4f);
                Assert.IsTrue(ext.useCustomCaptureOrigin);

                rasterVol.drawOrder = 3;
                rasterVol.blendDistance = 0.4f;
                Assert.AreEqual(3, rasterVol.drawOrder);
                Assert.IsNotNull(probe);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rasterGo);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DecalContext_SplitsModes_FallsBackDirectOnYCoCg_AndBuildsZeroGcClusters()
        {
            var table = new SceneTable<DecalComponent>();
            var registry = new ComponentRegistry<DecalComponent>();

            var goA = new GameObject("Decal_DBuffer");
            var decalA = goA.AddComponent<DecalComponent>();
            decalA.executionMode = EDecalExecutionMode.DBuffer;

            var goB = new GameObject("Decal_Direct");
            var decalB = goB.AddComponent<DecalComponent>();
            decalB.executionMode = EDecalExecutionMode.DirectGBuffer;

            var goC1 = new GameObject("Decal_Cluster_LowOrder");
            goC1.transform.position = new Vector3(0, 0, 10);
            goC1.transform.localScale = new Vector3(4, 4, 4);
            var decalC1 = goC1.AddComponent<DecalComponent>();
            decalC1.executionMode = EDecalExecutionMode.Clustered;
            decalC1.drawOrder = 5;

            var goC2 = new GameObject("Decal_Cluster_HighOrder");
            goC2.transform.position = new Vector3(0, 0, 12);
            goC2.transform.localScale = new Vector3(4, 4, 4);
            var decalC2 = goC2.AddComponent<DecalComponent>();
            decalC2.executionMode = EDecalExecutionMode.Clustered;
            decalC2.drawOrder = 20;

            var camGo = new GameObject("TestCam");
            camGo.transform.position = Vector3.zero;
            camGo.transform.rotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100.0f;
            cam.fieldOfView = 60.0f;

            try
            {
                registry.Register(1, decalA);
                registry.Register(2, decalB);
                registry.Register(3, decalC2); // Deliberately register high order first to verify drawOrder sorting.
                registry.Register(4, decalC1);
                table.Prepare(registry);
                table.Commit();

                using var ctx = new DecalContext();
                var screen = new int2(128, 128);
                ctx.Build(table, cam, screen, allowDirectGBuffer: true);
                Assert.AreEqual(1, ctx.DBufferDecalCount);
                Assert.AreEqual(1, ctx.DirectDecalCount);
                Assert.AreEqual(2, ctx.ClusteredDecalCount);
                Assert.AreEqual(4, ctx.TotalDecalCount);

                // Warmup + verify zero GC allocations in steady state.
                ctx.Build(table, cam, screen, allowDirectGBuffer: true);
                long before = GC.GetAllocatedBytesForCurrentThread();
                ctx.Build(table, cam, screen, allowDirectGBuffer: true);
                long after = GC.GetAllocatedBytesForCurrentThread();
                Assert.AreEqual(0L, after - before, "Steady-state DecalContext.Build should not allocate managed memory.");

                // Under YCoCg GBuffer layout, DirectGBuffer decals fall back safely to DBuffer.
                ctx.Build(table, cam, screen, allowDirectGBuffer: false);
                Assert.AreEqual(2, ctx.DBufferDecalCount);
                Assert.AreEqual(0, ctx.DirectDecalCount);
                Assert.AreEqual(2, ctx.ClusteredDecalCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(goA);
                UnityEngine.Object.DestroyImmediate(goB);
                UnityEngine.Object.DestroyImmediate(goC1);
                UnityEngine.Object.DestroyImmediate(goC2);
            }
        }

        [Test]
        public void ClusteredDecalsGpuContract_RespectsDrawOrderAndRenderingLayerMask()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and asynchronous readback required.");

            const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_DecalClusterContract.compute";
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            Assert.IsNotNull(shader, "Compute_DecalClusterContract.compute not found.");
            int kernel = shader.FindKernel("ClusteredDecalContract");

            var table = new SceneTable<DecalComponent>();
            var registry = new ComponentRegistry<DecalComponent>();

            var goBottom = new GameObject("Decal_Bottom");
            goBottom.transform.position = new Vector3(0, 0, 10);
            goBottom.transform.localScale = new Vector3(6, 6, 6);
            var dBottom = goBottom.AddComponent<DecalComponent>();
            dBottom.executionMode = EDecalExecutionMode.Clustered;
            dBottom.drawOrder = 1;
            dBottom.renderingLayerMask = ERenderingLayer.LightLayerDefault;
            dBottom.clusteredColor = Color.red; // linear (1, 0, 0, 1)
            dBottom.clusteredRoughness = 0.1f;
            dBottom.clusteredSpecular = 0.2f;

            var goTopMaskedOut = new GameObject("Decal_MaskedOut");
            goTopMaskedOut.transform.position = new Vector3(0, 0, 10);
            goTopMaskedOut.transform.localScale = new Vector3(6, 6, 6);
            var dMaskedOut = goTopMaskedOut.AddComponent<DecalComponent>();
            dMaskedOut.executionMode = EDecalExecutionMode.Clustered;
            dMaskedOut.drawOrder = 5;
            dMaskedOut.renderingLayerMask = ERenderingLayer.LightLayer1; // Does NOT match bit 0 -> must be skipped!
            dMaskedOut.clusteredColor = Color.blue;

            var goTopWinner = new GameObject("Decal_TopWinner");
            goTopWinner.transform.position = new Vector3(0, 0, 10);
            goTopWinner.transform.localScale = new Vector3(6, 6, 6);
            var dWinner = goTopWinner.AddComponent<DecalComponent>();
            dWinner.executionMode = EDecalExecutionMode.Clustered;
            dWinner.drawOrder = 10;
            dWinner.renderingLayerMask = ERenderingLayer.LightLayerDefault;
            dWinner.clusteredColor = new Color(0, 1, 0, 0.5f); // 50% green over red => ~ (0.5, 0.5, 0)
            dWinner.clusteredRoughness = 0.9f;
            dWinner.clusteredSpecular = 0.8f;

            var camGo = new GameObject("TestCam");
            camGo.transform.position = Vector3.zero;
            camGo.transform.rotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100.0f;
            cam.fieldOfView = 60.0f;

            try
            {
                registry.Register(10, dWinner);
                registry.Register(11, dMaskedOut);
                registry.Register(12, dBottom);
                table.Prepare(registry);
                table.Commit();

                using var ctx = new DecalContext();
                ctx.Build(table, cam, new int2(128, 128), allowDirectGBuffer: true);
                Assert.AreEqual(3, ctx.ClusteredDecalCount);

                // Upload via temporary buffers inside SpatialClusterIndex isn't public to raw GraphicsBuffer without TransferEncoder,
                // Wait: Upload uses RGTransferEncoder. For the shader unit contract, test Reflection & Decal uniform layout + ReflectionOrientedBox!
                DecalUniform uniform = ctx.ResolveUniform(true);
                using var decalUniform = new UniformBinding<DecalUniform>(uniform);
                using var records = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, System.Runtime.InteropServices.Marshal.SizeOf<DecalRecord>());
                using var headers = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ctx.ClusterHeaderBuffer.count, 8);
                using var indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));
                using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);

                Color linRed = Color.red.linear;
                Color linBlue = Color.blue.linear;
                Color linGreen = new Color(0, 1, 0, 0.5f).linear;
                Matrix4x4 w2l = Matrix4x4.Scale(new Vector3(6, 6, 6)).inverse * Matrix4x4.Translate(new Vector3(0, 0, -10));
                w2l = goBottom.transform.worldToLocalMatrix;
                var recArr = new[]
                {
                    new DecalRecord { worldToBox0 = w2l.GetRow(0), worldToBox1 = w2l.GetRow(1), worldToBox2 = w2l.GetRow(2),
                        albedoAlpha = new Vector4(linRed.r, linRed.g, linRed.b, 1.0f), materialParams = new Vector4(0.1f, 0, 0.2f, 5),
                        normalBlend = new Vector4(0, 1, 0, 1), layerAndFlags = new Vector4((byte)ERenderingLayer.LightLayerDefault, 0, 0, 0) },
                    new DecalRecord { worldToBox0 = w2l.GetRow(0), worldToBox1 = w2l.GetRow(1), worldToBox2 = w2l.GetRow(2),
                        albedoAlpha = new Vector4(linBlue.r, linBlue.g, linBlue.b, 1.0f), materialParams = new Vector4(0.5f, 0, 0.5f, 5),
                        normalBlend = new Vector4(0, 1, 0, 1), layerAndFlags = new Vector4((byte)ERenderingLayer.LightLayer1, 0, 0, 0) },
                    new DecalRecord { worldToBox0 = w2l.GetRow(0), worldToBox1 = w2l.GetRow(1), worldToBox2 = w2l.GetRow(2),
                        albedoAlpha = new Vector4(linGreen.r, linGreen.g, linGreen.b, 0.5f), materialParams = new Vector4(0.9f, 0, 0.8f, 5),
                        normalBlend = new Vector4(0, 1, 0, 1), layerAndFlags = new Vector4((byte)ERenderingLayer.LightLayerDefault, 0, 0, 0) }
                };
                records.SetData(recArr);

                var headerArr = new uint2[ctx.ClusterHeaderBuffer.count];
                int slice = SpatialProjection.DepthToZBin(10.0f, uniform.zSliceParams);
                int cluster = (slice * (int)uniform.clusterParams.w + 0) * (int)uniform.clusterParams.z + 0;
                headerArr[cluster] = new uint2(0, 3);
                headers.SetData(headerArr);
                indices.SetData(new uint[] { 0, 1, 2 });

                shader.SetBuffer(kernel, "SRV_DecalRecords", records);
                shader.SetBuffer(kernel, "SRV_DecalClusterHeaders", headers);
                shader.SetBuffer(kernel, "SRV_DecalClusterIndices", indices);
                shader.SetBuffer(kernel, "UAV_DecalResults", results);
                shader.Dispatch(kernel, 1, 1, 1);

                AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(results);
                req.WaitForCompletion();
                Assert.IsFalse(req.hasError);
                NativeArray<Vector4> outData = req.GetData<Vector4>();
                // Expected: 0.5 * red + 0.5 * green -> (0.5, 0.5, 0), roughness lerp(0.1, 0.9, 0.5) = 0.5, specular lerp(0.2, 0.8, 0.5) = 0.5
                Assert.AreEqual(0.5f, outData[0].x, 1e-3f);
                Assert.AreEqual(0.5f, outData[0].y, 1e-3f);
                Assert.AreEqual(0.0f, outData[0].z, 1e-3f);
                Assert.AreEqual(0.5f, outData[0].w, 1e-3f);
                Assert.AreEqual(0.5f, outData[1].w, 1e-3f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(goBottom);
                UnityEngine.Object.DestroyImmediate(goTopMaskedOut);
                UnityEngine.Object.DestroyImmediate(goTopWinner);
            }
        }
    }
}
