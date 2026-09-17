using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
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

    public sealed class InfinityRayTracingEnvironment : IDisposable
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
        int m_SyncedStructural = -1;
        int m_SyncedContent = -1;
        int m_InstanceCount;
        bool m_NeedsBuild;
        bool m_Disposed;

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

        public void SyncGeometry(MeshScene scene)
        {
            if (!IsReady)
            {
                return;
            }

            int structural = scene != null ? scene.StructuralRevision : -1;
            int content = scene != null ? scene.ContentRevision : -1;
            if (structural == m_SyncedStructural && content == m_SyncedContent && m_InstanceCount > 0)
            {
                return;
            }

            m_Accel.ClearInstances();
            m_InstanceCount = 0;
            var seenMeshes = new HashSet<ulong>();

            if (scene != null)
            {
                var draws = scene.GetDraws();
                var instances = scene.GetInstances();
                var transforms = scene.GetTransforms();
                var sections = scene.GetSections();
                var sectionGens = scene.GetSectionGenerations();
                for (int i = 0; i < scene.DrawHighWater; ++i)
                {
                    if (!scene.IsDrawSlotLive(i))
                    {
                        continue;
                    }

                    MeshDraw draw = draws[i];
                    if (!scene.TryGetInstance(draw.instance, out MeshInstanceRecord instance))
                    {
                        continue;
                    }

                    int sectionIndex = (int)draw.section.Index;
                    if (sectionIndex < 0 || sectionIndex >= sections.Length || sectionGens[sectionIndex] != draw.section.Generation)
                    {
                        continue;
                    }

                    Mesh mesh = ResolveMesh(sections[sectionIndex].meshUnityId);
                    if (mesh == null)
                    {
                        continue;
                    }

                    seenMeshes.Add(UnityEntityId.ToUInt64(mesh));
                    int transformIndex = (int)instance.transform.Index;
                    Matrix4x4 matrix = transformIndex >= 0 && transformIndex < transforms.Length
                        ? transforms[transformIndex].current
                        : Matrix4x4.identity;
                    AddMesh(mesh, draw.sectionIndex, matrix);
                }
            }

            MeshRenderer[] renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
            for (int i = 0; i < renderers.Length; ++i)
            {
                MeshRenderer renderer = renderers[i];
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || seenMeshes.Contains(UnityEntityId.ToUInt64(mesh)))
                {
                    continue;
                }

                for (int sub = 0; sub < mesh.subMeshCount; ++sub)
                {
                    AddMesh(mesh, sub, renderer.localToWorldMatrix);
                }
            }

            m_SyncedStructural = structural;
            m_SyncedContent = content;
            m_NeedsBuild = m_InstanceCount > 0;
            if (m_InstanceCount == 0)
            {
                lastBlockReason = "URT accel has no mesh instances.";
            }
            else if (string.IsNullOrEmpty(lastBlockReason) || lastBlockReason.StartsWith("URT accel"))
            {
                lastBlockReason = string.Empty;
            }
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

        public bool ConsumeBuild()
        {
            if (!m_NeedsBuild)
            {
                return false;
            }

            m_NeedsBuild = false;
            return true;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
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
