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
    public static class CreateTemporalValidationScene
    {
        const string SceneDirectory = "Assets/Scene/Validation";
        const string ScenePath = SceneDirectory + "/Validation_Temporal.unity";
        const string MaterialDirectory = SceneDirectory + "/TemporalMaterials";
        const string VolumeProfilePath = SceneDirectory + "/Validation_Temporal_Volume.asset";

        [MenuItem("Infinity/Validation/Create Temporal Fixture", false, 56)]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Create;
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scene/Validation/TemporalMaterials"));

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

            Material groundMaterial = CreateLitMaterial(MaterialDirectory + "/Ground.mat", litShader, new Color(0.55f, 0.55f, 0.52f, 1.0f), 0.55f, 0.35f);
            Material mirrorMaterial = CreateLitMaterial(MaterialDirectory + "/Mirror.mat", litShader, new Color(0.92f, 0.93f, 0.95f, 1.0f), 0.06f, 0.95f);
            Material diffuseMaterial = CreateLitMaterial(MaterialDirectory + "/Diffuse.mat", litShader, new Color(0.15f, 0.55f, 0.95f, 1.0f), 0.75f, 0.08f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2.5f, 1.0f, 2.5f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            GameObject mirror = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mirror.name = "MirrorCube";
            mirror.transform.position = new Vector3(-1.1f, 0.6f, 0.2f);
            mirror.transform.localScale = new Vector3(1.1f, 1.2f, 0.25f);
            mirror.GetComponent<MeshRenderer>().sharedMaterial = mirrorMaterial;

            GameObject diffuse = GameObject.CreatePrimitive(PrimitiveType.Cube);
            diffuse.name = "DiffuseCube";
            diffuse.transform.position = new Vector3(1.2f, 0.55f, -0.4f);
            diffuse.transform.localScale = new Vector3(1.0f, 1.1f, 1.0f);
            diffuse.GetComponent<MeshRenderer>().sharedMaterial = diffuseMaterial;

            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.96f, 0.88f, 1.0f);
            light.intensity = 2.0f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48.0f, -28.0f, 0.0f);
            LightComponent lightComponent = lightGo.AddComponent<LightComponent>();
            lightComponent.enableShadow = true;
            RenderSettings.sun = light;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ScreenSpaceReflection ssr = profile.Add<ScreenSpaceReflection>(true);
            OverrideAll(ssr);
            ScreenSpaceIndirectDiffuse ssgi = profile.Add<ScreenSpaceIndirectDiffuse>(true);
            OverrideAll(ssgi);
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            GameObject volumeGo = new GameObject("Global Volume");
            Volume volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100.0f;
            volume.sharedProfile = profile;

            GameObject cameraGo = new GameObject("Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0.0f, 1.6f, -5.2f);
            camera.transform.LookAt(new Vector3(0.0f, 0.5f, 0.0f));
            camera.cullingMask = ~0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1.0f);
            cameraGo.AddComponent<CameraComponent>();
            cameraGo.AddComponent<TemporalValidationCamera>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] Temporal fixture written: {ScenePath}");
        }

        static Material CreateLitMaterial(string assetPath, Shader litShader, Color baseColor, float roughness, float specular)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            Material material = new Material(litShader);
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Roughness", roughness);
            material.SetFloat("_SpecularLevel", specular);
            InfinityLitGUI.ApplyPassState(material);
            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        static void OverrideAll(VolumeComponent component)
        {
            component.active = true;
            var parameters = component.parameters;
            if (parameters == null)
            {
                return;
            }

            for (int i = 0; i < parameters.Count; ++i)
            {
                VolumeParameter parameter = parameters[i];
                if (parameter != null)
                {
                    parameter.overrideState = true;
                }
            }
        }
    }
}
