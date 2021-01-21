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
        public SharedRefFactory<Mesh> WorldMeshList;
        public SharedRefFactory<Material> WorldMaterialList;

        private List<CameraComponent> WorldViewList;
        private List<LightComponent> WorldLightList;
        private List<MeshComponent> WorldStaticPrimitiveList;
        private List<MeshComponent> WorldDynamicPrimitiveList;

        private FMeshBatchCollector MeshBatchCollector;


        //Function
        public FRenderWorld(string InName)
        {
            name = InName;
            ActiveWorld = this;

            WorldMeshList = new SharedRefFactory<Mesh>(256);
            WorldMaterialList = new SharedRefFactory<Material>(256);

            WorldViewList = new List<CameraComponent>(16);
            WorldLightList = new List<LightComponent>(64);
            WorldStaticPrimitiveList = new List<MeshComponent>(1024);
            WorldDynamicPrimitiveList = new List<MeshComponent>(1024);

            MeshBatchCollector = new FMeshBatchCollector();
        }

        #region WorldView
        public void AddWorldView(CameraComponent InViewComponent)
        {
            WorldViewList.Add(InViewComponent);
        }

        public void RemoveWorldView(CameraComponent InViewComponent)
        {
            if(bDisable == true) { return; }
            WorldViewList.Remove(InViewComponent);
        }

        public List<CameraComponent> GetWorldView()
        {
            return WorldViewList;
        }

        public void ClearWorldView()
        {
            WorldViewList.Clear();
        }
        #endregion //WorldView

        #region WorldLight
        public void AddWorldLight(LightComponent InLightComponent)
        {
            WorldLightList.Add(InLightComponent);
        }

        public void RemoveWorldLight(LightComponent InLightComponent)
        {
            if(bDisable == true) { return; }
            WorldLightList.Remove(InLightComponent);
        }

        public List<LightComponent> GetWorldLight()
        {
            return WorldLightList;
        }

        public void ClearWorldLight()
        {
            WorldLightList.Clear();
        }
        #endregion //WorldLight

        #region WorldPrimitive
        //Static
        public void AddWorldStaticPrimitive(MeshComponent InMeshComponent)
        {
            WorldStaticPrimitiveList.Add(InMeshComponent);
        }

        public void InvokeWorldStaticPrimitiveUpdate()
        {
            if(WorldStaticPrimitiveList.Count == 0) { return; }

            for (int i = 0; i < WorldStaticPrimitiveList.Count; i++)
            {
                WorldStaticPrimitiveList[i].EventUpdate();
            }
        }

        public void RemoveWorldStaticPrimitive(MeshComponent InMeshComponent)
        {
            if(bDisable == true) { return; }
            WorldStaticPrimitiveList.Remove(InMeshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldStaticPrimitive()
        {
            return WorldStaticPrimitiveList;
        }

        public void ClearWorldStaticPrimitive()
        {
            WorldStaticPrimitiveList.Clear();
        }

        //Dynamic
        public void AddWorldDynamicPrimitive(MeshComponent InMeshComponent)
        {
            WorldDynamicPrimitiveList.Add(InMeshComponent);
        }

        public void InvokeWorldDynamicPrimitiveUpdate()
        {
            if (WorldDynamicPrimitiveList.Count == 0) { return; }

            for (int i = 0; i < WorldDynamicPrimitiveList.Count; i++)
            {
                WorldDynamicPrimitiveList[i].EventUpdate();
            }
        }

        public void RemoveWorldDynamicPrimitive(MeshComponent InMeshComponent)
        {
            if(bDisable == true) { return; }
            WorldDynamicPrimitiveList.Remove(InMeshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldDynamicPrimitive()
        {
            return WorldDynamicPrimitiveList;
        }

        public void ClearWorldDynamicPrimitive()
        {
            WorldDynamicPrimitiveList.Clear();
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
            ClearWorldStaticPrimitive();
            ClearWorldDynamicPrimitive();

            WorldMeshList.Reset();
            WorldMaterialList.Reset();

            MeshBatchCollector.Initializ();
            MeshBatchCollector.Reset();
        }

        public void Release()
        {
            bDisable = true;
            
            ClearWorldView();
            ClearWorldLight();
            ClearWorldStaticPrimitive();
            ClearWorldDynamicPrimitive();

            WorldMeshList.Reset();
            WorldMaterialList.Reset();

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
