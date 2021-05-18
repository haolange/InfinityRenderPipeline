using UnityEngine;
using InfinityTech.Rendering.Core;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [ExecuteInEditMode]
    [AddComponentMenu("InfinityRenderer/World Component")]
    public class WorldComponent : MonoBehaviour
    {
        public bool isUpdateStatic;

        private bool m_IsInit;
        private FRenderWorld m_RenderWorld;


        void OnEnable()
        {
            m_IsInit = true;
            isUpdateStatic = true;

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
            if(isUpdateStatic)
            {
                isUpdateStatic = false;
                m_RenderWorld.InvokeWorldStaticMeshUpdate();
            }

            m_RenderWorld.InvokeWorldDynamicMeshUpdate();
        }

        protected void InvokeEventTickRuntime()
        {
            if(m_IsInit == true)
            {
                m_IsInit = false;
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
