using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.Editor;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class CreateLocalLightsValidationScene
    {
        const string SceneDirectory = "Assets/Scene/Validation";
        const string ScenePath = SceneDirectory + "/Validation_LocalLights.unity";
        const string MaterialDirectory = SceneDirectory + "/LocalLightsMaterials";
        const string VolumeProfilePath = SceneDirectory + "/Validation_LocalLights_Volume.asset";

        [MenuItem("Infinity/Validation/Create Local Lights Fixture", false, 55)]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Create;
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scene/Validation/LocalLightsMaterials"));

            if (File.Exists(ScenePath))
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath) != null)
            {
                AssetDatabase.DeleteAsset(VolumeProfilePath);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

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
            groundMaterial.SetColor("_BaseColor", new Color(0.65f, 0.65f, 0.62f, 1.0f));
            groundMaterial.SetFloat("_Roughness", 0.45f);
            groundMaterial.SetFloat("_SpecularLevel", 0.4f);
            InfinityLitGUI.ApplyPassState(groundMaterial);
            AssetDatabase.CreateAsset(groundMaterial, groundMatPath);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(3.0f, 1.0f, 3.0f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            CreateCaster("Caster_Point", new Vector3(-2.2f, 0.45f, 0.4f), new Vector3(0.7f, 0.9f, 0.7f), groundMaterial);
            CreateCaster("Caster_Spot", new Vector3(2.0f, 0.4f, -0.2f), new Vector3(0.6f, 0.8f, 0.6f), groundMaterial);
            CreateCaster("Caster_Sun", new Vector3(0.0f, 0.35f, 1.6f), new Vector3(0.8f, 0.7f, 0.5f), groundMaterial);

            GameObject directionalGo = new GameObject("Directional Light");
            Light directional = directionalGo.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.color = new Color(1.0f, 0.96f, 0.88f, 1.0f);
            directional.intensity = 1.2f;
            directional.shadows = LightShadows.Soft;
            directionalGo.transform.rotation = Quaternion.Euler(50.0f, -35.0f, 0.0f);
            LightComponent directionalExt = directionalGo.AddComponent<LightComponent>();
            directionalExt.enableShadow = true;
            RenderSettings.sun = directional;

            GameObject pointGo = new GameObject("Point Light");
            Light point = pointGo.AddComponent<Light>();
            point.type = LightType.Point;
            point.color = new Color(1.0f, 0.25f, 0.18f, 1.0f);
            point.intensity = 12.0f;
            point.range = 8.0f;
            point.shadows = LightShadows.Soft;
            pointGo.transform.position = new Vector3(-2.4f, 2.2f, 0.6f);
            LightComponent pointExt = pointGo.AddComponent<LightComponent>();
            pointExt.enableShadow = true;

            GameObject spotGo = new GameObject("Spot Light");
            Light spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(0.2f, 0.85f, 0.35f, 1.0f);
            spot.intensity = 22.0f;
            spot.range = 10.0f;
            spot.spotAngle = 50.0f;
            spot.innerSpotAngle = 25.0f;
            spot.shadows = LightShadows.Soft;
            spotGo.transform.position = new Vector3(2.6f, 3.0f, -1.4f);
            spotGo.transform.LookAt(new Vector3(2.0f, 0.0f, 0.0f));
            LightComponent spotExt = spotGo.AddComponent<LightComponent>();
            spotExt.enableShadow = true;

            GameObject rectGo = new GameObject("Rect Light");
            Light rect = rectGo.AddComponent<Light>();
            rect.type = LightType.Rectangle;
            rect.color = new Color(0.25f, 0.45f, 1.0f, 1.0f);
            rect.intensity = 20.0f;
            rect.range = 12.0f;
            rect.areaSize = new Vector2(2.2f, 1.1f);
            rect.shadows = LightShadows.None;
            rectGo.transform.position = new Vector3(0.0f, 2.6f, 0.2f);
            rectGo.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            LightComponent rectExt = rectGo.AddComponent<LightComponent>();
            rectExt.enableShadow = false;
            rectExt.width = 2.2f;
            rectExt.height = 1.1f;

            VolumeProfile volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            VolumetricFog fog = volumeProfile.Add<VolumetricFog>(true);
            fog.active = false;
            VolumetricCloud cloud = volumeProfile.Add<VolumetricCloud>(true);
            cloud.active = false;
            AssetDatabase.CreateAsset(volumeProfile, VolumeProfilePath);

            GameObject volumeGo = new GameObject("Global Volume");
            Volume volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100.0f;
            volume.sharedProfile = volumeProfile;

            GameObject cameraGo = new GameObject("Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0.0f, 4.2f, -8.0f);
            camera.transform.LookAt(new Vector3(0.0f, 0.2f, 0.0f));
            camera.cullingMask = ~0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 1.0f);
            cameraGo.AddComponent<CameraComponent>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] Local lights fixture written: {ScenePath}");
        }

        static void CreateCaster(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
