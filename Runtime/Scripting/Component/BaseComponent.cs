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
        // Public Variable
        [HideInInspector]
        public Transform EntityTransform;

        [HideInInspector]
        internal RenderTransfrom CurrTransform;

        [HideInInspector]
        internal RenderTransfrom LastTransform;


        // Function
        public BaseComponent() { }

        void OnEnable()
        {
            EntityTransform = GetComponent<Transform>();
            OnRigister();
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
            UnRigister();
        }

        private bool TransfromStateDirty()
        {
            CurrTransform.position = EntityTransform.position;
            CurrTransform.rotation = EntityTransform.rotation;
            CurrTransform.scale = EntityTransform.localScale;

            if (CurrTransform.Equals(LastTransform))
            {
                LastTransform = CurrTransform;
                return true;
            }

            return false;
        }

        protected virtual void OnRigister()
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

        protected virtual void UnRigister()
        {

        }

        protected FRenderWorld GetWorld()
        {
            if (FRenderWorld.ActiveWorld != null)
            {
                return FRenderWorld.ActiveWorld;
            }

            return null;
        }
    }
}
