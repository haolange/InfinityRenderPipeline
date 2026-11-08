using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public delegate void RenderTask(RenderContext renderContext);

    public static class RenderTaskQueue
    {
        internal struct MeshTask
        {
            internal MeshComponent component;
            internal MeshEntityId entity;
            internal int worldId;
            internal uint epoch;
            internal bool register;
        }
        internal static readonly List<MeshTask> MeshTasks = new List<MeshTask>(256);
        internal static void RegisterMesh(MeshComponent component, uint epoch) => MeshTasks.Add(new MeshTask { component = component, epoch = epoch, register = true });
        internal static void UnregisterMesh(MeshComponent component, MeshEntityId entity, int worldId) => MeshTasks.Add(new MeshTask { component = component, entity = entity, worldId = worldId });

        internal static List<RenderTask> GraphicsTasks = new List<RenderTask>(256);

        internal static void ProcessMeshTask(in MeshTask task, RenderContext context)
        {
            if (task.register) { if (task.component) task.component.CompleteRegistration(task.epoch); }
            else context.EnqueueEntityRemoval(task.entity, task.worldId);
        }

        public static void AddTask(RenderTask graphicsTask)
        {
            GraphicsTasks.Add(graphicsTask);
        }

        internal static void ClearTasks()
        {
            GraphicsTasks.Clear();
            MeshTasks.Clear();
        }

        internal static void ProcessTasks(RenderContext renderContext)
        {
            foreach (var task in MeshTasks)
            {
                ProcessMeshTask(task, renderContext);
            }

            for (int i = 0; i < GraphicsTasks.Count; ++i)
            {
                if (GraphicsTasks[i] != null)
                {
                    GraphicsTasks[i](renderContext);
                    GraphicsTasks[i] = null;
                }
            }
        }
    }

    public partial class RenderContext : IDisposable
    {
        private static readonly List<MeshComponent> s_DirtyMeshList = new List<MeshComponent>(256);
        private static readonly List<MeshComponent> s_TransformUpdates = new List<MeshComponent>(256);
        private readonly List<MeshEntityId> m_PendingRemovals = new List<MeshEntityId>(64);

        private List<TerrainComponent> m_TerrainList;
        private int m_WorldDecalCount;
        private bool m_WorldDecalsInitialized;
        private Dictionary<ulong, InfinityAdditionalLightData> m_LightList;

        internal LightContext lightContext;
        private readonly MeshWorld m_MeshWorld;
        internal ScriptableRenderContext scriptableRenderContext;

        public RenderContext(ResourcePool resourcePool, ComputeShader meshDrawShader)
        {
            m_LightList = new Dictionary<ulong, InfinityAdditionalLightData>(64);
            m_TerrainList = new List<TerrainComponent>(32);

            lightContext = new LightContext();
            m_MeshWorld = new MeshWorld(resourcePool, meshDrawShader);
            MeshComponent.ResyncAll();
        }

        public MeshWorld meshWorld => m_MeshWorld;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnqueueDirtyMesh(MeshComponent meshComponent)
        {
            if (ReferenceEquals(meshComponent, null))
            {
                return;
            }

            s_DirtyMeshList.Add(meshComponent);
        }

        internal static void EnqueueMeshTransform(MeshComponent mesh) => s_TransformUpdates.Add(mesh);

        /// <summary>Entities of another world (an earlier pipeline) are not this world's to remove.</summary>
        internal void EnqueueEntityRemoval(MeshEntityId entity, int worldId)
        {
            if (entity.IsValid && worldId == m_MeshWorld.worldId) m_PendingRemovals.Add(entity);
        }

        /// <summary>
        /// One structural transaction per frame: pending removals, then every dirty component. Components publish their
        /// staged state only after the commit; on failure the world rolls back, the components abandon their staged state,
        /// and the queues stay for the next frame while the original exception propagates.
        /// </summary>
        internal void DrainDirtyMeshes()
        {
            if (s_DirtyMeshList.Count == 0 && m_PendingRemovals.Count == 0) return;
            bool committed = false;
            try
            {
                using (MeshWorldUpdate update = m_MeshWorld.BeginUpdate())
                {
                    foreach (MeshEntityId entity in m_PendingRemovals)
                        if (m_MeshWorld.IsAlive(entity)) update.RemoveEntity(entity);
                    foreach (MeshComponent meshComponent in s_DirtyMeshList)
                        if (meshComponent) meshComponent.Sync(m_MeshWorld, update);
                    update.Commit();
                    committed = true;
                }
            }
            finally
            {
                foreach (MeshComponent meshComponent in s_DirtyMeshList)
                {
                    if (!meshComponent) continue;
                    if (committed) meshComponent.PublishSync();
                    else meshComponent.AbandonSync();
                }
                if (committed)
                {
                    m_PendingRemovals.Clear();
                    s_DirtyMeshList.Clear();
                }
            }
        }

        /// <summary>Explicit static pose notifications, written after the structural transaction.</summary>
        internal void ApplyMeshTransforms()
        {
            using var measurement = new PipelineFrameMeasurement.Section(1);
            foreach (MeshComponent mesh in s_TransformUpdates)
                if (mesh) mesh.ApplyStaticPose(m_MeshWorld);
            s_TransformUpdates.Clear();
        }

        internal void UpdateDynamicMeshPoses()
        {
            using var measurement = new PipelineFrameMeasurement.Section(0);
            m_MeshWorld.UpdateDynamicPoses();
        }

        #region WorldLight
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldLight(in ulong id, InfinityAdditionalLightData lightComponent)
        {
            m_LightList.TryAdd(id, lightComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldLight(in ulong id)
        {
            m_LightList.Remove(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Dictionary<ulong, InfinityAdditionalLightData> GetWorldLight()
        {
            return m_LightList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearWorldLight()
        {
            m_LightList.Clear();
        }
        #endregion //WorldLight

        #region WorldTerrain
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldTerrain(TerrainComponent terrainComponent)
        {
            m_TerrainList.Add(terrainComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldTerrain(TerrainComponent terrainComponent)
        {
            m_TerrainList.Remove(terrainComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<TerrainComponent> GetWorldTerrains()
        {
            return m_TerrainList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearWorldTerrains()
        {
            m_TerrainList.Clear();
        }
        #endregion //WorldTerrain


        #region WorldDecal
        public int WorldDecalCount => m_WorldDecalCount;

        // A pipeline replacement does not re-enable the loaded scene's components. Reconcile once,
        // after pending registration tasks were consumed, so those tasks cannot double-count decals.
        internal void InitializeWorldDecals()
        {
            if (m_WorldDecalsInitialized) return;
            m_WorldDecalCount = 0;
            foreach (DecalComponent decal in Resources.FindObjectsOfTypeAll<DecalComponent>())
                if (decal.isActiveAndEnabled && decal.gameObject.scene.IsValid() && decal.gameObject.scene.isLoaded)
                    m_WorldDecalCount++;
            m_WorldDecalsInitialized = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldDecal()
        {
            m_WorldDecalCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldDecal()
        {
            m_WorldDecalCount = m_WorldDecalCount > 0 ? m_WorldDecalCount - 1 : 0;
        }
        #endregion //WorldDecal


        public void Dispose()
        {
            ClearWorldLight();
            ClearWorldTerrains();
            foreach (MeshComponent mesh in s_TransformUpdates) if (mesh) mesh.transformPending = false;
            s_TransformUpdates.Clear();
            s_DirtyMeshList.Clear();
            m_PendingRemovals.Clear();

            RenderTaskQueue.ClearTasks();

            lightContext.Dispose();
            m_MeshWorld.Dispose();
        }
    }
}
