using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class CreateUIValidationScene
    {
        const string ScenePath = "Assets/Scene/Validation/Validation_UI.unity";

        [MenuItem("Window/Infinity/Create UI Fixture", false, 60)]
        public static void Create()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("UICamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.gray;

            CreateOverlay("OverlayCanvas");
            CreateCameraCanvas("CameraCanvas", camera);
            CreateWorldCanvas("WorldCanvas");

            DirectoryEnsure();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[InfinityRP] Validation_UI written to " + ScenePath);
        }

        static void DirectoryEnsure()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scene"))
                AssetDatabase.CreateFolder("Assets", "Scene");
            if (!AssetDatabase.IsValidFolder("Assets/Scene/Validation"))
                AssetDatabase.CreateFolder("Assets/Scene", "Validation");
        }

        static void CreateOverlay(string name)
        {
            var root = new GameObject(name);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
            AddLabel(root.transform, "Overlay TMP", new Vector2(0, 80));
            AddImage(root.transform, "Overlay Image", new Vector2(0, 0));
        }

        static void CreateCameraCanvas(string name, Camera camera)
        {
            var root = new GameObject(name);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
            AddLabel(root.transform, "Camera TMP", new Vector2(0, -80));
        }

        static void CreateWorldCanvas(string name)
        {
            var root = new GameObject(name);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.transform.position = new Vector3(0, 1, 2);
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
            AddLabel(root.transform, "World TMP", Vector2.zero);
        }

        static void AddLabel(Transform parent, string text, Vector2 anchored)
        {
            var go = new GameObject(text);
            go.transform.SetParent(parent, false);
            Text label = go.AddComponent<Text>();
            label.text = text;
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            if (Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") != null)
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(320, 48);
            rect.anchoredPosition = anchored;
        }

        static void AddImage(Transform parent, string name, Vector2 anchored)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(128, 128);
            rect.anchoredPosition = anchored;
            go.AddComponent<Mask>();
        }
    }
}
