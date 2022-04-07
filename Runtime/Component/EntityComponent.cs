using UnityEngine;
using Unity.Mathematics;

namespace InfinityTech.Component
{
    internal struct RenderTransfrom
    {
        public float3 position;
        public quaternion rotation;
        public float3 scale;

        public override int GetHashCode()
        {
            return position.GetHashCode() + rotation.GetHashCode() + scale.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return Equals((RenderTransfrom)obj);
        }

        public bool Equals(RenderTransfrom target)
        {
            return position.Equals(target.position) && rotation.Equals(target.rotation) && scale.Equals(target.scale);
        }
    };

    public class EntityComponent : MonoBehaviour
    {
        private RenderTransfrom m_CurrTransform;
        private RenderTransfrom m_LastTransform;

        void OnEnable()
        {
            OnRegister();
            EventPlay();
        }

        public void EventUpdate()
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
    }
}
