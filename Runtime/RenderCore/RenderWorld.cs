using System;
using UnityEngine;
using InfinityTech.Runtime.Core;
using System.Collections.Generic;
using InfinityTech.Runtime.Component;
using System.Runtime.CompilerServices;
using InfinityTech.Runtime.Rendering.MeshDrawPipeline;

namespace InfinityTech.Runtime.Rendering.Core
{
    [Serializable]
    public class RenderWorld : UObject
    {
        public static RenderWorld ActiveWorld { get; private set; }

        public string name;

        public SharedRefFactory<Mesh> WorldMeshList;
        public SharedRefFactory<Material> WorldMaterialList;

        private List<CameraComponent> WorldViewList;
        private List<LightComponent> WorldLightList;
        private List<MeshComponent> WorldStaticPrimitiveList;
        private List<MeshComponent> WorldDynamicPrimitiveList;

        private FMeshBatchCollector MeshBatchCollector;


        //Function
        public RenderWorld(string InName)
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
