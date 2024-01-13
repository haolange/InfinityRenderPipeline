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
        internal static List<FGraphicsTask> GraphicsTasks = new List<FGraphicsTask>(256);

        public static void AddTask(FGraphicsTask graphicsTask)
        {
            GraphicsTasks.Add(graphicsTask);
        }

        internal static void ClearGraphicsTasks()
        {
            GraphicsTasks.Clear();
        }

        internal static void ProcessGraphicsTasks(RenderContext renderContext)
        {
            if(GraphicsTasks.Count == 0) { return; }
            Debug.Log(GraphicsTasks.Count);
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

    public class RenderContext : IDisposable
    {
        private List<CameraComponent> m_ViewList;
        private List<TerrainComponent> m_TerrainList;
        private List<MeshComponent> m_StaticMeshList;
        private List<MeshComponent> m_DynamicMeshList;
        private Dictionary<int, LightComponent> m_LightList;

        internal LightContext lightContext;
        private MeshBatchCollector m_MeshBatchCollector;
        internal ScriptableRenderContext scriptableRenderContext;

        public RenderContext()
        {
            m_ViewList = new List<CameraComponent>(16);
            m_LightList = new Dictionary<int, LightComponent>(64);
            m_TerrainList = new List<TerrainComponent>(32);
            m_StaticMeshList = new List<MeshComponent>(8192);
            m_DynamicMeshList = new List<MeshComponent>(8192);

            lightContext = new LightContext();
            m_MeshBatchCollector = new MeshBatchCollector();
        }

        #region WorldView
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldView(CameraComponent viewComponent)
        {
            m_ViewList.Add(viewComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldView(CameraComponent viewComponent)
        {
            m_ViewList.Remove(viewComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<CameraComponent> GetWorldView()
        {
            return m_ViewList;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearWorldView()
        {
            m_ViewList.Clear();
        }
        #endregion //WorldView

        #region WorldLight
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldLight(in int id, LightComponent lightComponent)
        {
            m_LightList.TryAdd(id, lightComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldLight(in int id)
        {
            m_LightList.Remove(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Dictionary<int, LightComponent> GetWorldLight()
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
            m_StaticMeshList.Add(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeWorldStaticMeshUpdate()
        {
            if(m_StaticMeshList.Count == 0) { return; }

            for (int i = 0; i < m_StaticMeshList.Count; ++i)
            {
                m_StaticMeshList[i].EventUpdate();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldStaticMesh(MeshComponent meshComponent)
        {
            m_StaticMeshList.Remove(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldStaticMesh()
        {
            return m_StaticMeshList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearWorldStaticMesh()
        {
            m_StaticMeshList.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWorldDynamicMesh(MeshComponent meshComponent)
        {
            m_DynamicMeshList.Add(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeWorldDynamicMeshUpdate()
        {
            if (m_DynamicMeshList.Count == 0) { return; }

            for (int i = 0; i < m_DynamicMeshList.Count; ++i)
            {
                m_DynamicMeshList[i].EventUpdate();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldDynamicMesh(MeshComponent meshComponent)
        {
            m_DynamicMeshList.Remove(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldDynamicPrimitive()
        {
            return m_DynamicMeshList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearWorldDynamicMesh()
        {
            m_DynamicMeshList.Clear();
        }
        #endregion //WorldPrimitive

        #region MeshBatchCollector
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MeshBatchCollector GetMeshBatchColloctor()
        {
            return m_MeshBatchCollector;
        }
        #endregion //MeshBatchCollector

        public void Dispose()
        {
            ClearWorldView();
            ClearWorldLight();
            ClearWorldTerrains();
            ClearWorldStaticMesh();
            ClearWorldDynamicMesh();

            lightContext.Dispose();
            m_MeshBatchCollector.Dispose();
            FGraphics.ClearGraphicsTasks();
        }
    }
}
