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

        public FResourceFactory resourceFactory;

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
        public void AddWorldView(CameraComponent InViewComponent)
        {
            m_ViewList.Add(InViewComponent);
        }

        public void RemoveWorldView(CameraComponent InViewComponent)
        {
            if(bDisable == true) { return; }
            m_ViewList.Remove(InViewComponent);
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
        public void AddWorldLight(LightComponent InLightComponent)
        {
            m_LightList.Add(InLightComponent);
        }

        public void RemoveWorldLight(LightComponent InLightComponent)
        {
            if(bDisable == true) { return; }
            m_LightList.Remove(InLightComponent);
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
        public void AddWorldTerrain(TerrainComponent InTerrainComponent)
        {
            m_TerrainList.Add(InTerrainComponent);
        }

        public void RemoveWorldTerrain(TerrainComponent InTerrainComponent)
        {
            if (bDisable == true) { return; }
            m_TerrainList.Remove(InTerrainComponent);
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
        public void AddWorldStaticMesh(MeshComponent InMeshComponent)
        {
            m_StaticMeshList.Add(InMeshComponent);
        }

        public void InvokeWorldStaticMeshUpdate()
        {
            if(m_StaticMeshList.Count == 0) { return; }

            for (int i = 0; i < m_StaticMeshList.Count; i++)
            {
                m_StaticMeshList[i].EventUpdate();
            }
        }

        public void RemoveWorldStaticMesh(MeshComponent InMeshComponent)
        {
            if(bDisable == true) { return; }
            m_StaticMeshList.Remove(InMeshComponent);
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
        public void AddWorldDynamicMesh(MeshComponent InMeshComponent)
        {
            m_DynamicMeshList.Add(InMeshComponent);
        }

        public void InvokeWorldDynamicMeshUpdate()
        {
            if (m_DynamicMeshList.Count == 0) { return; }

            for (int i = 0; i < m_DynamicMeshList.Count; i++)
            {
                m_DynamicMeshList[i].EventUpdate();
            }
        }

        public void RemoveWorldDynamicMesh(MeshComponent InMeshComponent)
        {
            if(bDisable == true) { return; }
            m_DynamicMeshList.Remove(InMeshComponent);
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

            meshAssets.Reset();
            materialAssets.Reset();

            m_MeshBatchCollector.Initializ();
            m_MeshBatchCollector.Reset();

            resourceFactory = new FResourceFactory();
        }

        public void Release()
        {
            bDisable = true;
            
            ClearWorldView();
            ClearWorldLight();
            ClearWorldTerrains();
            ClearWorldStaticMesh();
            ClearWorldDynamicMesh();

            meshAssets.Reset();
            materialAssets.Reset();
            resourceFactory.Disposed();
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
