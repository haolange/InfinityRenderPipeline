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
    public static class CreateTranslucentValidationScene
    {
        const string SceneDirectory = "Assets/Scene/Validation";
        const string ScenePath = SceneDirectory + "/Validation_Translucent.unity";
        const string MaterialDirectory = SceneDirectory + "/TranslucentMaterials";
        const string VolumeProfilePath = SceneDirectory + "/Validation_Translucent_Volume.asset";

        [MenuItem("Infinity/Validation/Create Translucent Fixture", false, 58)]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Create;
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scene/Validation/TranslucentMaterials"));

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

            Material groundMaterial = CreateLitMaterial(MaterialDirectory + "/Ground.mat", litShader, new Color(0.52f, 0.52f, 0.50f, 1.0f), 0.6f, 0.3f, 0);
            Material glassMaterial = CreateLitMaterial(MaterialDirectory + "/T0_Glass.mat", litShader, new Color(0.55f, 0.75f, 0.85f, 0.28f), 0.08f, 0.85f, 1);
            Material refractiveMaterial = CreateLitMaterial(MaterialDirectory + "/T1_Refractive.mat", litShader, new Color(0.85f, 0.90f, 0.95f, 0.42f), 0.12f, 0.9f, 2);
            Material particleMaterial = CreateLitMaterial(MaterialDirectory + "/T2_Particle.mat", litShader, new Color(1.0f, 0.45f, 0.20f, 0.55f), 0.7f, 0.15f, 3);
            if (refractiveMaterial.HasProperty("_RefractionStrength"))
            {
                refractiveMaterial.SetFloat("_RefractionStrength", 0.06f);
            }

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2.5f, 1.0f, 2.5f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "T0_Glass";
            glass.transform.position = new Vector3(-1.2f, 0.7f, 0.1f);
            glass.transform.localScale = new Vector3(1.2f, 1.4f, 0.12f);
            glass.GetComponent<MeshRenderer>().sharedMaterial = glassMaterial;

            GameObject refractive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            refractive.name = "T1_Refractive";
            refractive.transform.position = new Vector3(1.15f, 0.65f, -0.15f);
            refractive.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
            refractive.GetComponent<MeshRenderer>().sharedMaterial = refractiveMaterial;

            GameObject particle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            particle.name = "T2_Particle";
            particle.transform.position = new Vector3(0.0f, 1.15f, 0.8f);
            particle.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            particle.GetComponent<MeshRenderer>().sharedMaterial = particleMaterial;

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
            VolumetricFog fog = profile.Add<VolumetricFog>(true);
            OverrideAll(fog);
            fog.Density.value = 0.04f;
            fog.Height.value = 8.0f;
            fog.MaxDistance.value = 64.0f;
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

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] Translucent fixture written: {ScenePath}");
        }

        static Material CreateLitMaterial(string assetPath, Shader litShader, Color baseColor, float roughness, float specular, int translucentStage)
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
            if (material.HasProperty("_TranslucentStage"))
            {
                material.SetFloat("_TranslucentStage", translucentStage);
            }
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
