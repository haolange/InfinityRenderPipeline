using UnityEditor;
using UnityEngine;
using InfinityTech.Runtime.Rendering.Core;

namespace InfinityTech.Runtime.Component
{
    internal struct RenderTransfrom
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public override int GetHashCode()
        {
            return position.GetHashCode() + rotation.GetHashCode() + scale.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            RenderTransfrom target = (RenderTransfrom)obj;
            return position != target.position || rotation != target.rotation || scale != target.scale;
        }

        public bool Equals(RenderTransfrom target)
        {
            return position != target.position || rotation != target.rotation || scale != target.scale;
        }
    };

    [ExecuteInEditMode]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class EntityComponent : MonoBehaviour
    {
        // Public Variable
        [HideInInspector]
        public Transform MeshTransform;

        [HideInInspector]
        internal RenderTransfrom CurrTransform;

        [HideInInspector]
        internal RenderTransfrom LastTransform;


        // Function
        public EntityComponent() { }

        void OnEnable()
        {
            MeshTransform = GetComponent<Transform>();
            OnRigister();
            EventPlay();
        }

        public void EventUpdate()
        {
            if(TransfromStateDirty()) {
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
            CurrTransform.position = MeshTransform.position;
            CurrTransform.rotation = MeshTransform.rotation;
            CurrTransform.scale = MeshTransform.localScale;

            if (CurrTransform.Equals(LastTransform)) {
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

        protected RenderWorld GetWorld()
        {
            if(RenderWorld.ActiveWorld != null) {
                return RenderWorld.ActiveWorld;
            }

            return null;
        }
    }
}
