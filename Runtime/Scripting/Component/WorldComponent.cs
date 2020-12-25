using UnityEngine;
using InfinityTech.Runtime.Rendering.Core;

namespace InfinityTech.Runtime.Component
{
    [ExecuteAlways]
    [ExecuteInEditMode]
    [AddComponentMenu("InfinityRender/WorldComponent")]
    public class WorldComponent : MonoBehaviour
    {
        public bool bUpdateStatic = true;

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
