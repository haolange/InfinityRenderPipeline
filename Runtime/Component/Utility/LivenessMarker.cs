using UnityEngine;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [AddComponentMenu("InfinityRenderer/Liveness Marker")]
    public class LivenessMarker : MonoBehaviour
    {
        void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            transform.Rotate(0.0f, 90.0f * Time.deltaTime, 0.0f);
        }
    }
}
