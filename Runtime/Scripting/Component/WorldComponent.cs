using Unity.Jobs;
using UnityEngine;
using System.Collections.Generic;
using InfinityTech.Runtime.Rendering.Core;
using InfinityTech.Runtime.Rendering.MeshDrawPipeline;

namespace InfinityTech.Runtime.Component
{
    [ExecuteAlways]
    [ExecuteInEditMode]
    [AddComponentMenu("InfinityRender/WorldComponent")]
    public class WorldComponent : MonoBehaviour
    {
        [HideInInspector]
        public bool bInit;

        [HideInInspector]
        public RenderWorld RenderScene;

        void OnEnable()
        {
            bInit = true;
            RenderScene = new RenderWorld("RenderScene");
            RenderScene.Initializ();
        }

        void Update()
        {
            InvokeEventTick();
            GatherStaticMeshBatch();
            //GatherDynamicMeshBatch();
        }

        protected void InvokeEventTick()
        {
           #if UNITY_EDITOR
                InvokeEventTickEditor();
           #else
                InvokeEventTickRuntime();
           #endif
        }

        protected void InvokeEventTickEditor()
        {
            RenderScene.InvokeWorldViewEventTick();
            RenderScene.InvokeWorldMeshEventTick();
            RenderScene.InvokeWorldLightEventTick();
        }

        protected void InvokeEventTickRuntime()
        {
            if(bInit == false)
                return;

            bInit = false;
            //float InvokStartTime = Time.realtimeSinceStartup;
            RenderScene.InvokeWorldViewEventTick();
            RenderScene.InvokeWorldMeshEventTick();
            RenderScene.InvokeWorldLightEventTick();
            //float InvokEndTime = (Time.realtimeSinceStartup - InvokStartTime) * 1000;
            //Debug.Log("InvokTime : " + InvokEndTime + "ms");
        }

        protected void GatherStaticMeshBatch()
        {
            GetWorld().GetMeshBatchColloctor().ResetDynamicCollector();
            GetWorld().GetMeshBatchColloctor().CopyStaticToDynamic();
        }

        protected void GatherDynamicMeshBatch()
        {
            List<MeshComponent> MeshList = GetWorld().GetWorldPrimitive();
            for (int PrimitiveID = 0; PrimitiveID < MeshList.Count; PrimitiveID++)
            {
                MeshComponent Mesh = MeshList[PrimitiveID];
                if (Mesh.GeometryState == EStateType.Dynamic) 
                {
                    Mesh.GetDynamicMeshBatch(GetWorld().GetMeshBatchColloctor());
                }
            }
        }

        void OnDisable()
        {
            RenderScene.Release();
            RenderScene.Dispose();
        }

        protected RenderWorld GetWorld()
        {
            if (RenderWorld.ActiveWorld != null)
            {
                return RenderWorld.ActiveWorld;
            }

            return null;
        }
    }
}
