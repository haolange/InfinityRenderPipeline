using UnityEngine;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [AddComponentMenu("InfinityRenderer/Temporal Validation Camera")]
    public class TemporalValidationCamera : MonoBehaviour
    {
        public float amplitude = 1.5f;
        public float period = 4.0f;

        Vector3 m_Origin;
        bool m_HasOrigin;

        void OnEnable()
        {
            m_Origin = transform.position;
            m_HasOrigin = true;
        }

        void Update()
        {
            if (!m_HasOrigin)
            {
                m_Origin = transform.position;
                m_HasOrigin = true;
            }

            if (!Application.isPlaying)
            {
                return;
            }

            float phase = (period > 1e-3f) ? (Time.time * (2.0f * Mathf.PI / period)) : 0.0f;
            transform.position = m_Origin + transform.right * (Mathf.Sin(phase) * amplitude);
        }
    }
}
