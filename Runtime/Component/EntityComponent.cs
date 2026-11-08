using UnityEngine;

namespace InfinityTech.Component
{
    /// <summary>Registers once per enable and unregisters once on disable or destroy.</summary>
    public class EntityComponent : MonoBehaviour
    {
        private bool m_Registered;

        void OnEnable()
        {
            if (m_Registered) return;
            m_Registered = true;
            OnRegister();
        }

        void OnDisable()
        {
            UnregisterOnce();
        }

        void OnDestroy()
        {
            UnregisterOnce();
        }

        void UnregisterOnce()
        {
            if (!m_Registered) return;
            m_Registered = false;
            UnRegister();
        }

        protected virtual void OnRegister()
        {
        }

        protected virtual void UnRegister()
        {
        }
    }
}
