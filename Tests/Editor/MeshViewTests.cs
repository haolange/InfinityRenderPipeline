using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.GPUResource;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshViewTests
    {
        [Test]
        public void FromCamera_KeysTheViewByCameraIdentity_AndShadowViewsKeepTheirLight()
        {
            var go = new GameObject("MeshViewTests.Camera") { hideFlags = HideFlags.DontSave };
            try
            {
                Camera camera = go.AddComponent<Camera>();
                camera.cullingMask = 0x5;
                Assert.IsTrue(camera.TryGetCullingParameters(out ScriptableCullingParameters culling));
                MeshView first = MeshView.FromCamera(camera, ref culling);
                MeshView second = MeshView.FromCamera(camera, ref culling);
                Assert.AreEqual(UnityEntityId.ToUInt64(camera), first.viewKey);
                Assert.AreEqual(first.viewKey, second.viewKey);
                Assert.AreEqual(EMeshViewKind.Main, first.kind);
                Assert.AreEqual(0x5, first.layerMask);
                Assert.IsFalse(first.FilterRenderingLayers, "Camera views do not apply light rendering layers.");
                Assert.AreEqual(Math.Min(MeshView.MaxPlanes, culling.cullingPlaneCount), first.planeCount);

                ulong lightKey = first.viewKey ^ 0x1111ul;
                MeshView cascade = MeshView.FromShadow(EMeshViewKind.CascadeShadow, lightKey, first.CopyPlanes(), first.viewPosition, ~0, 2u, 3, ulong.MaxValue, MeshTestUtility.Lod);
                Assert.AreEqual(lightKey, cascade.viewKey);
                Assert.AreEqual(3, cascade.subviewIndex);
                Assert.AreEqual(2u, cascade.renderingLayerMask);
                Assert.IsTrue(cascade.FilterRenderingLayers);
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    MeshView.FromShadow(EMeshViewKind.Main, lightKey, first.CopyPlanes(), first.viewPosition, ~0, 2u, 0, ulong.MaxValue, MeshTestUtility.Lod));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void VisibilityKey_SeparatesEveryCullingInput_ExceptPlanesWithinACameraRender()
        {
            Plane[] planes = MeshTestUtility.BoxPlanes();
            ulong light = (768ul << 32) | 42ul;
            MeshView cascade0 = MeshView.FromShadow(EMeshViewKind.CascadeShadow, light, planes, float3.zero, ~0, 1u, 0, ulong.MaxValue, MeshTestUtility.Lod);
            MeshVisibilityKey Key(in MeshView view, bool request = false) => new MeshVisibilityKey(view, request);

            MeshView slice1 = cascade0; slice1.subviewIndex = 1;
            MeshView local = cascade0; local.kind = EMeshViewKind.LocalShadow;
            MeshView replacedLight = cascade0; replacedLight.viewKey = (769ul << 32) | 42ul;
            MeshView rendering = cascade0; rendering.renderingLayerMask = 2u;
            MeshView layer = cascade0; layer.layerMask = 1;
            MeshView emptyScene = cascade0; emptyScene.sceneCullingMask = 0;
            foreach (MeshView different in new[] { slice1, local, replacedLight, rendering, layer, emptyScene })
                Assert.AreNotEqual(Key(cascade0), Key(different));

            MeshView moved = cascade0;
            moved.SetPlane(0, new Plane(Vector3.right, 20f));
            moved.viewPosition = new float3(1, 2, 3);
            Assert.AreEqual(Key(cascade0), Key(moved), "Planes are fixed within one camera render and are not part of the identity.");

            MeshView main = MeshTestUtility.CameraView(7);
            MeshView sceneView = main; sceneView.kind = EMeshViewKind.SceneView;
            Assert.AreEqual(Key(main), Key(sceneView), "Camera kinds share one culling policy.");
            MeshView zeroInitialized = new MeshView { viewKey = 7 };
            MeshView explicitAll = zeroInitialized; explicitAll.sceneCullingMask = ulong.MaxValue;
            Assert.AreEqual(Key(zeroInitialized), Key(explicitAll), "A zero-initialized view is unrestricted in scene eligibility.");
            foreach (ulong mask in new[] { 0ul, 1ul, 1ul << 63, 0x8000000000000001ul, ulong.MaxValue })
            {
                MeshView roundTrip = main; roundTrip.sceneCullingMask = mask;
                Assert.AreEqual(mask, roundTrip.sceneCullingMask);
            }

            MeshView firstPhase = main; firstPhase.visibilityPhase = EMeshVisibilityPhase.First;
            Assert.AreNotEqual(Key(main), Key(firstPhase));
            MeshView history = firstPhase; history.visibilityHistory = 9;
            Assert.AreEqual(Key(firstPhase), Key(history), "Storage identity ignores the occlusion history.");
            Assert.AreNotEqual(Key(firstPhase, true), Key(history, true), "Request identity includes the occlusion history.");
        }

        [Test]
        public void CpuVisibility_AppliesFlagsMasksAndFrustum_AndInternsPerCameraRender()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                var ids = new MeshEntityId[8];
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    ids[0] = MeshTestUtility.CreateEntity(update, new float3(3, 0, 0));
                    ids[1] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), EMeshEntityFlags.None);
                    ids[2] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), layerMask: 2);
                    ids[3] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), sceneCullingMask: 1ul << 63);
                    ids[4] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0), renderingLayerMask: 2);
                    ids[5] = MeshTestUtility.CreateEntity(update, new float3(40, 0, 0));
                    ids[6] = MeshTestUtility.CreateEntity(update, new float3(10.4f, 0, 0));
                    ids[7] = MeshTestUtility.CreateEntity(update, new float3(0, 0, 0));
                    update.Commit();
                }
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(ids[7]);
                    update.Commit();
                }

                MeshView camera = MeshTestUtility.CameraView(11);
                camera.layerMask = 1;
                camera.sceneCullingMask = 1;
                byte[] cameraVisible = MeshTestUtility.CullOnCpu(world, camera, out float[] depth);
                // Visible flag, layer and scene masks apply; camera views ignore rendering layers; bounds touching a plane stay visible.
                CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 1, 0, 1, 0 }, cameraVisible);
                Assert.AreEqual(3f, depth[(int)ids[0].Index], 1e-5f, "Depth is the distance from the view position.");

                MeshView shadow = MeshView.FromShadow(EMeshViewKind.LocalShadow, 12, MeshTestUtility.BoxPlanes(), float3.zero, 1, 1u, 0, ulong.MaxValue, MeshTestUtility.Lod);
                CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 1, 0, 0, 1, 0 }, MeshTestUtility.CullOnCpu(world, shadow, out _),
                    "Shadow views apply the light's rendering layers.");

                world.stats.visibilityProductions = 0;
                MeshVisibilityShare share = world.visibilityShare;
                MeshVisibilityHandle first = share.Acquire(world, camera);
                MeshView replanned = camera;
                replanned.SetPlane(0, new Plane(Vector3.right, 1f));
                MeshVisibilityHandle shared = share.Acquire(world, replanned);
                Assert.AreEqual(first.slot, shared.slot);
                Assert.AreEqual(first.generation, shared.generation);
                Assert.AreEqual(1, world.stats.visibilityProductions);
                share.BeginCamera();
                MeshVisibilityHandle next = share.Acquire(world, camera);
                Assert.AreEqual(2, world.stats.visibilityProductions, "A later camera render never reuses an earlier result.");
                Assert.IsTrue(share.Get(first).isValid, "Earlier results stay alive for their holders.");
                share.Release(first);
                share.Release(shared);
                share.Release(next);
                Assert.AreEqual(0, share.liveCount);
            }
        }

        [Test]
        public void ResidentGpuVisibility_IsKeptAcrossCameraMotion()
        {
            var pool = new ResourcePool();
            var cache = new MeshGpuVisibilityCache(pool);
            try
            {
                MeshView view = MeshTestUtility.CameraView(51);
                BufferHandle first = cache.GetBufferHandle(view, 64);
                for (int frame = 1; frame <= 4; ++frame)
                {
                    // The LOD projection moves with the camera every frame; LOD bias edits change it too.
                    view.lodProjection.origin = new float3(frame, 0, 0);
                    view.lodProjection.bias = 1 + frame * 0.25f;
                    Assert.AreSame(first.buffer, cache.GetBufferHandle(view, 64).buffer, $"frame {frame}");
                }
                Assert.AreEqual(1, cache.allocatedViewCount);
                MeshView other = MeshTestUtility.CameraView(52);
                Assert.AreNotSame(first.buffer, cache.GetBufferHandle(other, 64).buffer, "Another view keeps its own buffer.");
                Assert.AreEqual(2, cache.allocatedViewCount);
            }
            finally
            {
                cache.Dispose();
                pool.Dispose();
            }
        }

        [Test]
        public void GpuFrustumCulling_AgreesWithCpuCulling()
        {
            MeshTestUtility.RequireComputeReadback();
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshTestUtility.RequireGpuIndirect(world);
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    MeshTestUtility.CreateEntity(update, new float3(3, 0, 0));
                    MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.None);
                    MeshTestUtility.CreateEntity(update, float3.zero, layerMask: 2);
                    MeshTestUtility.CreateEntity(update, float3.zero, sceneCullingMask: 1ul << 63);
                    MeshTestUtility.CreateEntity(update, float3.zero, sceneCullingMask: 1ul);
                    MeshTestUtility.CreateEntity(update, float3.zero, renderingLayerMask: 2);
                    MeshTestUtility.CreateEntity(update, new float3(40, 0, 0));
                    MeshTestUtility.CreateEntity(update, new float3(10.4f, 0, -10.4f));
                    update.Commit();
                }
                world.UpdateResidency();
                MeshWorldGpu gpu = world.gpu;
                ComputeShader shader = Object.Instantiate(fixture.shader);
                var output = new ComputeBuffer(world.entityHighWater, sizeof(uint));
                try
                {
                    int kernel = shader.FindKernel("CullFrustum");
                    MeshView camera = MeshTestUtility.CameraView(21);
                    MeshView shadow = MeshView.FromShadow(EMeshViewKind.CascadeShadow, 22, MeshTestUtility.BoxPlanes(), float3.zero, ~0, 1u, 0, ulong.MaxValue, MeshTestUtility.Lod);
                    foreach (ulong sceneMask in new[] { ulong.MaxValue, 1ul, 1ul << 63, 0ul })
                    {
                        foreach (MeshView source in new[] { camera, shadow })
                        {
                            MeshView view = source;
                            view.sceneCullingMask = sceneMask;
                            uint[] cpu = MeshTestUtility.PackedCullOnCpu(world, view);
                            var planes = new Vector4[MeshView.MaxPlanes];
                            view.CopyFrustumPlanes(planes);
                            shader.SetVectorArray("_FrustumPlanes", planes);
                            shader.SetInt("_EntityCount", world.entityHighWater);
                            shader.SetInt("_ViewLayerMask", view.layerMask);
                            shader.SetInt("_ViewSceneCullingMaskLow", unchecked((int)view.sceneCullingMask));
                            shader.SetInt("_ViewSceneCullingMaskHigh", unchecked((int)(view.sceneCullingMask >> 32)));
                            shader.SetInt("_ViewRenderingLayerMask", (int)view.renderingLayerMask);
                            shader.SetInt("_FilterRenderingLayers", view.FilterRenderingLayers ? 1 : 0);
                            shader.SetBuffer(kernel, "_EntityBoundsCenter", gpu.boundsCenter.buffer);
                            shader.SetBuffer(kernel, "_EntityBoundsExtent", gpu.boundsExtent.buffer);
                            shader.SetBuffer(kernel, "_EntityFlags", gpu.flags.buffer);
                            shader.SetBuffer(kernel, "_EntityLayerMask", gpu.layerMask.buffer);
                            shader.SetBuffer(kernel, "_EntitySceneCullingMask", gpu.sceneCullingMask.buffer);
                            shader.SetBuffer(kernel, "_EntityRenderingLayer", gpu.renderingLayers.buffer);
                            MeshTestUtility.BindLod(shader, kernel, gpu.lodChains.buffer, view.lodProjection);
                            shader.SetBuffer(kernel, "_Visibility", output);
                            shader.Dispatch(kernel, (world.entityHighWater + 63) / 64, 1, 1);
                            var actual = new uint[world.entityHighWater];
                            output.GetData(actual);
                            for (int i = 0; i < actual.Length; ++i)
                                Assert.AreEqual(cpu[i], actual[i], $"{view.kind} scene mask {sceneMask:X16}, entity {i}");
                        }
                    }
                }
                finally
                {
                    output.Release();
                    Object.DestroyImmediate(shader);
                }
            }
        }
    }
}
