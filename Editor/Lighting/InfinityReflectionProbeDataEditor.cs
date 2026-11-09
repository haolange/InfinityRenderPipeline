using UnityEditor;
using UnityEngine;
using InfinityTech.Component;

namespace InfinityTech.Component.Editor
{
    // Infinity reflection probe data: the sphere influence (with a Scene view radius handle). Box influence, projection
    // box and blend distance stay on Unity's Reflection Probe component and its handles.
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InfinityAdditionalReflectionProbeData))]
    public sealed class InfinityReflectionProbeDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty influence = serializedObject.FindProperty("influence");
            EditorGUILayout.PropertyField(influence, new GUIContent("Influence"));
            if (influence.hasMultipleDifferentValues || influence.enumValueIndex == (int)EReflectionProbeInfluence.Sphere)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sphereRadius"), new GUIContent("Sphere Radius", "0 uses the largest half extent of the probe box."));
                EditorGUILayout.HelpBox("Unity culls probes by their box; keep the sphere inside the box so the probe is not culled while visible.", MessageType.None);
            }
            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            var data = (InfinityAdditionalReflectionProbeData)target;
            if (data.influence != EReflectionProbeInfluence.Sphere || !data.TryGetComponent(out ReflectionProbe probe))
                return;
            Vector3 center = probe.transform.position + probe.center;
            float radius = data.sphereRadius > 0 ? data.sphereRadius : Mathf.Max(probe.size.x, Mathf.Max(probe.size.y, probe.size.z)) * 0.5f;
            using (new Handles.DrawingScope(new Color(0.4f, 0.8f, 1.0f, 1.0f)))
            {
                EditorGUI.BeginChangeCheck();
                float next = Handles.RadiusHandle(Quaternion.identity, center, radius);
                if (probe.blendDistance > 0)
                {
                    using (new Handles.DrawingScope(new Color(0.4f, 0.8f, 1.0f, 0.35f)))
                        Handles.RadiusHandle(Quaternion.identity, center, radius + probe.blendDistance, true);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(data, "Probe Sphere Radius");
                    data.sphereRadius = Mathf.Max(0.01f, next);
                }
            }
        }
    }
}
