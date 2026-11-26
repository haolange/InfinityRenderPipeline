using System;
using System.Collections.Generic;
using InfinityTech.Core;
using InfinityTech.Rendering.AnalyticShadow;
using InfinityTech.Rendering.Spatial;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Tests
{
    public sealed class AnalyticShadowTests
    {
        [Test]
        public void SpatialBounds_SeparatesGeometryIndirectAndLightExtrudedDirectBounds()
        {
            var go = new GameObject("AnalyticBoundsProbe");
            try
            {
                go.transform.position = new Vector3(0.0f, 2.0f, 5.0f);
                var occ = go.AddComponent<InfinityAnalyticOccluder>();
                occ.shape = ESpatialVolumeKind.Sphere;
                occ.radius = 0.5f;
                occ.indirectFalloffDistance = 2.0f;
                occ.directSoftness = 0.1f;
                occ.directMaxDistance = 10.0f;

                SpatialBounds geom = occ.ComputeGeometryBounds();
                SpatialBounds indirect = occ.ComputeIndirectInfluenceBounds();
                // Directional light shining downward along -Y (toLightDir = +Y)
                SpatialBounds direct = InfinityAnalyticOccluder.ComputeDirectShadowBounds(in geom, new float3(0.0f, 1.0f, 0.0f), occ.directSoftness, occ.directMaxDistance);

                Assert.AreEqual(0.5f, geom.halfExtents.x, 1e-4f);
                Assert.AreEqual(2.5f, indirect.halfExtents.x, 1e-4f);
                // Direct shadow must extend downward along -Y all the way past y = 2 - 10 = -8, far outside geometry bounds
                float directMinY = direct.center.y - direct.halfExtents.y;
                float geomMinY = geom.center.y - geom.halfExtents.y;
                Assert.Less(directMinY, geomMinY - 8.0f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnalyticShadowContext_BuildIsZeroGC_InSteadyState()
        {
            var camGo = new GameObject("TestCam");
            var occGo = new GameObject("OccluderSphere");
            var capGo = new GameObject("OccluderCapsule");
            var table = new SceneTable<InfinityAnalyticOccluder>();
            var ctx = new AnalyticShadowContext();
            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.position = Vector3.zero;
                cam.transform.rotation = Quaternion.identity;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100.0f;
                cam.fieldOfView = 60.0f;

                occGo.transform.position = new Vector3(0.0f, 1.5f, 8.0f);
                var s = occGo.AddComponent<InfinityAnalyticOccluder>();
                s.shape = ESpatialVolumeKind.Sphere;
                s.radius = 0.8f;

                capGo.transform.position = new Vector3(1.5f, 1.5f, 8.0f);
                var c = capGo.AddComponent<InfinityAnalyticOccluder>();
                c.shape = ESpatialVolumeKind.Capsule;
                c.radius = 0.4f;
                c.halfExtents = new Vector3(0.4f, 1.0f, 0.4f);

                table.Prepare(InfinityAnalyticOccluder.s_Registry);
                table.Commit();

                var lights = new List<AnalyticShadowLight>
                {
                    new AnalyticShadowLight
                    {
                        isDirectional = true,
                        toLightDirWS = math.normalize(new float3(0.0f, 1.0f, 0.0f)),
                        angularRadius = 0.02f
                    }
                };

                // Warm up once to allocate internal native/managed capacities.
                ctx.Build(table, cam, new int2(512, 512), lights, true, true);
                Assert.AreEqual(2, ctx.OccluderCount);
                Assert.Greater(ctx.Stats.indirectClusterEntries, 0);
                Assert.Greater(ctx.Stats.directClusterEntries, 0);

                Recorder alloc = Recorder.Get("GC.Alloc");
                alloc.enabled = true;
                ctx.Build(table, cam, new int2(512, 512), lights, true, true);
                alloc.enabled = false;
                Assert.AreEqual(0, alloc.sampleBlockCount);
            }
            finally
            {
                ctx.Dispose();
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(occGo);
                UnityEngine.Object.DestroyImmediate(capGo);
            }
        }

        [Test]
        public void ComputeAnalyticShadowContract_SeparatesIndirectOcclusionAndDirectSoftShadow_OnGpu()
        {
            ComputeShader cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_AnalyticShadowContract.compute");
            Assert.IsNotNull(cs);
            int kernel = cs.FindKernel("EvaluateContract");
            Assert.GreaterOrEqual(kernel, 0);

            var camGo = new GameObject("GpuTestCam");
            var sphereGo = new GameObject("SphereOcc");
            var capGo = new GameObject("CapOcc");
            var boxGo = new GameObject("BoxOcc");
            var ellipGo = new GameObject("EllipOcc");
            var table = new SceneTable<InfinityAnalyticOccluder>();
            var ctx = new AnalyticShadowContext();
            var uniformBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, System.Runtime.InteropServices.Marshal.SizeOf<AnalyticShadowUniform>());
            var resultBuffer = new ComputeBuffer(8, sizeof(float) * 4);

            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.position = Vector3.zero;
                cam.transform.rotation = Quaternion.identity;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100.0f;
                cam.fieldOfView = 60.0f;

                // Sphere directly overhead of (0, 0, 10) at y = 1.6, r = 0.8 (indirectFalloff=2, directMaxDistance=12)
                sphereGo.transform.position = new Vector3(0.0f, 1.6f, 10.0f);
                var sphere = sphereGo.AddComponent<InfinityAnalyticOccluder>();
                sphere.shape = ESpatialVolumeKind.Sphere;
                sphere.radius = 0.8f;
                sphere.indirectFalloffDistance = 2.0f;
                sphere.directMaxDistance = 12.0f;
                sphere.directSoftness = 0.05f;
                sphere.renderingLayerMask = ERenderingLayer.LightLayerDefault;

                // Capsule hovering above (4, 0, 10) at y = 6.0 (far from other occluders, outside indirect falloff 2m, inside direct shadow 12m!)
                capGo.transform.position = new Vector3(4.0f, 6.0f, 10.0f);
                var cap = capGo.AddComponent<InfinityAnalyticOccluder>();
                cap.shape = ESpatialVolumeKind.Capsule;
                cap.radius = 0.6f;
                cap.halfExtents = new Vector3(0.6f, 1.0f, 0.6f);
                cap.indirectFalloffDistance = 2.0f;
                cap.directMaxDistance = 12.0f;
                cap.directSoftness = 0.05f;
                cap.renderingLayerMask = ERenderingLayer.Everything;

                // Box above (-4, 0, 10) at y = 1.5
                boxGo.transform.position = new Vector3(-4.0f, 1.5f, 10.0f);
                var box = boxGo.AddComponent<InfinityAnalyticOccluder>();
                box.shape = ESpatialVolumeKind.Box;
                box.halfExtents = new Vector3(0.75f, 0.75f, 0.75f);
                box.indirectFalloffDistance = 2.5f;
                box.directMaxDistance = 12.0f;
                box.directSoftness = 0.05f;

                // Ellipsoid above (0, 0, 14) at y = 1.3
                ellipGo.transform.position = new Vector3(0.0f, 1.3f, 14.0f);
                var ellip = ellipGo.AddComponent<InfinityAnalyticOccluder>();
                ellip.shape = ESpatialVolumeKind.Ellipsoid;
                ellip.halfExtents = new Vector3(1.0f, 0.5f, 0.8f);
                ellip.indirectFalloffDistance = 2.5f;
                ellip.directMaxDistance = 12.0f;
                ellip.directSoftness = 0.05f;

                table.Prepare(InfinityAnalyticOccluder.s_Registry);
                table.Commit();

                var lights = new List<AnalyticShadowLight>
                {
                    new AnalyticShadowLight
                    {
                        isDirectional = true,
                        toLightDirWS = new float3(0.0f, 1.0f, 0.0f),
                        angularRadius = 0.01f
                    }
                };

                const int width = 512;
                const int height = 512;
                ctx.Build(table, cam, new int2(width, height), lights, true, true);
                Assert.AreEqual(4, ctx.OccluderCount);

                AnalyticShadowUniform uniform = ctx.ResolveUniform(true);
                uniformBuffer.SetData(new[] { uniform });

                ctx.RecordBuffer.SetData(ctx.PrepareRecordUploadArray(), 0, 0, ctx.OccluderCount);
                ctx.IndirectHeaderBuffer.SetData(ctx.IndirectHeaders);
                ctx.IndirectIndexBuffer.SetData(ctx.IndirectIndices);
                ctx.DirectHeaderBuffer.SetData(ctx.DirectHeaders);
                ctx.DirectIndexBuffer.SetData(ctx.DirectIndices);

                Matrix4x4 gpuProj = GraphicsConventions.GetComputeGpuProjection(cam);
                Matrix4x4 viewProj = gpuProj * cam.worldToCameraMatrix;
                Vector4 ProjectPixel(Vector3 posWS)
                {
                    Vector4 clip = viewProj * new Vector4(posWS.x, posWS.y, posWS.z, 1.0f);
                    Vector2 uv = clip.w > 1e-4f
                        ? new Vector2(clip.x / clip.w * 0.5f + 0.5f, clip.y / clip.w * 0.5f + 0.5f)
                        : Vector2.zero;
                    return new Vector4(Mathf.Clamp(uv.x * width, 0, width - 1), Mathf.Clamp(uv.y * height, 0, height - 1), 1.0f, 0.0f);
                }

                var points = new[]
                {
                    new Vector4(0.0f, 0.0f, 10.0f, 10.0f),   // 0: under Sphere (indirect occluded & direct shadowed)
                    new Vector4(0.0f, 0.0f, 10.0f, 10.0f),   // 1: normal facing DOWN -> indirect unoccluded, direct still shadowed!
                    new Vector4(4.0f, 0.0f, 10.0f, 10.0f),   // 2: 6m below Capsule (outside indirect falloff -> indirect=1, direct shadowed!)
                    new Vector4(-4.0f, 0.0f, 10.0f, 10.0f),  // 3: under Box
                    new Vector4(0.0f, 0.0f, 14.0f, 14.0f),   // 4: under Ellipsoid
                    new Vector4(0.0f, 0.0f, 10.0f, 10.0f),   // 5: non-matching layer under Sphere -> both 1.0
                    new Vector4(20.0f, 0.0f, 10.0f, 10.0f),  // 6: far away -> both 1.0
                    new Vector4(0.0f, 0.0f, 10.0f, 10.0f),   // 7: duplicate check
                };
                var normals = new[]
                {
                    new Vector4(0, 1, 0, 0),
                    new Vector4(0, -1, 0, 0),
                    new Vector4(0, 1, 0, 0),
                    new Vector4(0, 1, 0, 0),
                    new Vector4(0, 1, 0, 0),
                    new Vector4(0, 1, 0, 0),
                    new Vector4(0, 1, 0, 0),
                    new Vector4(0, 1, 0, 0),
                };
                var layers = new[]
                {
                    ProjectPixel(new Vector3(0, 0, 10)),
                    ProjectPixel(new Vector3(0, 0, 10)),
                    ProjectPixel(new Vector3(4, 0, 10)),
                    ProjectPixel(new Vector3(-4, 0, 10)),
                    ProjectPixel(new Vector3(0, 0, 14)),
                    new Vector4(ProjectPixel(new Vector3(0, 0, 10)).x, ProjectPixel(new Vector3(0, 0, 10)).y, 4.0f, 0.0f),
                    ProjectPixel(new Vector3(20, 0, 10)),
                    ProjectPixel(new Vector3(0, 0, 10)),
                };

                cs.SetConstantBuffer(Shader.PropertyToID("AnalyticShadowUniform"), uniformBuffer, 0, uniformBuffer.stride);
                cs.SetBuffer(kernel, AnalyticShadowShaderIDs.AnalyticOccluderBuffer, ctx.RecordBuffer);
                cs.SetBuffer(kernel, AnalyticShadowShaderIDs.AnalyticIndirectClusterHeader, ctx.IndirectHeaderBuffer);
                cs.SetBuffer(kernel, AnalyticShadowShaderIDs.AnalyticIndirectClusterIndices, ctx.IndirectIndexBuffer);
                cs.SetBuffer(kernel, AnalyticShadowShaderIDs.AnalyticDirectClusterHeader, ctx.DirectHeaderBuffer);
                cs.SetBuffer(kernel, AnalyticShadowShaderIDs.AnalyticDirectClusterIndices, ctx.DirectIndexBuffer);
                cs.SetBuffer(kernel, Shader.PropertyToID("UAV_OutResults"), resultBuffer);
                cs.SetVector(Shader.PropertyToID("_TestLightDir"), new Vector4(0.0f, 1.0f, 0.0f, 0.01f));
                cs.SetVectorArray(Shader.PropertyToID("_TestPointPos"), points);
                cs.SetVectorArray(Shader.PropertyToID("_TestPointNormal"), normals);
                cs.SetVectorArray(Shader.PropertyToID("_TestPointLayer"), layers);

                cs.Dispatch(kernel, 1, 1, 1);
                var results = new Vector4[8];
                resultBuffer.GetData(results);
                for (int r = 0; r < 7; ++r)
                {
                    Assert.AreEqual(1.0f, results[r].w, 1e-4f, $"Kernel sentinel failed at {r}");
                }

                // 0: Sphere overhead -> both indirect AO and direct shadow darken
                Assert.Less(results[0].x, 0.85f, "Sphere indirect AO");
                Assert.Less(results[0].y, 0.25f, "Sphere direct shadow");

                // 1: Normal inverted -> indirect AO ~ 1.0, direct shadow still dark (< 0.25)
                Assert.Greater(results[1].x, 0.99f, "Sphere below horizon gives no indirect AO");
                Assert.Less(results[1].y, 0.25f, "Direct shadow is independent of indirect normal horizon");

                // 2: Capsule 6m above -> outside indirect falloff (indirect == 1.0), inside directMaxDistance (direct < 0.25)
                Assert.AreEqual(1.0f, results[2].x, 1e-4f, "Outside indirect falloff");
                Assert.Less(results[2].y, 0.25f, "Capsule long-range direct shadow");

                // 3: Box overhead -> both darken
                Assert.Less(results[3].x, 0.9f, "Box indirect AO");
                Assert.Less(results[3].y, 0.25f, "Box direct shadow");

                // 4: Ellipsoid overhead -> both darken
                Assert.Less(results[4].x, 0.9f, "Ellipsoid indirect AO");
                Assert.Less(results[4].y, 0.25f, "Ellipsoid direct shadow");

                // 5: Layer mismatch -> unshadowed & unoccluded
                Assert.AreEqual(1.0f, results[5].x, 1e-4f, "Layer mask filters indirect");
                Assert.AreEqual(1.0f, results[5].y, 1e-4f, "Layer mask filters direct");

                // 6: Out of bounds -> 1.0
                Assert.AreEqual(1.0f, results[6].x, 1e-4f);
                Assert.AreEqual(1.0f, results[6].y, 1e-4f);
            }
            finally
            {
                resultBuffer.Dispose();
                uniformBuffer.Dispose();
                ctx.Dispose();
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(sphereGo);
                UnityEngine.Object.DestroyImmediate(capGo);
                UnityEngine.Object.DestroyImmediate(boxGo);
                UnityEngine.Object.DestroyImmediate(ellipGo);
            }
        }
    }
}
