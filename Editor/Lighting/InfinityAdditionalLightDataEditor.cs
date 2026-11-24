using UnityEditor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InfinityAdditionalLightData))]
    internal sealed class InfinityAdditionalLightDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() { }
    }
}
