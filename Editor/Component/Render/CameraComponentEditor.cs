using UnityEditor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(CameraComponent))]
    public class CameraComponentEditor : UnityEditor.Editor
    {
        //Camera OriginCamera { get { return target as Camera; } }
        //InfinityCamera IntinityCamera;

        void OnEnable()
        {
            /*IntinityCamera = OriginCamera.gameObject.GetComponent<InfinityCamera>();
            if (IntinityCamera == null)
            {
                OriginCamera.gameObject.AddComponent<InfinityCamera>();
                IntinityCamera = OriginCamera.gameObject.GetComponent<InfinityCamera>();
            } else {
                IntinityCamera = OriginCamera.gameObject.GetComponent<InfinityCamera>();
            }*/
        }

        public override void OnInspectorGUI()
        {
            /*serializedObject.Update();

            serializedObject.ApplyModifiedProperties();*/
        }
    }
}
