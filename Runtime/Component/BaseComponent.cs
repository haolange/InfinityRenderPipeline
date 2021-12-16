using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Core;

namespace InfinityTech.Component
{
    [ExecuteInEditMode]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class BaseComponent : MonoBehaviour
    {
        private RenderTransfrom m_CurrTransform;
        private RenderTransfrom m_LastTransform;

        void OnEnable()
        {
            OnRegister();
            EventPlay();
        }

        void Update()
        {
            if (TransfromStateDirty())
            {
                OnTransformChange();
            }

            EventTick();
        }

        void OnDisable()
        {
            UnRegister();
        }

        private bool TransfromStateDirty()
        {
            m_CurrTransform.position = transform.position;
            m_CurrTransform.rotation = transform.rotation;
            m_CurrTransform.scale = transform.localScale;

            if (m_CurrTransform.Equals(m_LastTransform)) { return false; }
            m_LastTransform = m_CurrTransform;
            return true;
        }

        protected virtual void OnRegister()
        {

        }

        protected virtual void EventPlay()
        {

        }

        protected virtual void EventTick()
        {

        }

        protected virtual void OnTransformChange()
        {

        }

        protected virtual void UnRegister()
        {

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
