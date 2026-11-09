using UnityEngine;

namespace InfinityTech.Component
{
    /// <summary>Registers once per enable and unregisters once on disable or destroy.</summary>
    public class EntityComponent : MonoBehaviour
    {
        private bool m_Registered;

        private void OnEnable()
        {
            if (m_Registered)
            {
                return;
            }
            m_Registered = true;
            OnRegister();
        }

        private void OnDisable()
        {
            UnregisterOnce();
        }

        private void OnDestroy()
        {
            UnregisterOnce();
        }

        private void UnregisterOnce()
        {
            if (!m_Registered)
            {
                return;
            }
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
