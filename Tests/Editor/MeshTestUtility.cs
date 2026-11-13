using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.RenderGraph;
using Object = UnityEngine.Object;
using InfinityTech.Rendering.MaterialPipeline;

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

        /// <summary>A camera-like culling context: the box planes, no mask restrictions, and <see cref="Lod"/> moved to the origin.</summary>
        internal static MeshCullingContext CameraContext(float3 origin = default)
        {
            MeshLODProjection lod = Lod;
            lod.origin = origin;
            return MeshCullingContext.Create(BoxPlanes(), ~0, ulong.MaxValue, (uint)ERenderingLayer.Everything, false, lod);
        }

        /// <summary>
        /// A synthetic candidate table: element <c>entity * levels + level</c> belongs to command <c>level</c> and draws LOD
        /// <c>level</c> of its entity, the layout a pass cache with one command per level would build.
        /// </summary>
        internal static uint2[] LevelCandidates(int entityCount, int levels)
        {
            var candidates = new uint2[entityCount * levels];
            for (int entity = 0; entity < entityCount; ++entity)
                for (int level = 0; level < levels; ++level)
                    candidates[entity * levels + level] = new uint2((uint)entity, (uint)level | ((uint)level << MeshPassCache.LodShift));
            return candidates;
        }

        /// <summary>The CPU gather job a CPU draw list schedules, over an explicit candidate table, completed.</summary>
        internal static List<MeshDrawnInstance> GatherOnCpu(MeshWorld world, uint2[] candidates, int commandCount, in MeshCullingContext context, bool dither)
        {
            var table = new Unity.Collections.NativeArray<uint2>(candidates, Unity.Collections.Allocator.TempJob);
            var commands = new Unity.Collections.NativeArray<MeshDrawCommand>(Math.Max(1, commandCount), Unity.Collections.Allocator.TempJob);
            var elements = new Unity.Collections.NativeArray<MeshElement>(Math.Max(1, candidates.Length), Unity.Collections.Allocator.TempJob);
            var items = new Unity.Collections.NativeList<MeshDrawInstance>(Math.Max(1, candidates.Length * 2), Unity.Collections.Allocator.TempJob);
            try
            {
                new MeshDrawGatherJob<StateSortKey>
                {
                    candidates = table,
                    commands = commands,
                    elements = elements,
                    entities = world.entities,
                    generations = world.entityGenerations,
                    bounds = world.entityBounds,
                    context = context,
                    lod = context.ReadLODProjection(),
                    ditherLod = dither,
                    items = items.AsParallelWriter()
                }.Schedule(candidates.Length, 64).Complete();
                var result = new List<MeshDrawnInstance>(items.Length);
                foreach (MeshDrawInstance item in items) result.Add(new MeshDrawnInstance(item.entity, item.command, item.fade));
                result.Sort();
                return result;
            }
            finally
            {
                table.Dispose();
                commands.Dispose();
                elements.Dispose();
                items.Dispose();
            }
        }

        /// <summary>Per entity slot, whether any of its elements was drawn.</summary>
        internal static uint[] DrawnEntities(List<MeshDrawnInstance> drawn, int entityCount)
        {
            var bits = new uint[entityCount];
            foreach (MeshDrawnInstance instance in drawn) bits[instance.entity] = 1;
            return bits;
        }

        /// <summary>Drops the recorded graph (which releases its draw lists) before disposing the builder.</summary>
        internal static void Release(RGBuilder graph)
        {
            try { graph.ClearRecordedGraph(); }
            finally { graph.Dispose(); }
        }
    }

    /// <summary>One appended instance of a draw list: entity, command and signed dither factor (0 in the stable range).</summary>
    internal readonly struct MeshDrawnInstance : IEquatable<MeshDrawnInstance>, IComparable<MeshDrawnInstance>
    {
        internal readonly int entity, command;
        internal readonly float factor;
        internal MeshDrawnInstance(int entity, int command, float factor) { this.entity = entity; this.command = command; this.factor = factor; }
        public bool Equals(MeshDrawnInstance other) => entity == other.entity && command == other.command && factor == other.factor;
        public override bool Equals(object obj) => obj is MeshDrawnInstance other && Equals(other);
        public override int GetHashCode() => (entity * 397) ^ command ^ factor.GetHashCode();
        public int CompareTo(MeshDrawnInstance other)
        {
            int c = entity.CompareTo(other.entity);
            if (c != 0) return c;
            c = command.CompareTo(other.command);
            return c != 0 ? c : factor.CompareTo(other.factor);
        }
        public override string ToString() => $"({entity}, {command}, {factor})";
    }

    /// <summary>Entity columns a draw-list producer reads.</summary>
    internal struct MeshEntityColumns
    {
        internal GraphicsBuffer center, extent, flags, layers, scenes, renderingLayers, lodChains;

        internal static MeshEntityColumns Of(MeshWorld world)
        {
            MeshWorldGPU gpu = world.gpu;
            return new MeshEntityColumns
            {
                center = gpu.boundsCenter.buffer, extent = gpu.boundsExtent.buffer, flags = gpu.flags.buffer, layers = gpu.layerMask.buffer,
                scenes = gpu.sceneCullingMask.buffer, renderingLayers = gpu.renderingLayers.buffer, lodChains = gpu.lodChains.buffer
            };
        }
    }

    /// <summary>
    /// Runs the GPU draw-list producer kernels directly over an explicit candidate table, bound the way
    /// <c>MeshDrawGPUBackend.Produce</c> binds them, and reads back what they appended. Command <c>c</c> owns the stable range
    /// <c>[c * n, c * n + n)</c> and the fading range after all stable ranges, where <c>n</c> is the candidate count.
    /// </summary>
    internal sealed class MeshProducerRun : IDisposable
    {
        readonly ComputeShader m_Shader;
        readonly MeshEntityColumns m_Columns;
        readonly int m_CandidateCount, m_CommandCount;
        readonly GraphicsBuffer m_Candidates, m_Meta, m_Args, m_Instances, m_Fades, m_Context, m_Drawn;
        readonly uint[] m_MetaValues;

        internal MeshProducerRun(ComputeShader shader, in MeshEntityColumns columns, uint2[] candidates, int commandCount)
        {
            m_Shader = shader;
            m_Columns = columns;
            m_CandidateCount = candidates.Length;
            m_CommandCount = commandCount;
            int n = Math.Max(1, m_CandidateCount);
            m_Candidates = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(uint) * 2);
            m_Candidates.SetData(candidates);
            m_MetaValues = new uint[commandCount * MeshPassCache.MetaStride];
            for (int c = 0; c < commandCount; ++c)
            {
                m_MetaValues[c * 5 + 0] = 3;
                m_MetaValues[c * 5 + 3] = (uint)(c * n);
                m_MetaValues[c * 5 + 4] = (uint)((commandCount + c) * n);
            }
            m_Meta = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(1, m_MetaValues.Length), sizeof(uint));
            m_Meta.SetData(m_MetaValues);
            m_Args = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, Math.Max(1, commandCount * 2 * 5), sizeof(uint));
            m_Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, commandCount * 2 * n, sizeof(uint));
            m_Fades = new GraphicsBuffer(GraphicsBuffer.Target.Structured, commandCount * 2 * n, sizeof(float));
            m_Context = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, System.Runtime.InteropServices.Marshal.SizeOf<MeshCullingContext>());
            m_Drawn = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(uint));
        }

        /// <summary>Binds an occluder for the occluded kernels.</summary>
        internal void SetOccluder(Texture furthest, Matrix4x4 viewProjection, int width, int height, int mipCount)
        {
            foreach (string kernel in new[] { "BuildDrawListOccluded", "BuildDrawListOccludedHistory" })
                m_Shader.SetTexture(m_Shader.FindKernel(kernel), "_MeshCullFurthest", furthest);
            m_Shader.SetMatrix("_MeshCullProjection", viewProjection);
            m_Shader.SetVector("_MeshCullFullExtent", new Vector4(width, height, 0, 0));
            m_Shader.SetInt("_MeshCullMipCount", mipCount);
        }

        /// <summary>Binds the transform versions the history kernel compares.</summary>
        internal void SetHistory(GraphicsBuffer current, GraphicsBuffer currentGenerations, GraphicsBuffer previous, GraphicsBuffer previousGenerations, int currentCount, int previousCount)
        {
            int kernel = m_Shader.FindKernel("BuildDrawListOccludedHistory");
            m_Shader.SetBuffer(kernel, "_MeshCullCurrentTransforms", current);
            m_Shader.SetBuffer(kernel, "_MeshCullCurrentGenerations", currentGenerations);
            m_Shader.SetBuffer(kernel, "_MeshCullPreviousTransforms", previous);
            m_Shader.SetBuffer(kernel, "_MeshCullPreviousGenerations", previousGenerations);
            m_Shader.SetInt("_MeshCullCurrentCount", currentCount);
            m_Shader.SetInt("_MeshCullPreviousCount", previousCount);
        }

        /// <summary>Initializes the arguments and runs one build kernel; the occluded kernels also write the drawn table.</summary>
        internal List<MeshDrawnInstance> Build(string kernelName, in MeshCullingContext context, bool dither = false, bool readDrawn = false)
        {
            m_Context.SetData(new[] { context });
            int initialize = m_Shader.FindKernel("InitializeIndirectArgs");
            m_Shader.SetInt("_CommandCount", m_CommandCount);
            m_Shader.SetBuffer(initialize, "_CommandMeta", m_Meta);
            m_Shader.SetBuffer(initialize, "_IndirectArgs", m_Args);
            m_Shader.Dispatch(initialize, Math.Max(1, (m_CommandCount + 63) / 64), 1, 1);

            int kernel = m_Shader.FindKernel(kernelName);
            m_Shader.SetInt("_CandidateCount", m_CandidateCount);
            m_Shader.SetInt("_DitherLod", dither ? 1 : 0);
            m_Shader.SetInt("_ReadDrawn", readDrawn ? 1 : 0);
            m_Shader.SetBuffer(kernel, "_CullingContext", m_Context);
            m_Shader.SetBuffer(kernel, "_EntityBoundsCenter", m_Columns.center);
            m_Shader.SetBuffer(kernel, "_EntityBoundsExtent", m_Columns.extent);
            m_Shader.SetBuffer(kernel, "_EntityFlags", m_Columns.flags);
            m_Shader.SetBuffer(kernel, "_EntityLayerMask", m_Columns.layers);
            m_Shader.SetBuffer(kernel, "_EntitySceneCullingMask", m_Columns.scenes);
            m_Shader.SetBuffer(kernel, "_EntityRenderingLayer", m_Columns.renderingLayers);
            m_Shader.SetBuffer(kernel, "_EntityLodChains", m_Columns.lodChains);
            m_Shader.SetBuffer(kernel, "_CandidateTable", m_Candidates);
            m_Shader.SetBuffer(kernel, "_CommandMeta", m_Meta);
            m_Shader.SetBuffer(kernel, "_IndirectArgs", m_Args);
            m_Shader.SetBuffer(kernel, "_InstanceToEntity", m_Instances);
            m_Shader.SetBuffer(kernel, "_InstanceFade", m_Fades);
            if (kernelName != "BuildDrawList") m_Shader.SetBuffer(kernel, "_DrawnCandidates", m_Drawn);
            m_Shader.Dispatch(kernel, Math.Max(1, (m_CandidateCount + 63) / 64), 1, 1);
            return Read();
        }

        List<MeshDrawnInstance> Read()
        {
            var args = new uint[m_Args.count];
            var instances = new uint[m_Instances.count];
            var fades = new float[m_Fades.count];
            m_Args.GetData(args);
            m_Instances.GetData(instances);
            m_Fades.GetData(fades);
            var result = new List<MeshDrawnInstance>();
            for (int command = 0; command < m_CommandCount; ++command)
                for (int range = 0; range < 2; ++range)
                {
                    uint count = args[(command * 2 + range) * 5 + 1];
                    uint first = m_MetaValues[command * 5 + 3 + range];
                    for (uint i = 0; i < count; ++i)
                        result.Add(new MeshDrawnInstance((int)instances[first + i], command, fades[first + i]));
                }
            result.Sort();
            return result;
        }

        /// <summary>The drawn table the occluded kernels wrote, one word per candidate.</summary>
        internal uint[] DrawnCandidates()
        {
            var drawn = new uint[m_Drawn.count];
            m_Drawn.GetData(drawn);
            return drawn;
        }

        public void Dispose()
        {
            m_Candidates.Release();
            m_Meta.Release();
            m_Args.Release();
            m_Instances.Release();
            m_Fades.Release();
            m_Context.Release();
            m_Drawn.Release();
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
