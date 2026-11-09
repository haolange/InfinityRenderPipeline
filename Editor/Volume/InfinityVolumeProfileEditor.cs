using InfinityTech.Rendering.Pipeline;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Editor
{
    [CustomEditor(typeof(VolumeProfile))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    internal sealed class InfinityVolumeProfileEditor : UnityEditor.Editor
    {
        VolumeComponentListEditor m_Components;

        void OnEnable()
        {
            // Reuse CoreRP component editors without VolumeProfileEditor.Init's automatic
            // EnsureAllOverridesForDefaultProfile mutation of the live default profile.
            m_Components = new VolumeComponentListEditor(this);
            m_Components.Init((VolumeProfile)target, serializedObject);
        }

        void OnDisable()
        {
            m_Components?.Clear();
            m_Components = null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            m_Components.OnGUI();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
