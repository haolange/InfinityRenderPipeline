using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    [CustomEditor(typeof(InfinityDefaultVolumeProfileSettings))]
    sealed class InfinityDefaultVolumeProfileSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty profile = serializedObject.FindProperty("volumeProfile");
            EditorGUILayout.PropertyField(profile, new GUIContent("Default Volume Profile"));
            VolumeProfile volume = profile.objectReferenceValue as VolumeProfile;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New"))
            {
                string path = EditorUtility.SaveFilePanelInProject("Create Default Volume Profile", "InfinityDefaultVolumeProfile", "asset", "Choose a project path.");
                if (!string.IsNullOrEmpty(path))
                {
                    VolumeProfile created = DefaultVolumeProfileFactory.CreateInMemory();
                    AssetDatabase.CreateAsset(created, path);
                    foreach (VolumeComponent component in created.components)
                    {
                        if (component != null)
                            AssetDatabase.AddObjectToAsset(component, created);
                    }
                    AssetDatabase.SaveAssets();
                    profile.objectReferenceValue = created;
                }
            }
            using (new EditorGUI.DisabledScope(volume == null))
            {
                if (GUILayout.Button("Clone") && volume != null)
                {
                    string path = EditorUtility.SaveFilePanelInProject("Clone Default Volume Profile", volume.name + " Copy", "asset", "Choose a project path.");
                    if (!string.IsNullOrEmpty(path))
                    {
                        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume), path);
                        profile.objectReferenceValue = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (volume != null)
            {
                if (GUILayout.Button("Validate & Complete"))
                {
                    DefaultVolumeProfileFactory.ValidateAndComplete(volume);
                    EditorUtility.SetDirty(volume);
                }

                if (GUILayout.Button("Reset to Packaged Defaults"))
                {
                    if (EditorUtility.DisplayDialog("Reset default Volume profile",
                        "This overwrites the assigned default profile with packaged film/grade values and completes the type registry. Continue?",
                        "Reset", "Cancel"))
                    {
                        DefaultVolumeProfileFactory.ValidateAndComplete(volume);
                        DefaultVolumeProfileFactory.ApplyPackagedDefaults(volume);
                        EditorUtility.SetDirty(volume);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    static class InfinityAssetMigrationMenus
    {
        [MenuItem("Window/Infinity/Migrate/Pipeline Asset Resources To GlobalSettings")]
        static void MigratePipelineAsset()
        {
            InfinityRenderPipelineAsset asset = Selection.activeObject as InfinityRenderPipelineAsset;
            if (asset == null)
            {
                asset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            }

            if (asset == null)
            {
                EditorUtility.DisplayDialog("InfinityRP", "Select or activate an InfinityRenderPipelineAsset.", "OK");
                return;
            }

            InfinityRenderPipelineGlobalSettings.Ensure();
            DefaultVolumeProfileFactory.AssignDefaultToGlobalSettings();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[InfinityRP] GlobalSettings resources and default Volume profile assigned. Example asset byte receipts still require the Mac migration protocol.");
        }
    }
}
