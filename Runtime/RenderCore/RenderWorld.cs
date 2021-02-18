using System;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Component;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Core
{
    //[Serializable]
    public class FRenderWorld : UObject
    {
        public static FRenderWorld ActiveWorld { get; private set; }

        public string name;
        public bool bDisable;
        public SharedRefFactory<Mesh> WorldMeshs;
        public SharedRefFactory<Material> WorldMaterials;

        private List<CameraComponent> WorldViews;
        private List<LightComponent> WorldLights;
        private List<TerrainComponent> WorldTerrains;
        private List<MeshComponent> WorldStaticPrimitives;
        private List<MeshComponent> WorldDynamicPrimitives;

        private FMeshBatchCollector MeshBatchCollector;


        //Function
        public FRenderWorld(string InName)
        {
            name = InName;
            ActiveWorld = this;

            WorldMeshs = new SharedRefFactory<Mesh>(256);
            WorldMaterials = new SharedRefFactory<Material>(256);

            WorldViews = new List<CameraComponent>(16);
            WorldLights = new List<LightComponent>(64);
            WorldTerrains = new List<TerrainComponent>(32);
            WorldStaticPrimitives = new List<MeshComponent>(1024);
            WorldDynamicPrimitives = new List<MeshComponent>(1024);

            MeshBatchCollector = new FMeshBatchCollector();
        }

        #region WorldView
        public void AddWorldView(CameraComponent InViewComponent)
        {
            WorldViews.Add(InViewComponent);
        }

        public void RemoveWorldView(CameraComponent InViewComponent)
        {
            if(bDisable == true) { return; }
            WorldViews.Remove(InViewComponent);
        }

        public List<CameraComponent> GetWorldView()
        {
            return WorldViews;
        }

        public void ClearWorldView()
        {
            WorldViews.Clear();
        }
        #endregion //WorldView

        #region WorldLight
        public void AddWorldLight(LightComponent InLightComponent)
        {
            WorldLights.Add(InLightComponent);
        }

        public void RemoveWorldLight(LightComponent InLightComponent)
        {
            if(bDisable == true) { return; }
            WorldLights.Remove(InLightComponent);
        }

        public List<LightComponent> GetWorldLight()
        {
            return WorldLights;
        }

        public void ClearWorldLight()
        {
            WorldLights.Clear();
        }
        #endregion //WorldLight

        #region WorldTerrain
        public void AddWorldTerrain(TerrainComponent InTerrainComponent)
        {
            WorldTerrains.Add(InTerrainComponent);
        }

        public void RemoveWorldTerrain(TerrainComponent InTerrainComponent)
        {
            if (bDisable == true) { return; }
            WorldTerrains.Remove(InTerrainComponent);
        }

        public List<TerrainComponent> GetWorldTerrains()
        {
            return WorldTerrains;
        }

        public void ClearWorldTerrains()
        {
            WorldTerrains.Clear();
        }
        #endregion //WorldTerrain

        #region WorldPrimitive
        //Static
        public void AddWorldStaticPrimitive(MeshComponent InMeshComponent)
        {
            WorldStaticPrimitives.Add(InMeshComponent);
        }

        public void InvokeWorldStaticPrimitiveUpdate()
        {
            if(WorldStaticPrimitives.Count == 0) { return; }

            for (int i = 0; i < WorldStaticPrimitives.Count; i++)
            {
                WorldStaticPrimitives[i].EventUpdate();
            }
        }

        public void RemoveWorldStaticPrimitive(MeshComponent InMeshComponent)
        {
            if(bDisable == true) { return; }
            WorldStaticPrimitives.Remove(InMeshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldStaticPrimitive()
        {
            return WorldStaticPrimitives;
        }

        public void ClearWorldStaticPrimitive()
        {
            WorldStaticPrimitives.Clear();
        }

        //Dynamic
        public void AddWorldDynamicPrimitive(MeshComponent InMeshComponent)
        {
            WorldDynamicPrimitives.Add(InMeshComponent);
        }

        public void InvokeWorldDynamicPrimitiveUpdate()
        {
            if (WorldDynamicPrimitives.Count == 0) { return; }

            for (int i = 0; i < WorldDynamicPrimitives.Count; i++)
            {
                WorldDynamicPrimitives[i].EventUpdate();
            }
        }

        public void RemoveWorldDynamicPrimitive(MeshComponent InMeshComponent)
        {
            if(bDisable == true) { return; }
            WorldDynamicPrimitives.Remove(InMeshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldDynamicPrimitive()
        {
            return WorldDynamicPrimitives;
        }

        public void ClearWorldDynamicPrimitive()
        {
            WorldDynamicPrimitives.Clear();
        }
        #endregion //WorldPrimitive

        #region MeshBatchCollector
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FMeshBatchCollector GetMeshBatchColloctor()
        {
            return MeshBatchCollector;
        }
        #endregion //MeshBatchCollector

        public void Initializ()
        {
            bDisable = false;

            ClearWorldView();
            ClearWorldLight();
            ClearWorldTerrains();
            ClearWorldStaticPrimitive();
            ClearWorldDynamicPrimitive();

            WorldMeshs.Reset();
            WorldMaterials.Reset();

            MeshBatchCollector.Initializ();
            MeshBatchCollector.Reset();
        }

        public void Release()
        {
            bDisable = true;
            
            ClearWorldView();
            ClearWorldLight();
            ClearWorldTerrains();
            ClearWorldStaticPrimitive();
            ClearWorldDynamicPrimitive();

            WorldMeshs.Reset();
            WorldMaterials.Reset();

            MeshBatchCollector.Release();
        }

        protected override void DisposeManaged()
        {

        }

        protected override void DisposeUnManaged()
        {
            
        }
    }
}
