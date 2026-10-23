using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Every light shape on a glossy and a rough floor: sphere, line, tube, rect with barn doors, disk, pyramid and box
    // spots, and a sun with a visible disk. Used to check the Karis representative-point highlights (0.5.0 S8).
    public static class CreateAreaLightsValidationScene
    {

        [MenuItem("Window/Infinity/Create Area Lights Fixture", false, 56)]
        public static void Create() => CreateInDirectory("Assets/Scene/Validation");

        internal static void CreateInDirectory(string sceneDirectory)
        {
            if (string.IsNullOrWhiteSpace(sceneDirectory) || !sceneDirectory.StartsWith("Assets/", System.StringComparison.Ordinal) ||
                sceneDirectory.Contains("..") || sceneDirectory.Contains('\\') || sceneDirectory.EndsWith("/", System.StringComparison.Ordinal))
                throw new System.ArgumentException("Fixture directory must be a project-relative Assets subdirectory.");
            if (!ValidationFixture.LeavePlayMode(() => CreateInDirectory(sceneDirectory))) return;
            string ScenePath = sceneDirectory + "/Validation_AreaLights.unity";
            string MaterialDirectory = sceneDirectory + "/AreaLightsMaterials";
            Directory.CreateDirectory(Path.GetFullPath(MaterialDirectory));
            AssetDatabase.Refresh();
            if (File.Exists(ScenePath))
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader litShader = Shader.Find("InfinityPipeline/InfinityLit");
            if (litShader == null)
            {
                throw new System.InvalidOperationException("InfinityRP Validation: InfinityPipeline/InfinityLit shader missing.");
            }

            Material glossy = CreateMaterial(MaterialDirectory, litShader, "Glossy", new Color(0.18f, 0.18f, 0.2f), 0.12f);
            Material rough = CreateMaterial(MaterialDirectory, litShader, "Rough", new Color(0.6f, 0.6f, 0.58f), 0.65f);

            CreateFloor("Floor_Glossy", new Vector3(-5.5f, 0, 0), glossy);
            CreateFloor("Floor_Rough", new Vector3(5.5f, 0, 0), rough);

            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 5.5f;
                CreateLight("Sphere", new Vector3(x - 3.0f, 1.0f, 2.5f), Quaternion.identity, LightType.Point, new Color(1, 0.4f, 0.2f), 6,
                    (ref FLightShape shape) => { shape.type = EInfinityLightType.Point; shape.sourceRadius = 0.3f; });
                CreateLight("Line", new Vector3(x, 1.0f, 2.5f), Quaternion.identity, LightType.Point, new Color(0.3f, 1, 0.4f), 6,
                    (ref FLightShape shape) => { shape.type = EInfinityLightType.Point; shape.sourceLength = 1.5f; });
                CreateLight("Tube", new Vector3(x + 3.0f, 1.0f, 2.5f), Quaternion.Euler(0, 30, 0), LightType.Point, new Color(0.3f, 0.5f, 1), 6,
                    (ref FLightShape shape) => { shape.type = EInfinityLightType.Point; shape.sourceLength = 2.0f; shape.sourceRadius = 0.08f; });
                CreateLight("Rect", new Vector3(x - 3.0f, 1.5f, -1.5f), Quaternion.Euler(70, 0, 0), LightType.Rectangle, Color.white, 10,
                    (ref FLightShape shape) => { shape.type = EInfinityLightType.Rect; shape.sourceWidth = 1.6f; shape.sourceHeight = 0.6f; shape.barnDoorAngle = 45; shape.barnDoorLength = 0.4f; });
                CreateLight("Disk", new Vector3(x, 1.5f, -1.5f), Quaternion.Euler(70, 0, 0), LightType.Disc, new Color(1, 0.9f, 0.6f), 10,
                    (ref FLightShape shape) => { shape.type = EInfinityLightType.Disk; shape.sourceRadius = 0.5f; });
                CreateLight("Pyramid", new Vector3(x + 3.0f, 3.0f, -1.5f), Quaternion.Euler(90, 0, 0), LightType.Pyramid, new Color(1, 0.3f, 0.8f), 20,
                    (ref FLightShape shape) => { shape.type = EInfinityLightType.Spot; shape.spotShape = ESpotShape.Pyramid; shape.pyramidAspect = 2; });
                CreateLight("Box", new Vector3(x, 3.0f, -4.0f), Quaternion.Euler(90, 0, 0), LightType.Box, new Color(0.3f, 1, 1), 2,
                    (ref FLightShape shape) => { shape.type = EInfinityLightType.Spot; shape.spotShape = ESpotShape.Box; shape.boxSize = new Vector2(1.5f, 0.75f); });
            }

            Light sun = CreateLight("Sun", Vector3.zero, Quaternion.Euler(25, 160, 0), LightType.Directional, new Color(1, 0.95f, 0.85f), 0.6f,
                (ref FLightShape shape) => { shape.type = EInfinityLightType.Directional; shape.sourceAngle = 2.0f; });
            RenderSettings.sun = sun;

            GameObject cameraGo = new GameObject("Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0.0f, 4.0f, -11.0f);
            camera.transform.LookAt(new Vector3(0.0f, 0.5f, 0.5f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1.0f);
            cameraGo.AddComponent<InfinityAdditionalCameraData>();
            ValidationSceneUtility.EnsureLitLivenessMarker(camera);

            ValidationFixture.SaveSceneAndAssets(scene, ScenePath, new UnityEngine.Object[] { glossy, rough });
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] Area lights fixture written: {ScenePath}");
        }

        static Material CreateMaterial(string MaterialDirectory, Shader shader, string name, Color color, float roughness)
        {
            string path = MaterialDirectory + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Roughness", roughness);
            material.SetFloat("_SpecularLevel", 0.5f);
            InfinityTech.Rendering.Pipeline.MaterialRouteUtility.ApplyPassState(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void CreateFloor(string name, Vector3 position, Material material)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = name;
            floor.transform.position = position;
            floor.transform.localScale = new Vector3(1.0f, 1.0f, 1.2f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        delegate void ShapeEdit(ref FLightShape shape);

        static Light CreateLight(string name, Vector3 position, Quaternion rotation, LightType type, Color color, float intensity, ShapeEdit edit)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            Light light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = 8;
            light.spotAngle = 60;
            light.shadows = LightShadows.None;
            InfinityAdditionalLightData data = go.AddComponent<InfinityAdditionalLightData>();
            FLightShape shape = data.shape;
            edit(ref shape);
            data.SetShape(shape);
            return light;
        }
    }
}
