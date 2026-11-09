#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace InfinityTech.Component
{
    [ExecuteInEditMode]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class BaseComponent : MonoBehaviour
    {
        void OnEnable()
        {
            OnRegister();
        }

        void OnDisable()
        {
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
