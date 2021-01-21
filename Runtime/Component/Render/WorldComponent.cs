using UnityEngine;
using InfinityTech.Rendering.Core;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [ExecuteInEditMode]
    [AddComponentMenu("InfinityRenderer/World Component")]
    public class WorldComponent : MonoBehaviour
    {
        public bool bUpdateStatic = true;

        [HideInInspector]
        public bool bInit;

        [HideInInspector]
        public FRenderWorld RenderScene;

        void OnEnable()
        {
            bInit = true;
            bUpdateStatic = true;
            
            RenderScene = new FRenderWorld("RenderScene");
            RenderScene.Initializ();
        }

        void Update()
        {
            InvokeEventTick();
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
            if(bUpdateStatic)
            {
                bUpdateStatic = false;
                RenderScene.InvokeWorldStaticPrimitiveUpdate();
            }

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

        void OnDisable()
        {
            RenderScene.Release();
            RenderScene.Dispose();
        }

        protected FRenderWorld GetWorld()
        {
            if (FRenderWorld.ActiveWorld != null)
            {
                return FRenderWorld.ActiveWorld;
            }

            return null;
        }
    }
}
