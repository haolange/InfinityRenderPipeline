using UnityEngine;
using UnityEditor;
using InfinityTech.Rendering.Pipeline;
using UnityEngine.Rendering;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Camera))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class CameraOverrideEditor : CameraEditor
    {
        #region TargetObject
        CameraComponent m_CameraComponent;
        SerializedObject m_SerializeCamera;
        #endregion //TargetObject

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            /*m_SerializeLight.Update();
            m_LightComponent.OnGUIChange();
            m_SerializeLight.ApplyModifiedProperties();*/
        }

        public override void OnSceneGUI()
        {

        }

        private void InitSerializeComponent()
        {
            Camera camera = (Camera)target;
            m_CameraComponent = camera.gameObject.GetComponent<CameraComponent>();
            if (m_CameraComponent == null)
            {
                camera.gameObject.AddComponent<LightComponent>();
                m_CameraComponent = camera.gameObject.GetComponent<CameraComponent>();
            } else {
                m_CameraComponent = camera.gameObject.GetComponent<CameraComponent>();
            }

            m_SerializeCamera = new SerializedObject(m_CameraComponent);
        }

    }
}
