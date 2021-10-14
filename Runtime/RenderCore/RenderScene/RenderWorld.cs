using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Component;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Core
{
    public class FRenderWorld : Disposer
    {
        public static FRenderWorld RenderWorld { get; private set; }

        public string name;
        public bool bDisable;
        public SharedRefFactory<Mesh> meshAssets;
        public SharedRefFactory<Material> materialAssets;

        private List<CameraComponent> m_ViewList;
        private List<LightComponent> m_LightList;
        private List<TerrainComponent> m_TerrainList;
        private List<MeshComponent> m_StaticMeshList;
        private List<MeshComponent> m_DynamicMeshList;
        private FMeshBatchCollector m_MeshBatchCollector;

        public FRenderWorld(string name)
        {
            this.name = name;
            FRenderWorld.RenderWorld = this;
            this.m_ViewList = new List<CameraComponent>(16);
            this.meshAssets = new SharedRefFactory<Mesh>(512);
            this.m_LightList = new List<LightComponent>(64);
            this.m_TerrainList = new List<TerrainComponent>(32);
            this.materialAssets = new SharedRefFactory<Material>(512);
            this.m_StaticMeshList = new List<MeshComponent>(8192);
            this.m_DynamicMeshList = new List<MeshComponent>(8192);
            this.m_MeshBatchCollector = new FMeshBatchCollector();
        }

        #region WorldView
        public void AddWorldView(CameraComponent viewComponent)
        {
            m_ViewList.Add(viewComponent);
        }

        public void RemoveWorldView(CameraComponent viewComponent)
        {
            if(bDisable == true) { return; }
            m_ViewList.Remove(viewComponent);
        }

        public List<CameraComponent> GetWorldView()
        {
            return m_ViewList;
        }

        public void ClearWorldView()
        {
            m_ViewList.Clear();
        }
        #endregion //WorldView

        #region WorldLight
        public void AddWorldLight(LightComponent lightComponent)
        {
            m_LightList.Add(lightComponent);
        }

        public void RemoveWorldLight(LightComponent lightComponent)
        {
            if(bDisable == true) { return; }
            m_LightList.Remove(lightComponent);
        }

        public List<LightComponent> GetWorldLight()
        {
            return m_LightList;
        }

        public void ClearWorldLight()
        {
            m_LightList.Clear();
        }
        #endregion //WorldLight

        #region WorldTerrain
        public void AddWorldTerrain(TerrainComponent terrainComponent)
        {
            m_TerrainList.Add(terrainComponent);
        }

        public void RemoveWorldTerrain(TerrainComponent terrainComponent)
        {
            if (bDisable == true) { return; }
            m_TerrainList.Remove(terrainComponent);
        }

        public List<TerrainComponent> GetWorldTerrains()
        {
            return m_TerrainList;
        }

        public void ClearWorldTerrains()
        {
            m_TerrainList.Clear();
        }
        #endregion //WorldTerrain

        #region WorldPrimitive
        //Static
        public void AddWorldStaticMesh(MeshComponent meshComponent)
        {
            m_StaticMeshList.Add(meshComponent);
        }

        public void InvokeWorldStaticMeshUpdate()
        {
            if(m_StaticMeshList.Count == 0) { return; }

            for (int i = 0; i < m_StaticMeshList.Count; ++i)
            {
                m_StaticMeshList[i].EventUpdate();
            }
        }

        public void RemoveWorldStaticMesh(MeshComponent meshComponent)
        {
            if(bDisable == true) { return; }
            m_StaticMeshList.Remove(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldStaticMesh()
        {
            return m_StaticMeshList;
        }

        public void ClearWorldStaticMesh()
        {
            m_StaticMeshList.Clear();
        }

        //Dynamic
        public void AddWorldDynamicMesh(MeshComponent meshComponent)
        {
            m_DynamicMeshList.Add(meshComponent);
        }

        public void InvokeWorldDynamicMeshUpdate()
        {
            if (m_DynamicMeshList.Count == 0) { return; }

            for (int i = 0; i < m_DynamicMeshList.Count; ++i)
            {
                m_DynamicMeshList[i].EventUpdate();
            }
        }

        public void RemoveWorldDynamicMesh(MeshComponent meshComponent)
        {
            if(bDisable == true) { return; }
            m_DynamicMeshList.Remove(meshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldDynamicPrimitive()
        {
            return m_DynamicMeshList;
        }

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

        public void Initializ()
        {
            bDisable = false;

            ClearWorldView();
            ClearWorldLight();
            ClearWorldTerrains();
            ClearWorldStaticMesh();
            ClearWorldDynamicMesh();

            meshAssets.Clear();
            materialAssets.Clear();
            m_MeshBatchCollector.Initializ();
        }

        public void Release()
        {
            bDisable = true;
            
            ClearWorldView();
            ClearWorldLight();
            ClearWorldTerrains();
            ClearWorldStaticMesh();
            ClearWorldDynamicMesh();

            meshAssets.Clear();
            materialAssets.Clear();
            m_MeshBatchCollector.Release();
        }

        protected override void DisposeManaged()
        {

        }

        protected override void DisposeUnManaged()
        {
            
        }
    }
}
