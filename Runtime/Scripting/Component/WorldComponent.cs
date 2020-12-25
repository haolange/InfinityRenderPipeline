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
            GatherMeshBatch();

            //print("Static : " + GetWorld().GetWorldStaticPrimitive().Count);
            //print("Dynamic : " + GetWorld().GetWorldDynamicPrimitive().Count);
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
            RenderScene.InvokeWorldStaticPrimitiveUpdate();
            RenderScene.InvokeWorldDynamicPrimitiveUpdate();
        }

        protected void InvokeEventTickRuntime()
        {
            if(bInit == true)
            {
                bInit = false;
                RenderScene.InvokeWorldStaticPrimitiveUpdate();
            }

            RenderScene.InvokeWorldDynamicPrimitiveUpdate();
        }

        protected void GatherMeshBatch()
        {
            GetWorld().GetMeshBatchColloctor().ResetDynamicCollector();
            GetWorld().GetMeshBatchColloctor().CopyStaticToDynamic();
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
