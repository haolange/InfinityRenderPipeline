using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Editor
{
    /// <summary>
    /// Window/Analysis/Infinity Render Graph Viewer: the last compiled graph of each camera. Columns are passes in record
    /// order (orange header = async compute, gray = culled), rows are resources; a cell shows the pass reading (green),
    /// writing (red) or both (yellow), and the band behind the cells is the pool lifetime from the allocating pass to the
    /// releasing one. Clicking a header or a resource name shows its details. Capturing runs only while the window is
    /// open (RGDebugSession).
    /// </summary>
    public sealed class InfinityRenderGraphViewer : EditorWindow
    {
        const float kNameWidth = 260f;
        const float kCellSize = 16f;
        const float kHeaderHeight = 20f;

        static readonly Color kRead = new Color(0.30f, 0.80f, 0.35f);
        static readonly Color kWrite = new Color(0.90f, 0.30f, 0.25f);
        static readonly Color kReadWrite = new Color(0.95f, 0.80f, 0.20f);
        static readonly Color kLifetime = new Color(0.35f, 0.55f, 0.90f, 0.35f);
        static readonly Color kGraphics = new Color(0.30f, 0.45f, 0.70f);
        static readonly Color kAsync = new Color(0.90f, 0.55f, 0.15f);
        static readonly Color kCulled = new Color(0.35f, 0.35f, 0.35f);

        [SerializeField] string m_SelectedGraph;
        [SerializeField] bool m_ShowCulled = true;
        [SerializeField] bool m_ShowImported = true;
        [SerializeField] bool m_ShowUnused;
        [SerializeField] string m_Filter = string.Empty;
        Vector2 m_GridScroll;
        Vector2 m_DetailScroll;
        int m_SelectedPass = -1;
        int m_SelectedResource = -1;
        bool m_Dirty;

        [MenuItem("Window/Analysis/Infinity Render Graph Viewer")]
        static void Open()
        {
            var window = GetWindow<InfinityRenderGraphViewer>();
            window.titleContent = new GUIContent("Render Graph Viewer");
            window.Show();
        }

        void OnEnable()
        {
            RGDebugSession.captureEnabled = true;
            RGDebugSession.changed += OnGraphChanged;
            EditorApplication.update += OnUpdate;
        }

        void OnDisable()
        {
            RGDebugSession.changed -= OnGraphChanged;
            EditorApplication.update -= OnUpdate;
            RGDebugSession.captureEnabled = false;
            RGDebugSession.paused = false;
        }

        void OnGraphChanged() => m_Dirty = true;

        // Graphs compile every frame; repainting at most a few times per second keeps the Editor responsive.
        double m_LastRepaint;
        void OnUpdate()
        {
            if (!m_Dirty || EditorApplication.timeSinceStartup - m_LastRepaint < 0.25) return;
            m_Dirty = false;
            m_LastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }

        void OnGUI()
        {
            IReadOnlyDictionary<string, RGGraphSnapshot> graphs = RGDebugSession.latest;
            DrawToolbar(graphs);
            if (graphs.Count == 0)
            {
                EditorGUILayout.HelpBox("No graph captured yet. Render a Game or Scene view with the Infinity Render Pipeline.", MessageType.Info);
                return;
            }

            if (string.IsNullOrEmpty(m_SelectedGraph) || !graphs.ContainsKey(m_SelectedGraph))
            {
                m_SelectedGraph = graphs.Keys.First();
            }
            RGGraphSnapshot graph = graphs[m_SelectedGraph];

            List<int> passes = VisiblePasses(graph);
            List<int> resources = VisibleResources(graph);
            EditorGUILayout.LabelField($"Frame {graph.frame}: {graph.passes.Count} passes ({graph.CountCulled()} culled, {graph.CountAsync()} async, {graph.segmentCount} segments), {graph.resources.Count} resources, peak {graph.peakAliasBytes / 1024} KiB", EditorStyles.miniLabel);

            float detailHeight = Mathf.Min(180f, position.height * 0.35f);
            Rect gridArea = GUILayoutUtility.GetRect(position.width, position.height - detailHeight - 60f);
            DrawGrid(gridArea, graph, passes, resources);
            DrawDetails(graph, detailHeight);
        }

        void DrawToolbar(IReadOnlyDictionary<string, RGGraphSnapshot> graphs)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string[] labels = graphs.Keys.ToArray();
                int index = Mathf.Max(0, Array.IndexOf(labels, m_SelectedGraph));
                if (labels.Length > 0)
                {
                    int newIndex = EditorGUILayout.Popup(index, labels, EditorStyles.toolbarPopup, GUILayout.Width(180));
                    m_SelectedGraph = labels[Mathf.Clamp(newIndex, 0, labels.Length - 1)];
                }
                RGDebugSession.paused = GUILayout.Toggle(RGDebugSession.paused, "Pause", EditorStyles.toolbarButton, GUILayout.Width(50));
                m_ShowCulled = GUILayout.Toggle(m_ShowCulled, "Culled", EditorStyles.toolbarButton, GUILayout.Width(55));
                m_ShowImported = GUILayout.Toggle(m_ShowImported, "Imported", EditorStyles.toolbarButton, GUILayout.Width(65));
                m_ShowUnused = GUILayout.Toggle(m_ShowUnused, "Unused", EditorStyles.toolbarButton, GUILayout.Width(55));
                GUILayout.Space(6);
                m_Filter = EditorGUILayout.TextField(m_Filter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(45)) && m_SelectedGraph != null && graphs.TryGetValue(m_SelectedGraph, out RGGraphSnapshot graph))
                {
                    EditorGUIUtility.systemCopyBuffer = graph.ToText();
                }
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    RGDebugSession.Clear();
                    m_SelectedPass = m_SelectedResource = -1;
                }
            }
        }

        List<int> VisiblePasses(RGGraphSnapshot graph)
        {
            var list = new List<int>(graph.passes.Count);
            for (int i = 0; i < graph.passes.Count; i++)
            {
                if (!m_ShowCulled && graph.passes[i].culled) continue;
                list.Add(i);
            }
            return list;
        }

        List<int> VisibleResources(RGGraphSnapshot graph)
        {
            var list = new List<int>(graph.resources.Count);
            for (int i = 0; i < graph.resources.Count; i++)
            {
                RGResourceSnapshot resource = graph.resources[i];
                if (!m_ShowImported && resource.imported) continue;
                if (!m_ShowUnused && resource.firstUse < 0) continue;
                if (!string.IsNullOrEmpty(m_Filter) && resource.name.IndexOf(m_Filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(i);
            }
            return list;
        }

        void DrawGrid(Rect area, RGGraphSnapshot graph, List<int> passes, List<int> resources)
        {
            var content = new Rect(0, 0, kNameWidth + passes.Count * kCellSize, kHeaderHeight + resources.Count * kCellSize);
            m_GridScroll = GUI.BeginScrollView(area, m_GridScroll, content);

            var column = new Dictionary<int, int>(passes.Count);
            for (int c = 0; c < passes.Count; c++)
            {
                int passIndex = passes[c];
                column[passIndex] = c;
                RGPassSnapshot pass = graph.passes[passIndex];
                var header = new Rect(kNameWidth + c * kCellSize, 0, kCellSize - 1, kHeaderHeight - 2);
                EditorGUI.DrawRect(header, pass.culled ? kCulled : pass.asyncCompute ? kAsync : kGraphics);
                if (passIndex == m_SelectedPass) EditorGUI.DrawRect(new Rect(header.x, header.yMax - 3, header.width, 3), Color.white);
                string tip = $"{passIndex}: {pass.name} ({pass.type}{(pass.asyncCompute ? ", async" : "")}{(pass.culled ? ", culled" : "")}, segment {pass.segment}{(pass.mergeGroup >= 0 ? ", merge " + pass.mergeGroup + "." + pass.subpass : "")})";
                if (GUI.Button(header, new GUIContent(string.Empty, tip), GUIStyle.none))
                {
                    m_SelectedPass = passIndex;
                    m_SelectedResource = -1;
                }
            }

            for (int r = 0; r < resources.Count; r++)
            {
                int resourceIndex = resources[r];
                RGResourceSnapshot resource = graph.resources[resourceIndex];
                float y = kHeaderHeight + r * kCellSize;
                if ((r & 1) == 0) EditorGUI.DrawRect(new Rect(0, y, content.width, kCellSize), new Color(0, 0, 0, 0.08f));
                string label = (resource.imported ? "• " : "") + resource.name;
                var nameRect = new Rect(2, y, kNameWidth - 4, kCellSize);
                if (GUI.Button(nameRect, new GUIContent(label, resource.description), resourceIndex == m_SelectedResource ? EditorStyles.boldLabel : EditorStyles.label))
                {
                    m_SelectedResource = resourceIndex;
                    m_SelectedPass = -1;
                }

                int start = resource.createPass >= 0 ? resource.createPass : resource.firstUse;
                int end = resource.releasePass >= 0 ? resource.releasePass : resource.lastUse;
                if (!resource.imported && start >= 0 && end >= start)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue;
                    for (int p = start; p <= end; p++)
                    {
                        if (!column.TryGetValue(p, out int c)) continue;
                        x0 = Mathf.Min(x0, kNameWidth + c * kCellSize);
                        x1 = Mathf.Max(x1, kNameWidth + (c + 1) * kCellSize);
                    }
                    if (x1 > x0) EditorGUI.DrawRect(new Rect(x0, y + 2, x1 - x0, kCellSize - 4), kLifetime);
                }

                foreach (KeyValuePair<int, int> entry in column)
                {
                    RGPassSnapshot pass = graph.passes[entry.Key];
                    bool reads = Array.IndexOf(pass.reads, resourceIndex) >= 0;
                    bool writes = Array.IndexOf(pass.writes, resourceIndex) >= 0;
                    if (!reads && !writes) continue;
                    Color color = reads && writes ? kReadWrite : writes ? kWrite : kRead;
                    if (pass.culled) color *= 0.5f;
                    EditorGUI.DrawRect(new Rect(kNameWidth + entry.Value * kCellSize + 3, y + 3, kCellSize - 7, kCellSize - 6), color);
                }
            }
            GUI.EndScrollView();
        }

        void DrawDetails(RGGraphSnapshot graph, float height)
        {
            m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll, GUILayout.Height(height));
            if (m_SelectedPass >= 0 && m_SelectedPass < graph.passes.Count)
            {
                RGPassSnapshot pass = graph.passes[m_SelectedPass];
                EditorGUILayout.LabelField($"{m_SelectedPass}: {pass.name}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Type", pass.type.ToString());
                EditorGUILayout.LabelField("Queue", pass.asyncCompute ? "Async compute" : "Graphics");
                EditorGUILayout.LabelField("Segment", pass.segment.ToString());
                if (pass.mergeGroup >= 0) EditorGUILayout.LabelField("Native pass", pass.mergeGroup + " subpass " + pass.subpass);
                EditorGUILayout.LabelField("Culled", pass.culled ? "Yes (" + (string.IsNullOrEmpty(pass.cullReason) ? "unused output" : pass.cullReason) + ")" : pass.allowCulling ? "No (cullable)" : "No (side effect)");
                if (pass.waitsForPass >= 0) EditorGUILayout.LabelField("Waits for", PassName(graph, pass.waitsForPass));
                if (pass.signalsToPass >= 0) EditorGUILayout.LabelField("Signals", PassName(graph, pass.signalsToPass));
                DrawResourceList(graph, "Reads", pass.reads);
                DrawResourceList(graph, "Writes", pass.writes);
                DrawResourceList(graph, "Allocates", pass.creates);
                DrawResourceList(graph, "Releases after", pass.releases);
            }
            else if (m_SelectedResource >= 0 && m_SelectedResource < graph.resources.Count)
            {
                RGResourceSnapshot resource = graph.resources[m_SelectedResource];
                EditorGUILayout.LabelField(resource.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Kind", resource.type + (resource.imported ? " (imported)" : " (pooled)"));
                EditorGUILayout.LabelField("Size", resource.description + (resource.estimatedBytes > 0 ? " (~" + resource.estimatedBytes / 1024 + " KiB)" : ""));
                if (resource.aliasSlot >= 0) EditorGUILayout.LabelField("Physical slot", "group " + resource.aliasGroup + " slot " + resource.aliasSlot + (resource.memoryless ? " memoryless" : ""));
                EditorGUILayout.LabelField("Used", resource.firstUse < 0 ? "Never (all users culled)" : PassName(graph, resource.firstUse) + " → " + PassName(graph, resource.lastUse));
                if (resource.createPass >= 0) EditorGUILayout.LabelField("Allocated by", PassName(graph, resource.createPass));
                if (resource.releasePass >= 0) EditorGUILayout.LabelField("Released after", PassName(graph, resource.releasePass));
                var producers = new List<string>();
                var consumers = new List<string>();
                for (int i = 0; i < graph.passes.Count; i++)
                {
                    if (Array.IndexOf(graph.passes[i].writes, m_SelectedResource) >= 0) producers.Add(PassName(graph, i));
                    if (Array.IndexOf(graph.passes[i].reads, m_SelectedResource) >= 0) consumers.Add(PassName(graph, i));
                }
                EditorGUILayout.LabelField("Writers", producers.Count == 0 ? "-" : string.Join(", ", producers), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Readers", consumers.Count == 0 ? "-" : string.Join(", ", consumers), EditorStyles.wordWrappedLabel);
            }
            else
            {
                EditorGUILayout.LabelField("Click a pass header or a resource name.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        static string PassName(RGGraphSnapshot graph, int index)
        {
            return index >= 0 && index < graph.passes.Count ? index + " " + graph.passes[index].name : "-";
        }

        static void DrawResourceList(RGGraphSnapshot graph, string label, int[] resources)
        {
            if (resources == null || resources.Length == 0) return;
            EditorGUILayout.LabelField(label, string.Join(", ", resources.Select(r => graph.resources[r].name)), EditorStyles.wordWrappedLabel);
        }
    }
}
