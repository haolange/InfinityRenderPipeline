using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class CreateDecalValidationScene
    {
        const string SceneDirectory = "Assets/Scene/Validation";
        const string ScenePath = SceneDirectory + "/Validation_Decal.unity";
        const string MaterialDirectory = SceneDirectory + "/DecalMaterials";

        [MenuItem("Infinity/Validation/Create Decal Fixture", false, 53)]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Create;
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scene/Validation/DecalMaterials"));

            if (File.Exists(ScenePath))
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 3.0f;
            light.color = Color.white;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);
            LightComponent lightComponent = lightGo.AddComponent<LightComponent>();
            lightComponent.enableShadow = false;
            lightComponent.intensity = 10.0f;
            lightComponent.color = Color.white;
            RenderSettings.sun = light;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2.0f, 1.0f, 2.0f);
            Shader litShader = Shader.Find("InfinityPipeline/InfinityLit");
            if (litShader == null)
            {
                throw new System.InvalidOperationException("InfinityRP Validation: InfinityPipeline/InfinityLit shader missing.");
            }

            string groundMatPath = MaterialDirectory + "/Ground.mat";
            Material groundExisting = AssetDatabase.LoadAssetAtPath<Material>(groundMatPath);
            if (groundExisting != null)
            {
                AssetDatabase.DeleteAsset(groundMatPath);
            }

            Material groundMaterial = new Material(litShader);
            groundMaterial.SetColor("_BaseColor", new Color(0.72f, 0.72f, 0.70f, 1.0f));
            groundMaterial.SetFloat("_Roughness", 0.55f);
            groundMaterial.SetFloat("_SpecularLevel", 0.35f);
            if (groundMaterial.HasProperty("_EmissionColor"))
            {
                groundMaterial.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.32f, 1.0f));
            }
            InfinityLitGUI.ApplyPassState(groundMaterial);
            AssetDatabase.CreateAsset(groundMaterial, groundMatPath);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            Shader decalShader = Shader.Find("InfinityPipeline/InfinityDecal");
            if (decalShader == null)
            {
                throw new System.InvalidOperationException("InfinityRP Validation: InfinityPipeline/InfinityDecal shader missing.");
            }

            CreateDecal("Decal_Red", new Vector3(-0.6f, 0.5f, 0.0f), new Vector3(3.0f, 1.5f, 3.0f), new Color(0.85f, 0.15f, 0.12f, 0.85f), 0, decalShader);
            CreateDecal("Decal_Green", new Vector3(0.2f, 0.5f, 0.2f), new Vector3(3.0f, 1.5f, 3.0f), new Color(0.12f, 0.75f, 0.22f, 0.75f), 1, decalShader);
            CreateDecal("Decal_Blue", new Vector3(0.4f, 0.5f, -0.3f), new Vector3(3.0f, 1.5f, 3.0f), new Color(0.15f, 0.28f, 0.85f, 0.65f), 2, decalShader);

            GameObject cameraGo = new GameObject("Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0.0f, 3.2f, -6.0f);
            camera.transform.LookAt(Vector3.zero);
            camera.cullingMask = ~0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.18f, 0.20f, 1.0f);
            cameraGo.AddComponent<CameraComponent>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] Decal fixture written: {ScenePath}");
        }

        static void CreateDecal(string name, Vector3 position, Vector3 scale, Color color, int drawOrder, Shader decalShader)
        {
            string materialPath = MaterialDirectory + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(materialPath);
            }

            Material material = new Material(decalShader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Roughness", 0.35f);
            AssetDatabase.CreateAsset(material, materialPath);

            GameObject decalGo = new GameObject(name);
            decalGo.transform.position = position;
            decalGo.transform.localScale = scale;
            MeshFilter filter = decalGo.AddComponent<MeshFilter>();
            MeshRenderer renderer = decalGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            DecalComponent decal = decalGo.AddComponent<DecalComponent>();
            decal.drawOrder = drawOrder;
            renderer.rendererPriority = drawOrder;
            if (filter.sharedMesh == null)
            {
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }
        }
    }
}
