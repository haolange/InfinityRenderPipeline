using System;
using System.IO;
using InfinityTech.Rendering.Pipeline;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace InfinityTech.Rendering.Editor
{
    // This drawer preserves the existing serialized field and uses CoreRP's profile editor.
    // The CoreRP default-profile drawer assumes a different schema and completes overrides on assignment.
    [CustomPropertyDrawer(typeof(InfinityDefaultVolumeProfileSettings))]
    sealed class InfinityDefaultVolumeProfileSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var slot = property.FindPropertyRelative("volumeProfile");
            var profileField = new PropertyField(slot, "Default Volume Profile");
            profileField.RegisterValueChangeCallback(_ => NotifySelectionChanged());
            root.Add(profileField);
            root.Add(new HelpBox("The default profile supplies pipeline-wide values. Assignment and inspection never reset or complete existing assets.", HelpBoxMessageType.Info));
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            root.Add(actions);
            actions.Add(new Button(() => CreateProfile(slot, false)) { text = "New" });
            actions.Add(new Button(() => CreateProfile(slot, true)) { text = "Clone" });
            actions.Add(new Button(() =>
            {
                var profile = slot.objectReferenceValue as VolumeProfile;
                if (profile == null) return;
                int count = DefaultVolumeProfileFactory.ValidateAndComplete(profile);
                Debug.Log($"[InfinityRP] Added {count} missing default Volume components; existing values and overrides preserved.");
            }) { text = "Validate & Complete" });
            actions.Add(new Button(() =>
            {
                var profile = slot.objectReferenceValue as VolumeProfile;
                if (profile == null) return;
                if (EditorUtility.DisplayDialog("Reset default Volume profile",
                    $"Reset all Infinity components in {AssetDatabase.GetAssetPath(profile)} to packaged defaults? Existing subasset identities and unrelated components are preserved. A current-byte backup is created before changing values; the operation supports Undo.", "Reset", "Cancel"))
                    InfinityVolumeProfileAuthoring.Reset(profile);
            }) { text = "Reset to Packaged Defaults" });

            UnityEditor.Editor profileEditor = null;
            VolumeProfile edited = null;
            var inspector = new IMGUIContainer(() =>
            {
                var profile = slot.objectReferenceValue as VolumeProfile;
                if (edited != profile)
                {
                    if (profileEditor != null) UnityEngine.Object.DestroyImmediate(profileEditor);
                    profileEditor = profile == null ? null : UnityEditor.Editor.CreateEditor(profile, typeof(InfinityVolumeProfileEditor));
                    edited = profile;
                }
                profileEditor?.OnInspectorGUI();
            });
            inspector.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (profileEditor != null) UnityEngine.Object.DestroyImmediate(profileEditor);
                profileEditor = null;
                edited = null;
            });
            root.Add(inspector);
            return root;
        }

        static void NotifySelectionChanged()
        {
            if (GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset asset)
                asset.RequestEditorRecreation();
        }

        static void CreateProfile(SerializedProperty slot, bool clone)
        {
            var current = slot.objectReferenceValue as VolumeProfile;
            if (clone && current == null) return;
            string path = EditorUtility.SaveFilePanelInProject(clone ? "Clone Default Volume Profile" : "Create Default Volume Profile",
                clone ? current.name + " Copy" : "InfinityDefaultVolumeProfile", "asset", "Choose a new asset path.");
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path)) throw new InvalidOperationException("Choose a new path; existing profiles are never overwritten by creation.");
            VolumeProfile created;
            if (clone)
            {
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(current), path))
                    throw new IOException("Could not clone the selected Volume profile.");
                created = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            }
            else
            {
                created = DefaultVolumeProfileFactory.CreateInMemory();
                AssetDatabase.CreateAsset(created, path);
                foreach (VolumeComponent component in created.components)
                    AssetDatabase.AddObjectToAsset(component, created);
                AssetDatabase.SaveAssetIfDirty(created);
            }
            Undo.RegisterCreatedObjectUndo(created, "Create default Volume profile");
            slot.serializedObject.Update();
            slot.objectReferenceValue = created;
            slot.serializedObject.ApplyModifiedProperties();
            NotifySelectionChanged();
        }
    }

    [InitializeOnLoad]
    internal static class InfinityVolumeProfileAuthoring
    {
        static InfinityVolumeProfileAuthoring()
        {
            Undo.undoRedoPerformed += RefreshEffectiveDefaults;
        }

        static void RefreshEffectiveDefaults()
        {
            if (GraphicsSettings.currentRenderPipeline is not InfinityRenderPipelineAsset asset ||
                !VolumeManager.instance.isInitialized) return;
            GraphicsSettings.TryGetRenderPipelineSettings(out InfinityDefaultVolumeProfileSettings settings);
            if (settings == null || VolumeManager.instance.globalDefaultProfile != settings.volumeProfile ||
                VolumeManager.instance.qualityDefaultProfile != asset.qualityVolumeProfile)
            {
                asset.RequestEditorRecreation();
                return;
            }
            VolumeManager.instance.OnVolumeProfileChanged(settings.volumeProfile);
            if (asset.qualityVolumeProfile != null)
                VolumeManager.instance.OnVolumeProfileChanged(asset.qualityVolumeProfile);
        }

        internal static string Reset(VolumeProfile profile, string backupDirectory = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string source = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
                throw new InvalidOperationException("Reset requires a saved Volume profile so current asset bytes can be backed up.");
            string intermediate = Path.Combine(Path.GetDirectoryName(Application.dataPath), "intermediate");
            string root = Path.GetFullPath(backupDirectory ?? Path.Combine(intermediate, "volume-reset", Guid.NewGuid().ToString("N")));
            if (!root.StartsWith(Path.GetFullPath(intermediate) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Directory.Exists(root))
                throw new InvalidOperationException("Reset requires a new task-owned backup directory under the project intermediate root.");
            Directory.CreateDirectory(root);
            File.Copy(source, Path.Combine(root, "profile.asset"));
            File.Copy(source + ".meta", Path.Combine(root, "profile.asset.meta"));
            File.WriteAllText(Path.Combine(root, "intent.json"), JsonUtility.ToJson(new ResetIntent { source = source, consumer = "Review reset values and Undo before deleting this backup", time = DateTime.UtcNow.ToString("O") }, true));
            var snapshots = new System.Collections.Generic.List<ComponentSnapshot>();
            foreach (VolumeComponent component in profile.components)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string guid, out long localId);
                snapshots.Add(new ComponentSnapshot { guid = guid, localId = localId, type = component.GetType().FullName, json = EditorJsonUtility.ToJson(component) });
            }
            File.WriteAllText(Path.Combine(root, "current-values.json"), JsonUtility.ToJson(new ProfileSnapshot { components = snapshots.ToArray() }, true));
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Reset default Volume profile");
            var defaults = DefaultVolumeProfileFactory.CreateInMemory();
            try
            {
                DefaultVolumeProfileFactory.ValidateAndComplete(profile);
                foreach (VolumeComponent template in defaults.components)
                {
                    profile.TryGet(template.GetType(), out VolumeComponent component);
                    Undo.RegisterCompleteObjectUndo(component, "Reset default Volume profile");
                    EditorUtility.CopySerialized(template, component);
                    component.name = template.GetType().Name;
                    EditorUtility.SetDirty(component);
                }
                EditorUtility.SetDirty(profile);
                Undo.CollapseUndoOperations(undoGroup);
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.OnVolumeProfileChanged(profile);
                Debug.Log($"[InfinityRP] Default Volume reset (unsaved, Undo available). Current-byte backup: {root}");
                return root;
            }
            finally
            {
                foreach (VolumeComponent component in defaults.components) UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(defaults);
            }
        }

        [Serializable] sealed class ComponentSnapshot { public string guid; public long localId; public string type; public string json; }
        [Serializable] sealed class ProfileSnapshot { public ComponentSnapshot[] components; }

        [MenuItem("Assets/Create/Rendering/Infinity/Global Settings")]
        static void CreateGlobalSettings()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Infinity GlobalSettings", "InfinityRenderPipelineGlobalSettings", "asset", "Choose a new settings asset path.");
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path)) throw new InvalidOperationException("GlobalSettings creation cannot overwrite an existing asset.");
            var created = RenderPipelineGlobalSettingsUtils.Create<InfinityRenderPipelineGlobalSettings>(path);
            Selection.activeObject = created;
        }

        [Serializable] sealed class ResetIntent { public string source; public string consumer; public string time; }
    }
}
