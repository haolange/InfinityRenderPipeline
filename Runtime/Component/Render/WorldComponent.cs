using UnityEngine;
using InfinityTech.Rendering.Core;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [ExecuteInEditMode]
    [AddComponentMenu("InfinityRenderer/World Component")]
    public class WorldComponent : MonoBehaviour
    {
        public bool bUpdateStatic;

        private bool bInit;
        private FRenderWorld m_RenderWorld;


        void OnEnable()
        {
            bInit = true;
            bUpdateStatic = true;

            m_RenderWorld = new FRenderWorld("RenderScene");
            m_RenderWorld.Initializ();
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
                m_RenderWorld.InvokeWorldStaticMeshUpdate();
            }

            m_RenderWorld.InvokeWorldDynamicMeshUpdate();
        }

        protected void InvokeEventTickRuntime()
        {
            if(bInit == true)
            {
                bInit = false;
                m_RenderWorld.InvokeWorldStaticMeshUpdate();
            }

            m_RenderWorld.InvokeWorldDynamicMeshUpdate();
        }

        void OnDisable()
        {
            m_RenderWorld.Release();
            m_RenderWorld.Dispose();
        }

        protected FRenderWorld GetWorld()
        {
            if (FRenderWorld.RenderWorld != null)
            {
                return FRenderWorld.RenderWorld;
            }

            return null;
        }
    }
}
