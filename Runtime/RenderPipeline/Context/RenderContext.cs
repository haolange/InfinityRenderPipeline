using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public delegate void FGraphicsTask(RenderContext renderContext);

    public static class FGraphics
    {
        internal struct MeshTask
        {
            internal MeshComponent component;
            internal MeshInstanceId instance;
            internal EStateType mobility;
            internal uint epoch;
            internal bool register;
        }
        internal static readonly List<MeshTask> MeshTasks = new List<MeshTask>(256);
        internal static void RegisterMesh(MeshComponent component, uint epoch) => MeshTasks.Add(new MeshTask { component = component, epoch = epoch, register = true });
        internal static void UnregisterMesh(MeshComponent component, MeshInstanceId instance, EStateType mobility) => MeshTasks.Add(new MeshTask { component = component, instance = instance, mobility = mobility });

        internal static List<FGraphicsTask> GraphicsTasks = new List<FGraphicsTask>(256);

        internal static void ProcessMeshTask(in MeshTask task, RenderContext context)
        {
            if (task.register) { if (task.component) task.component.CompleteRegistration(context, task.epoch); }
            else MeshComponent.CompleteUnregistration(context, task.component, task.instance, task.mobility);
        }

        public static void AddTask(FGraphicsTask graphicsTask)
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

        private List<TerrainComponent> m_TerrainList;
        private List<MeshComponent> m_StaticMeshList;
        private List<MeshComponent> m_DynamicMeshList;
        private int m_WorldDecalCount;
        private bool m_WorldDecalsInitialized;
        private Dictionary<ulong, InfinityAdditionalLightData> m_LightList;

        internal LightContext lightContext;
        private MeshScene m_MeshScene;
        internal ScriptableRenderContext scriptableRenderContext;

        public RenderContext()
        {
            m_LightList = new Dictionary<ulong, InfinityAdditionalLightData>(64);
            m_TerrainList = new List<TerrainComponent>(32);
            m_StaticMeshList = new List<MeshComponent>(8192);
            m_DynamicMeshList = new List<MeshComponent>(8192);

            lightContext = new LightContext();
            m_MeshScene = new MeshScene();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnqueueDirtyMesh(MeshComponent meshComponent)
        {
            if (ReferenceEquals(meshComponent, null))
            {
                return;
            }

            s_DirtyMeshList.Add(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrainDirtyMeshes()
        {
            if (s_DirtyMeshList.Count == 0)
            {
                return;
            }

            for (int i = 0; i < s_DirtyMeshList.Count; ++i)
            {
                MeshComponent meshComponent = s_DirtyMeshList[i];
                if (meshComponent)
                {
                    meshComponent.SyncFromSnapshot(this);
                }
            }

            s_DirtyMeshList.Clear();
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

        #region WorldPrimitive
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldStaticMesh(MeshComponent meshComponent)
        {
            if (meshComponent.staticIndex >= 0) return;
            meshComponent.staticIndex = m_StaticMeshList.Count;
            m_StaticMeshList.Add(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeWorldStaticMeshUpdate()
        {
            if(m_StaticMeshList.Count == 0) { return; }

            for (int i = 0; i < m_StaticMeshList.Count; ++i)
            {
                m_StaticMeshList[i].NotifyInstanceChanged();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldStaticMesh(MeshComponent meshComponent)
        {
            int index = meshComponent.staticIndex;
            if (index < 0) return;
            int last = m_StaticMeshList.Count - 1;
            m_StaticMeshList[index] = m_StaticMeshList[last];
            m_StaticMeshList[index].staticIndex = index;
            m_StaticMeshList.RemoveAt(last);
            meshComponent.staticIndex = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldStaticMesh()
        {
            return m_StaticMeshList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearWorldStaticMesh()
        {
            foreach (var mesh in m_StaticMeshList) mesh.staticIndex = -1;
            m_StaticMeshList.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldDynamicMesh(MeshComponent meshComponent)
        {
            if (meshComponent.dynamicIndex >= 0) return;
            meshComponent.dynamicIndex = m_DynamicMeshList.Count;
            m_DynamicMeshList.Add(meshComponent);
            AddDynamicTransform(meshComponent);
        }

        readonly List<MeshComponent> m_ProbeBatch = new List<MeshComponent>();
        readonly HashSet<MeshComponent> m_ProbeRequests = new HashSet<MeshComponent>();

        // One light-probe interpolation for every mesh whose probe position moved or whose probe data changed this
        // frame, instead of one call per mesh (static meshes are sampled again only after a probe bake).
        internal void InterpolateMeshProbes()
        {
            using var measurement = new PipelineFrameMeasurement.Section(1);
            List<Vector3> positions = MeshBakedLighting.BatchPositions;
            positions.Clear();
            m_ProbeBatch.Clear();
            m_ProbeRequests.Clear();
            bool versionChanged = m_LastProbeVersion != MeshBakedLighting.ProbeVersion;
            var probes = LightmapSettings.lightProbes;
            bool probesAvailable = probes != null && probes.count > 0;
            if (versionChanged)
            {
                foreach (var mesh in m_StaticMeshList) mesh.RefreshLightingConfiguration(probesAvailable);
                foreach (var mesh in m_DynamicMeshList) mesh.RefreshLightingConfiguration(probesAvailable);
            }
            else foreach (var mesh in s_DirtyMeshList) if (mesh) mesh.RefreshLightingConfiguration(probesAvailable);
            if (versionChanged)
            {
                CollectProbeRequests(m_StaticMeshList, positions);
                CollectProbeRequests(m_DynamicMeshList, positions);
            }
            else
            {
                CollectProbeRequests(s_TransformUpdates, positions);
                CollectProbeRequests(s_DirtyMeshList, positions);
            }
            m_LastProbeVersion = MeshBakedLighting.ProbeVersion;
            if (positions.Count == 0) return;
            MeshBakedLighting.InterpolateProbes(positions, MeshBakedLighting.BatchProbes, MeshBakedLighting.BatchOcclusion);
            for (int i = 0; i < m_ProbeBatch.Count; ++i)
            {
                m_ProbeBatch[i].SetProbeSample(positions[i], MeshBakedLighting.BatchProbes[i], MeshBakedLighting.BatchOcclusion[i]);
            }
        }

        int m_LastProbeVersion = -1;
        void CollectProbeRequests(List<MeshComponent> meshes, List<Vector3> positions)
        {
            for (int i = 0; i < meshes.Count; ++i)
            {
                MeshComponent mesh = meshes[i];
                if (mesh != null && m_ProbeRequests.Add(mesh) && mesh.NeedsProbeSample(out Vector3 position))
                {
                    m_ProbeBatch.Add(mesh);
                    positions.Add(position);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeWorldDynamicMeshUpdate()
        {
            UpdateDynamicTransforms();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldDynamicMesh(MeshComponent meshComponent)
        {
            int index = meshComponent.dynamicIndex;
            if (index < 0) return;
            int last = m_DynamicMeshList.Count - 1;
            m_DynamicMeshList[index] = m_DynamicMeshList[last];
            m_DynamicMeshList[index].dynamicIndex = index;
            m_DynamicMeshList.RemoveAt(last);
            RemoveDynamicTransform(index, last);
            meshComponent.dynamicIndex = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldDynamicPrimitive()
        {
            return m_DynamicMeshList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearWorldDynamicMesh()
        {
            foreach (var mesh in m_DynamicMeshList) mesh.dynamicIndex = -1;
            m_DynamicMeshList.Clear();
            if (m_DynamicTransforms.isCreated) m_DynamicTransforms.SetTransforms(System.Array.Empty<Transform>());
        }
        #endregion //WorldPrimitive

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

        #region MeshScene
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MeshScene GetMeshScene()
        {
            return m_MeshScene;
        }
        #endregion //MeshScene

        public void Dispose()
        {
            DisposeTransformBatch();
            ClearWorldLight();
            ClearWorldTerrains();
            ClearWorldStaticMesh();
            ClearWorldDynamicMesh();
            s_DirtyMeshList.Clear();

            FGraphics.ClearTasks();

            lightContext.Dispose();
            m_MeshScene.Dispose();
        }
    }
}
