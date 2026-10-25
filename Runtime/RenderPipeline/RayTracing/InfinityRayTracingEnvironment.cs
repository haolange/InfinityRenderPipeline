using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    public enum ERayTracingBackend
    {
        Unavailable = 0,
        Hardware = 1,
        Compute = 2
    }

    public sealed class InfinityRayTracingEnvironment : IDisposable, IRGPassQueueObserver
    {
        const string VisibilityShaderPath = "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/RayTracing/VisibilityRTAO.urtshader";

        public static InfinityRayTracingEnvironment current { get; private set; }
        public static ERayTracingBackend lastBackend { get; private set; } = ERayTracingBackend.Unavailable;
        public static string lastBlockReason { get; private set; } = "URT context is not created.";

        RayTracingContext m_Context;
        RayTracingResources m_Resources;
        IRayTracingAccelStruct m_Accel;
        IRayTracingShader m_Shader;
        GraphicsBuffer m_BuildScratch;
        GraphicsBuffer m_TraceScratch;
        internal readonly struct GeometryInstance : IEquatable<GeometryInstance>
        {
            internal readonly ulong owner;
            internal readonly bool native;
            internal readonly Mesh mesh;
            internal readonly int section;
            internal readonly Matrix4x4 matrix;
            internal GeometryInstance(ulong owner, bool native, Mesh mesh, int section, Matrix4x4 matrix)
            { this.owner = owner; this.native = native; this.mesh = mesh; this.section = section; this.matrix = matrix; }
            public bool Equals(GeometryInstance other) => owner == other.owner && native == other.native
                && mesh == other.mesh && section == other.section && matrix.Equals(other.matrix);
        }
        readonly List<GeometryInstance> m_Geometry = new List<GeometryInstance>();
        readonly List<GeometryInstance> m_CollectedGeometry = new List<GeometryInstance>();
        readonly List<GameObject> m_Roots = new List<GameObject>();
        readonly List<MeshRenderer> m_Renderers = new List<MeshRenderer>();
        GameObject m_PersistentSceneAnchor;
        internal IReadOnlyList<GeometryInstance> Geometry => m_CollectedGeometry;
        internal bool NeedsBuild => m_NeedsBuild && !m_BuildQueued;
        int m_SyncedStructural = -1;
        int m_SyncedContent = -1;
        int m_InstanceCount;
        bool m_NeedsBuild, m_BuildQueued;
        bool m_Disposed;

        internal RayTracingBackend Backend => IsReady ? m_Context.BackendType
            : throw new InvalidOperationException("URT context is not ready.");

        internal static CustomSamplerId OcclusionSampler(RayTracingBackend backend) => backend switch
        {
            RayTracingBackend.Compute => CustomSamplerId.ComputeRayTracedOcclusion,
            RayTracingBackend.Hardware => CustomSamplerId.RayTracingOcclusion,
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };

        public IRayTracingAccelStruct Accel => m_Accel;
        public IRayTracingShader Shader => m_Shader;
        public int InstanceCount => m_InstanceCount;
        public bool IsReady => !m_Disposed && m_Context != null && m_Accel != null && m_Shader != null;

        public static InfinityRayTracingEnvironment EnsureOwned()
        {
            if (current == null || current.m_Disposed)
            {
                current = new InfinityRayTracingEnvironment();
            }

            current.TryInitialize();
            return current;
        }

        public static ERayTracingBackend ResolveBackend()
        {
            EnsureOwned();
            return lastBackend;
        }

        public static bool CanRecord(InfinityRenderPipelineAsset asset, InfinityRenderPipelineRuntimeShaders shaders)
        {
            InfinityRayTracingEnvironment env = EnsureOwned();
            if (!env.IsReady)
            {
                lastBackend = ERayTracingBackend.Unavailable;
                lastBlockReason = env.m_Disposed
                    ? "URT environment was disposed."
                    : lastBlockReason;
                return false;
            }

            return true;
        }

        public static void ReportBlockReason(string reason)
        {
            lastBlockReason = reason ?? string.Empty;
        }

        public void TryInitialize()
        {
            if (m_Disposed || IsReady)
            {
                return;
            }

            m_Resources = new RayTracingResources();
            bool loaded = m_Resources.LoadFromRenderPipelineResources();
#if UNITY_EDITOR
            if (!loaded)
            {
                m_Resources.Load();
                loaded = m_Resources.geometryPoolKernels != null && m_Resources.buildHlbvh != null;
            }
#endif
            if (!loaded)
            {
                lastBlockReason = "UnifiedRayTracing resources are missing (RayTracingRenderPipelineResources / CoreRP kernels).";
                lastBackend = ERayTracingBackend.Unavailable;
                return;
            }

            RayTracingBackend backend;
            if (RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware))
            {
                backend = RayTracingBackend.Hardware;
                lastBackend = ERayTracingBackend.Hardware;
            }
            else if (RayTracingContext.IsBackendSupported(RayTracingBackend.Compute))
            {
                backend = RayTracingBackend.Compute;
                lastBackend = ERayTracingBackend.Compute;
            }
            else
            {
                lastBackend = ERayTracingBackend.Unavailable;
                lastBlockReason = "No UnifiedRayTracing backend is supported on this GPU.";
                return;
            }

            try
            {
                m_Context = new RayTracingContext(backend, m_Resources);
                m_Accel = m_Context.CreateAccelerationStructure(new AccelerationStructureOptions
                {
                    buildFlags = BuildFlags.PreferFastBuild
                });
#if UNITY_EDITOR
                m_Shader = m_Context.LoadRayTracingShader(VisibilityShaderPath);
#else
                lastBlockReason = "Player URT shader load is not wired (Editor LoadRayTracingShader only).";
                lastBackend = ERayTracingBackend.Unavailable;
                DisposeContextOnly();
                return;
#endif
                if (m_Shader == null)
                {
                    lastBlockReason = "VisibilityRTAO.urtshader failed to load.";
                    lastBackend = ERayTracingBackend.Unavailable;
                    DisposeContextOnly();
                    return;
                }
            }
            catch (Exception error)
            {
                lastBlockReason = "URT context create failed: " + error.Message;
                lastBackend = ERayTracingBackend.Unavailable;
                DisposeContextOnly();
                return;
            }

            lastBlockReason = string.Empty;
        }

        // URT accepts explicit instances rather than a raster CullingResults. Its input adapter is isolated here.
        // Reused root/component lists observe native scene changes without a second raster registry or per-frame arrays.
        internal bool CollectGeometry(MeshScene scene)
        {
            m_CollectedGeometry.Clear();
            if (scene != null)
            {
                var draws = scene.GetDraws();
                var instances = scene.GetInstances();
                var transforms = scene.GetTransforms();
                var sections = scene.GetSections();
                var generations = scene.GetSectionGenerations();
                var drawGenerations = scene.GetDrawGenerations();
                for (int i = 0; i < scene.DrawHighWater; ++i)
                {
                    if (!scene.IsDrawSlotLive(i)) continue;
                    var draw = draws[i];
                    if (!scene.TryGetInstance(draw.instance, out var instance)
                        || (instance.flags & EMeshInstanceFlags.Visible) == 0) continue;
                    int section = (int)draw.section.Index;
                    if (section < 0 || section >= sections.Length || generations[section] != draw.section.Generation) continue;
                    Mesh mesh = ResolveMesh(sections[section].meshUnityId);
                    if (mesh == null || draw.sectionIndex < 0 || draw.sectionIndex >= mesh.subMeshCount) continue;
                    int transform = (int)instance.transform.Index;
                    m_CollectedGeometry.Add(new GeometryInstance(((ulong)drawGenerations[i] << 32) | (uint)i,
                        false, mesh, draw.sectionIndex, transforms[transform].current));
                }
            }
            for (int i = 0; i < SceneManager.sceneCount; ++i) CollectNativeScene(SceneManager.GetSceneAt(i));
            if (Application.isPlaying)
            {
                // Unity excludes its persistent scene from SceneManager.sceneCount; keep one owned handle into it.
                if (m_PersistentSceneAnchor == null)
                {
                    m_PersistentSceneAnchor = new GameObject("Infinity RT Scene Scope") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(m_PersistentSceneAnchor);
                }
                CollectNativeScene(m_PersistentSceneAnchor.scene);
            }
            bool changed = m_Geometry.Count != m_CollectedGeometry.Count;
            if (!changed)
                for (int i = 0; i < m_Geometry.Count; ++i)
                    if (!m_Geometry[i].Equals(m_CollectedGeometry[i])) { changed = true; break; }
            return changed;
        }

        void CollectNativeScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            m_Roots.Clear();
            if (m_Roots.Capacity < scene.rootCount) m_Roots.Capacity = scene.rootCount;
            scene.GetRootGameObjects(m_Roots);
            foreach (var root in m_Roots)
            {
                root.GetComponentsInChildren(false, m_Renderers);
                foreach (var renderer in m_Renderers)
                {
                    if (!renderer.gameObject.activeInHierarchy || !renderer.enabled || renderer.forceRenderingOff || !renderer.TryGetComponent(out MeshFilter filter)) continue;
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null) continue;
                    ulong owner = UnityEntityId.ToUInt64(renderer);
                    for (int section = 0; section < mesh.subMeshCount; ++section)
                        m_CollectedGeometry.Add(new GeometryInstance(owner, true, mesh, section, renderer.localToWorldMatrix));
                }
            }
        }

        public void SyncGeometry(MeshScene scene)
        {
            if (!IsReady) return;
            bool changed = CollectGeometry(scene);
            int structural = scene != null ? scene.StructuralRevision : -1;
            int content = scene != null ? scene.ContentRevision : -1;
            if (!changed && structural == m_SyncedStructural && content == m_SyncedContent) return;
            m_Accel.ClearInstances();
            m_BuildQueued = false;
            m_InstanceCount = 0;
            foreach (var instance in m_CollectedGeometry) AddMesh(instance.mesh, instance.section, instance.matrix);
            m_Geometry.Clear(); m_Geometry.AddRange(m_CollectedGeometry);
            m_SyncedStructural = structural; m_SyncedContent = content;
            m_NeedsBuild = m_InstanceCount > 0;
            if (m_InstanceCount == 0) lastBlockReason = "URT accel has no mesh instances.";
            else if (lastBlockReason.StartsWith("URT accel")) lastBlockReason = string.Empty;
        }

        void IRGPassQueueObserver.OnQueued() => m_BuildQueued = true;
        void IRGPassQueueObserver.OnQueueFailed() { m_NeedsBuild = true; m_BuildQueued = false; }
        internal void CompleteFrame(bool accepted)
        {
            if (m_BuildQueued) m_NeedsBuild = !accepted;
            m_BuildQueued = false;
        }

        public GraphicsBuffer PrepareBuildScratch()
        {
            ulong bytes = m_Accel.GetBuildScratchBufferRequiredSizeInBytes();
            EnsureScratch(ref m_BuildScratch, bytes);
            return m_BuildScratch;
        }

        public GraphicsBuffer PrepareTraceScratch(uint width, uint height)
        {
            ulong bytes = m_Shader.GetTraceScratchBufferRequiredSizeInBytes(width, height, 1);
            EnsureScratch(ref m_TraceScratch, bytes);
            return m_TraceScratch;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            if (m_PersistentSceneAnchor != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(m_PersistentSceneAnchor);
                else UnityEngine.Object.DestroyImmediate(m_PersistentSceneAnchor);
                m_PersistentSceneAnchor = null;
            }
            DisposeContextOnly();
            if (current == this)
            {
                current = null;
            }

            lastBackend = ERayTracingBackend.Unavailable;
            lastBlockReason = "URT environment was disposed.";
        }

        void DisposeContextOnly()
        {
            m_Shader = null;
            m_Accel?.Dispose();
            m_Accel = null;
            m_Context?.Dispose();
            m_Context = null;
            m_BuildScratch?.Release();
            m_BuildScratch = null;
            m_TraceScratch?.Release();
            m_TraceScratch = null;
        }

        void AddMesh(Mesh mesh, int subMesh, Matrix4x4 matrix)
        {
            var desc = new MeshInstanceDesc(mesh, subMesh)
            {
                localToWorldMatrix = matrix,
                mask = 0xFF,
                opaqueGeometry = true
            };
            m_Accel.AddInstance(desc);
            m_InstanceCount++;
        }

        static Mesh ResolveMesh(ulong meshUnityId)
        {
            if (meshUnityId == 0 || meshUnityId == ulong.MaxValue)
            {
                return null;
            }

            return UnityEntityId.ToObject<Mesh>(meshUnityId);
        }

        static void EnsureScratch(ref GraphicsBuffer buffer, ulong bytes)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(bytes / 4.0f));
            if (buffer != null && buffer.count >= count)
            {
                return;
            }

            buffer?.Release();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
        }
    }
}
