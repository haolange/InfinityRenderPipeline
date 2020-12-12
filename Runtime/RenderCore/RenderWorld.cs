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
        private List<MeshComponent> WorldPrimitiveList;

        private FMeshBatchCollector MeshBatchCollector;


        //Function
        public RenderWorld(string InName)
        {
            name = InName;
            ActiveWorld = this;

            WorldMeshList = new SharedRefFactory<Mesh>(128);
            WorldMaterialList = new SharedRefFactory<Material>(128);

            WorldViewList = new List<CameraComponent>(16);
            WorldLightList = new List<LightComponent>(32);
            WorldPrimitiveList = new List<MeshComponent>(128);

            MeshBatchCollector = new FMeshBatchCollector();
        }

        #region WorldView
        public void AddWorldView(CameraComponent InViewComponent)
        {
            WorldViewList.Add(InViewComponent);
        }

        public void InvokeWorldViewEventTick()
        {
            for (int i = 0; i < WorldViewList.Count; i++)
            {
                WorldViewList[i].EventUpdate();
            }
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

        public void InvokeWorldLightEventTick()
        {
            for (int i = 0; i < WorldLightList.Count; i++)
            {
                WorldLightList[i].EventUpdate();
            }
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
        public void AddWorldPrimitive(MeshComponent InMeshComponent)
        {
            WorldPrimitiveList.Add(InMeshComponent);
        }

        public void InvokeWorldMeshEventTick()
        {
            for (int i = 0; i < WorldPrimitiveList.Count; i++)
            {
                WorldPrimitiveList[i].EventUpdate();
            }
        }

        public void RemoveWorldPrimitive(MeshComponent InMeshComponent)
        {
            WorldPrimitiveList.Remove(InMeshComponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<MeshComponent> GetWorldPrimitive()
        {
            return WorldPrimitiveList;
        }

        public void ClearWorldPrimitive()
        {
            WorldPrimitiveList.Clear();
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
            ClearWorldPrimitive();

            WorldMeshList.Reset();
            WorldMaterialList.Reset();

            MeshBatchCollector.Initializ();
            MeshBatchCollector.Reset();
        }

        public void Release()
        {
            ClearWorldView();
            ClearWorldLight();
            ClearWorldPrimitive();

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
