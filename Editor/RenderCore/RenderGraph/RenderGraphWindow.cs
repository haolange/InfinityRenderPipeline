using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace InfinityTech.Rendering.RDG.Editor
{
    public class RenderGraphWindow : EditorWindow
    {
        private RenderGraphView graphView;

        [MenuItem("Tool/GraphBuilderView")]
        public static void ShowWindow()
        {
            RenderGraphWindow graphWindow = GetWindow<RenderGraphWindow>();
            graphWindow.minSize = new Vector2(500, 400);
            graphWindow.titleContent = new GUIContent("GraphBuilderView");
        }

        public void OnEnable()
        {
            graphView = new RenderGraphView();
            graphView.StretchToParentSize();
            rootVisualElement.Add(graphView);
        }

        public void OnDisable()
        {
            rootVisualElement.Remove(graphView);
        }
    }
}
