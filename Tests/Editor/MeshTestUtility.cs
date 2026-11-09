using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.RenderGraph;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    internal static class MeshTestUtility
    {
        internal const string MeshDrawShaderPath = "Packages/com.infinity.render-pipeline/Shaders/Mesh/Compute_MeshDrawPipeline.compute";
        internal const string InstancedShaderName = "InfinityPipeline/InfinityLit-Instanced";

        /// <summary>
        /// A material state that no preset accepts: opaque presets end at queue 2999 and translucent presets require a
        /// translucent stage. World bookkeeping tests use it so that commits never resolve Unity objects.
        /// </summary>
        internal static readonly MeshMaterialState Unrouted = new MeshMaterialState(MeshPassTable.TranslucentQueueMax + 1, 0, 0, 0);

        internal static ComputeShader LoadMeshDrawShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(MeshDrawShaderPath);
            if (shader == null) Assert.Ignore("Compute_MeshDrawPipeline is not available.");
            return shader;
        }

        /// <summary>GPU fixtures run only where the world's indirect backend is real; they never pass on a CPU path.</summary>
        internal static void RequireGPUIndirect(MeshWorld world)
        {
            if (!MeshDrawGPUBackend.DeviceSupportsIndirect || !world.gpuBackend.isSupported)
                Assert.Ignore("GPU indirect mesh drawing is not supported on this device.");
        }

        internal static void RequireComputeReadback()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute shaders with GPU readback are not supported on this device.");
        }

        internal static Mesh CreateMesh(int subMeshCount, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.subMeshCount = subMeshCount;
            for (int subMesh = 0; subMesh < subMeshCount; ++subMesh) mesh.SetTriangles(new[] { 0, 1, 2 }, subMesh);
            return mesh;
        }

        /// <summary>An instanced material routed through the one route authority, with its pass state applied.</summary>
        internal static Material CreateRoutedMaterial(int route, int stage, string name)
        {
            Shader shader = Shader.Find(InstancedShaderName);
            Assert.IsNotNull(shader, InstancedShaderName);
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            material.SetFloat("_SurfaceRoute", route);
            material.SetFloat("_TranslucentStage", stage);
            MaterialRouteUtility.ApplyPassState(material);
            return material;
        }

        internal static MeshMaterialState StateOf(Material material) => StateOf(material, material.renderQueue);

        /// <summary>The material's route, stage and shader identity with an explicit queue.</summary>
        internal static MeshMaterialState StateOf(Material material, int renderQueue)
        {
            MaterialRouteUtility.Read(material, out int route, out int stage);
            return new MeshMaterialState(renderQueue, route, stage, UnityEntityId.ToUInt64(material.shader));
        }

        internal static ulong Id(Object value) => UnityEntityId.ToUInt64(value);

        internal static MeshEntityDesc Desc(EMeshEntityFlags flags = EMeshEntityFlags.Visible, int layerMask = ~0,
            uint renderingLayerMask = 1, ulong sceneCullingMask = ulong.MaxValue)
        {
            return new MeshEntityDesc { flags = flags, layerMask = layerMask, renderingLayerMask = renderingLayerMask, sceneCullingMask = sceneCullingMask };
        }

        internal static BoundingBox Bound(float3 center) => new BoundingBox(center, new float3(0.5f));

        /// <summary>A perspective-like LOD projection from the origin (screen size = largest half-extent / distance).</summary>
        internal static readonly MeshLODProjection Lod = new MeshLODProjection { origin = float3.zero, scale = 1, bias = 1 };

        internal static MeshEntityId CreateEntity(in MeshWorldUpdate update, float3 center, EMeshEntityFlags flags = EMeshEntityFlags.Visible,
            int layerMask = ~0, uint renderingLayerMask = 1, ulong sceneCullingMask = ulong.MaxValue)
        {
            return update.CreateEntity(float4x4.Translate(center), Bound(center), Desc(flags, layerMask, renderingLayerMask, sceneCullingMask), MeshLODChain.Single);
        }

        /// <summary>Six planes enclosing [-10, 10] on every axis.</summary>
        internal static Plane[] BoxPlanes()
        {
            return new[]
            {
                new Plane(Vector3.right, 10f), new Plane(Vector3.left, 10f),
                new Plane(Vector3.up, 10f), new Plane(Vector3.down, 10f),
                new Plane(Vector3.forward, 10f), new Plane(Vector3.back, 10f)
            };
        }

        internal static MeshView CameraView(ulong viewKey, float3 position = default)
        {
            var view = new MeshView
            {
                viewKey = viewKey,
                kind = EMeshViewKind.Main,
                viewPosition = position,
                layerMask = ~0,
                sceneCullingMask = ulong.MaxValue,
                renderingLayerMask = (uint)ERenderingLayer.Everything,
                lodProjection = Lod
            };
            Plane[] planes = BoxPlanes();
            view.planeCount = planes.Length;
            for (int i = 0; i < planes.Length; ++i) view.SetPlane(i, planes[i]);
            return view;
        }

        /// <summary>CPU visibility of every entity slot through the world's interning share.</summary>
        internal static byte[] CullOnCpu(MeshWorld world, in MeshView view, out float[] depth)
        {
            MeshVisibilityHandle handle = world.visibilityShare.Acquire(world, view);
            try
            {
                MeshViewVisibility result = world.visibilityShare.Get(handle);
                result.job.Complete();
                var visible = new byte[world.entityHighWater];
                depth = new float[world.entityHighWater];
                for (int i = 0; i < visible.Length; ++i)
                {
                    visible[i] = result.visible[i];
                    depth[i] = result.depth[i];
                }
                return visible;
            }
            finally { world.visibilityShare.Release(handle); }
        }

        /// <summary>CPU visibility in the layout the GPU kernels write: 0, or 1 | level << 8 | fade << 16 (65535 = stable).</summary>
        internal static uint[] PackedCullOnCpu(MeshWorld world, in MeshView view)
        {
            MeshVisibilityHandle handle = world.visibilityShare.Acquire(world, view);
            try
            {
                MeshViewVisibility result = world.visibilityShare.Get(handle);
                result.job.Complete();
                var packed = new uint[world.entityHighWater];
                for (int i = 0; i < packed.Length; ++i)
                {
                    if (result.visible[i] == 0) continue;
                    float fade = result.fade[i];
                    uint quantized = fade >= 1 ? 65535u : (uint)(math.saturate(fade) * 65534.0f);
                    packed[i] = 1u | ((uint)result.lod[i] << 8) | (quantized << 16);
                }
                return packed;
            }
            finally { world.visibilityShare.Release(handle); }
        }

        /// <summary>The LOD inputs every culling kernel reads, bound the way <c>MeshDrawGPUBackend.BindCullBounds</c> binds them.</summary>
        internal static void BindLod(ComputeShader shader, int kernel, ComputeBuffer chains, in MeshLODProjection lod)
        {
            shader.SetBuffer(kernel, "_EntityLodChains", chains);
            shader.SetVector("_LodOrigin", new Vector4(lod.origin.x, lod.origin.y, lod.origin.z, 0));
            shader.SetFloat("_LodScale", lod.scale);
            shader.SetFloat("_LodBias", lod.bias);
            shader.SetInt("_LodOrthographic", lod.orthographic ? 1 : 0);
        }

        /// <summary>Reduces packed visibility words to 0/1 for list comparisons.</summary>
        internal static uint[] VisibleBits(uint[] packed)
        {
            var bits = new uint[packed.Length];
            for (int i = 0; i < packed.Length; ++i) bits[i] = packed[i] != 0 ? 1u : 0u;
            return bits;
        }

        internal static void DispatchFrustum(ComputeShader shader, MeshWorld world, MeshView[] views, ComputeBuffer visibility, int stride)
        {
            int kernel = shader.FindKernel("CullFrustum");
            var table = new MeshFrustumView[views.Length];
            for (int i = 0; i < views.Length; ++i) table[i] = MeshFrustumView.FromView(views[i]);
            var viewBuffer = new ComputeBuffer(Math.Max(1, views.Length), System.Runtime.InteropServices.Marshal.SizeOf<MeshFrustumView>());
            try
            {
                viewBuffer.SetData(table);
                MeshWorldGPU gpu = world.gpu;
                shader.SetInt("_EntityCount", world.entityHighWater);
                shader.SetInt("_ViewCount", views.Length);
                shader.SetInt("_EntityStride", stride);
                shader.SetBuffer(kernel, "_FrustumViews", viewBuffer);
                shader.SetBuffer(kernel, "_EntityBoundsCenter", gpu.boundsCenter.buffer);
                shader.SetBuffer(kernel, "_EntityBoundsExtent", gpu.boundsExtent.buffer);
                shader.SetBuffer(kernel, "_EntityFlags", gpu.flags.buffer);
                shader.SetBuffer(kernel, "_EntityLayerMask", gpu.layerMask.buffer);
                shader.SetBuffer(kernel, "_EntitySceneCullingMask", gpu.sceneCullingMask.buffer);
                shader.SetBuffer(kernel, "_EntityRenderingLayer", gpu.renderingLayers.buffer);
                shader.SetBuffer(kernel, "_EntityLodChains", gpu.lodChains.buffer);
                shader.SetBuffer(kernel, "_Visibility", visibility);
                shader.Dispatch(kernel, (world.entityHighWater + 63) / 64, Math.Max(1, views.Length), 1);
            }
            finally { viewBuffer.Release(); }
        }

        /// <summary>Drops the recorded graph (which releases its draw lists) before disposing the builder.</summary>
        internal static void Release(RGBuilder graph)
        {
            try { graph.ClearRecordedGraph(); }
            finally { graph.Dispose(); }
        }
    }

    /// <summary>A pool, a world on the real mesh compute shader, and the Unity objects a test creates, released in that order.</summary>
    internal sealed class MeshWorldFixture : IDisposable
    {
        internal readonly ComputeShader shader;
        internal readonly ResourcePool pool;
        internal readonly MeshWorld world;
        readonly List<Object> m_Objects = new List<Object>();

        internal MeshWorldFixture()
        {
            shader = MeshTestUtility.LoadMeshDrawShader();
            pool = new ResourcePool();
            try { world = new MeshWorld(pool, shader); }
            catch
            {
                pool.Dispose();
                throw;
            }
        }

        internal T Own<T>(T value) where T : Object
        {
            m_Objects.Add(value);
            return value;
        }

        internal Mesh Mesh(int subMeshCount) => Own(MeshTestUtility.CreateMesh(subMeshCount, "MeshWorldFixture.Mesh" + m_Objects.Count));

        internal Material Material(int route, int stage) => Own(MeshTestUtility.CreateRoutedMaterial(route, stage, "MeshWorldFixture.Material" + m_Objects.Count));

        public void Dispose()
        {
            try { world.Dispose(); }
            finally
            {
                pool.Dispose();
                foreach (Object value in m_Objects) Object.DestroyImmediate(value);
                m_Objects.Clear();
            }
        }
    }
}
