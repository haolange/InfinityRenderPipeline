using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Editor
{
    // Project and scene configuration checks with Fix / Fix All (InfinityWizardChecks). Checks only read; every change
    // is an explicit button press.
    internal sealed class InfinityRenderPipelineWizard : EditorWindow
    {
        Vector2 m_Scroll;

        [MenuItem("Window/Infinity/Render Pipeline Wizard", false, 0)]
        static void Open() => GetWindow<InfinityRenderPipelineWizard>("Infinity Wizard");

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Checks read the project and the loaded scenes. Fix applies one correction; Fix All applies every available correction.", MessageType.None);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawScope(EWizardScope.Project, "Project");
            DrawScope(EWizardScope.Scene, "Loaded scenes");
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("Fix All"))
            {
                foreach (InfinityWizardCheck check in InfinityWizardChecks.All)
                {
                    if (check.fix != null && !check.isSatisfied())
                        check.fix();
                }
            }
        }

        static void DrawScope(EWizardScope scope, string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            foreach (InfinityWizardCheck check in InfinityWizardChecks.All)
            {
                if (check.scope != scope) continue;
                bool satisfied = check.isSatisfied();
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUIContent icon = EditorGUIUtility.IconContent(satisfied ? "TestPassed" : "console.warnicon.sml");
                    GUILayout.Label(icon, GUILayout.Width(20));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(check.title, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(check.description, EditorStyles.wordWrappedMiniLabel);
                    }
                    using (new EditorGUI.DisabledScope(satisfied || check.fix == null))
                    {
                        if (GUILayout.Button("Fix", GUILayout.Width(48)))
                        {
                            check.fix();
                        }
                    }
                }
            }
        }
    }
}
