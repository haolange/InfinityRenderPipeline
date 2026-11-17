using System;
using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Rendering.Scene;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// The pipeline-lifetime root: every service that outlives a frame, built in dependency order and disposed in reverse.
    /// A frame's engine facts live in <see cref="FrameContext"/> and a camera's in <see cref="ViewContext"/>; neither is
    /// stored here. The root forwards nothing and no subsystem refers back to it: the pipeline hands each pass the services
    /// it reads.
    /// </summary>
    internal sealed class RenderContext : IDisposable
    {
        internal readonly ResourcePool resourcePool;
        internal readonly SceneWorld sceneWorld;
        internal readonly RGBuilder graph;
        internal readonly AtmosphereSharedCache atmosphereCache;
        internal readonly ProbeVolumeIntegration probeVolumes;
        internal readonly CameraStateTable cameras;
        // Pipeline-wide frame transactions in completion order; camera-owned ones live in CameraFrameState.
        internal readonly IFrameTransaction[] frameTransactions;

        internal RenderContext(in InfinityRenderPipelineAsset.Settings settings, ComputeShader meshDrawShader)
        {
            try
            {
                resourcePool = new ResourcePool();
                sceneWorld = new SceneWorld(resourcePool, meshDrawShader);
                graph = new RGBuilder("RenderGraph", resourcePool);
                graph.SetMeshWorld(sceneWorld.meshes);
                atmosphereCache = new AtmosphereSharedCache(resourcePool);
                probeVolumes = new ProbeVolumeIntegration(settings);
                cameras = new CameraStateTable(resourcePool);
                frameTransactions = new IFrameTransaction[]
                {
                    atmosphereCache, sceneWorld.lights.shadowCache, sceneWorld.lights.cookieAtlas, sceneWorld.probes.atlas
                };
            }
            catch (Exception constructionFailure)
            {
                try { Dispose(); }
                catch (Exception cleanupFailure) { throw new AggregateException(constructionFailure, cleanupFailure); }
                throw;
            }
        }

        // Reverse construction order. A recorded graph is cleared first: its draw lists and camera reads return to their
        // owners before those dispose. Construction rollback reaches here with the later services unbuilt.
        public void Dispose()
        {
            var failures = new List<Exception>();
            void Release(Action action)
            {
                try { action(); }
                catch (Exception error) { failures.Add(error); }
            }
            if (graph != null) Release(graph.ClearRecordedGraph);
            if (cameras != null) Release(cameras.Dispose);
            if (probeVolumes != null) Release(probeVolumes.Dispose);
            if (atmosphereCache != null) Release(atmosphereCache.Dispose);
            if (graph != null) Release(graph.Dispose);
            if (sceneWorld != null) Release(sceneWorld.Dispose);
            if (resourcePool != null) Release(resourcePool.Dispose);
            if (failures.Count == 1)
            {
                throw failures[0];
            }
            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }
    }
}
