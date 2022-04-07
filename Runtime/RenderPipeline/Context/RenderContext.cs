using System;
using UnityEngine;
using InfinityTech.Core;
using UnityEngine.Rendering;
using InfinityTech.Component;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public delegate void FGraphicsTask(FRenderContext renderContext);

    public static class FGraphics
    {
        internal static List<FGraphicsTask> GraphicsTasks = new List<FGraphicsTask>(256);

        public static void AddTask(FGraphicsTask graphicsTask)
        {
            GraphicsTasks.Add(graphicsTask);
        }

        internal static void ClearGraphicsTasks()
        {
            FGraphics.GraphicsTasks.Clear();
        }

        internal static void ProcessGraphicsTasks(FRenderContext renderContext)
        {
            if(FGraphics.GraphicsTasks.Count == 0) { return; }
            
            for (int i = 0; i < FGraphics.GraphicsTasks.Count; ++i) 
            {
                if (FGraphics.GraphicsTasks[i] != null) 
                {
                    FGraphics.GraphicsTasks[i](renderContext);
                    FGraphics.GraphicsTasks[i] = null;
                }
            }
        }
    }

    public class FRenderContext : IDisposable
    {
        public ScriptableRenderContext scriptableRenderContext;

        private List<CameraComponent> m_ViewList;
        private List<LightComponent> m_LightList;
        private List<TerrainComponent> m_TerrainList;
        private List<MeshComponent> m_StaticMeshList;
        private List<MeshComponent> m_DynamicMeshList;
        private FMeshBatchCollector m_MeshBatchCollector;

        public FRenderContext()
        {
            m_ViewList = new List<CameraComponent>(16);
            m_LightList = new List<LightComponent>(64);
            m_TerrainList = new List<TerrainComponent>(32);
            m_StaticMeshList = new List<MeshComponent>(8192);
            m_DynamicMeshList = new List<MeshComponent>(8192);
            m_MeshBatchCollector = new FMeshBatchCollector();
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
        public void AddWorldLight(LightComponent lightComponent)
        {
            m_LightList.Add(lightComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveWorldLight(LightComponent lightComponent)
        {
            m_LightList.Remove(lightComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<LightComponent> GetWorldLight()
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
        public FMeshBatchCollector GetMeshBatchColloctor()
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
            m_MeshBatchCollector.Dispose();
        }
    }
}
