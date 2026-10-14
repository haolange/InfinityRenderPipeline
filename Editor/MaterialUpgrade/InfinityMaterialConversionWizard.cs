using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace InfinityTech.Rendering.Editor
{
    /// <summary>
    /// Window/Infinity/Material Conversion Wizard: lists the project's Built-in, URP and HDRP materials that an
    /// InfinityMaterialUpgraders upgrader converts, with the inputs InfinityLit cannot keep, and converts the checked
    /// ones in place (Undo-able; the asset keeps its GUID, so scenes and prefabs keep their references).
    /// </summary>
    public sealed class InfinityMaterialConversionWizard : EditorWindow
    {
        sealed class Entry
        {
            public Material material;
            public string path;
            public string sourceShader;
            public string dropped;
            public bool selected = true;
        }

        readonly List<Entry> m_Entries = new List<Entry>();
        Vector2 m_Scroll;
        bool m_Scanned;
        string m_Status;

        [MenuItem("Window/Infinity/Material Conversion Wizard", false, 31)]
        public static void Open()
        {
            var window = GetWindow<InfinityMaterialConversionWizard>();
            window.titleContent = new GUIContent("Material Conversion");
            window.minSize = new Vector2(560, 300);
            window.Show();
        }

        // Opened by the Render Pipeline Wizard with the materials of the loaded scenes.
        internal static void Open(IEnumerable<Material> materials)
        {
            Open();
            var window = GetWindow<InfinityMaterialConversionWizard>();
            window.SetEntries(materials);
        }

        void SetEntries(IEnumerable<Material> materials)
        {
            m_Entries.Clear();
            foreach (Material material in materials)
            {
                MaterialUpgrader upgrader = InfinityMaterialUpgraders.Find(material);
                if (upgrader == null) continue;
                List<string> dropped = InfinityMaterialUpgraders.DroppedInputs(material);
                m_Entries.Add(new Entry
                {
                    material = material,
                    path = AssetDatabase.GetAssetPath(material),
                    sourceShader = material.shader.name,
                    dropped = dropped.Count == 0 ? string.Empty : string.Join(", ", dropped.Distinct()),
                });
            }
            m_Entries.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            m_Scanned = true;
            m_Status = $"{m_Entries.Count} convertible material(s).";
        }

        void ScanProject()
        {
            var materials = new List<Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) materials.Add(material);
            }
            SetEntries(materials);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Converts Built-in Standard / Legacy / Unlit, URP Lit / Simple Lit / Unlit and HDRP Lit / Unlit materials to " +
                "InfinityLit / InfinityUnlit. Smoothness becomes 1 - Roughness, Metallic becomes Reflectance, transparent surfaces become " +
                "translucent stage T0. Mask, occlusion, detail, height and emission maps have no InfinityLit input and are listed as dropped.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan Project", GUILayout.Width(110))) ScanProject();
                if (GUILayout.Button("Scan Selection", GUILayout.Width(110))) SetEntries(Selection.GetFiltered<Material>(SelectionMode.DeepAssets));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(m_Entries.Count == 0))
                {
                    if (GUILayout.Button("All", GUILayout.Width(40))) m_Entries.ForEach(e => e.selected = true);
                    if (GUILayout.Button("None", GUILayout.Width(45))) m_Entries.ForEach(e => e.selected = false);
                }
            }

            if (!m_Scanned)
            {
                EditorGUILayout.LabelField("Scan the project or the selection to list convertible materials.", EditorStyles.miniLabel);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (Entry entry in m_Entries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(18));
                    EditorGUILayout.ObjectField(entry.material, typeof(Material), false, GUILayout.Width(200));
                    EditorGUILayout.LabelField(entry.sourceShader, GUILayout.Width(200));
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(entry.dropped) ? "-" : "drops " + entry.dropped, EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(m_Status ?? string.Empty, EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(!m_Entries.Any(e => e.selected)))
                {
                    if (GUILayout.Button("Convert Selected", GUILayout.Width(130))) ConvertSelected();
                }
            }
        }

        void ConvertSelected()
        {
            Entry[] selected = m_Entries.Where(e => e.selected && e.material != null).ToArray();
            int converted = 0;
            try
            {
                for (int i = 0; i < selected.Length; i++)
                {
                    Entry entry = selected[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Material Conversion", entry.path, (float)i / selected.Length)) break;
                    MaterialUpgrader upgrader = InfinityMaterialUpgraders.Find(entry.material);
                    if (upgrader == null) continue;
                    Undo.RecordObject(entry.material, "Convert Material to Infinity");
                    upgrader.Upgrade(entry.material, MaterialUpgrader.UpgradeFlags.None);
                    EditorUtility.SetDirty(entry.material);
                    converted++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            m_Entries.RemoveAll(e => e.material == null || InfinityMaterialUpgraders.Find(e.material) == null);
            m_Status = $"Converted {converted} material(s); {m_Entries.Count} left.";
        }
    }
}
