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
    public static class CreateOutputValidationScene
    {
        const string SceneDirectory = "Assets/Scene/Validation";
        const string ScenePath = SceneDirectory + "/Validation_Output.unity";
        const string MaterialDirectory = SceneDirectory + "/OutputMaterials";
        const string VolumeProfilePath = SceneDirectory + "/Validation_Output_Volume.asset";

        [MenuItem("Infinity/Validation/Create Output Fixture", false, 59)]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Create;
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scene/Validation/OutputMaterials"));

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

            Material cardMaterial = CreateEmissiveCard(MaterialDirectory + "/GrayCard18.mat", litShader, new Color(0.18f, 0.18f, 0.18f, 1.0f));

            GameObject card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "GrayCard18";
            card.transform.position = Vector3.zero;
            card.transform.rotation = Quaternion.identity;
            card.transform.localScale = new Vector3(2.0f, 2.0f, 1.0f);
            card.GetComponent<MeshRenderer>().sharedMaterial = cardMaterial;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Exposure exposure = profile.Add<Exposure>(true);
            OverrideAll(exposure);
            exposure.mode.value = EExposureMode.Manual;
            exposure.evCompensation.value = 0.0f;
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            GameObject volumeGo = new GameObject("Global Volume");
            Volume volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100.0f;
            volume.sharedProfile = profile;

            GameObject cameraGo = new GameObject("Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0.0f, 0.0f, -1.6f);
            camera.transform.LookAt(Vector3.zero);
            camera.cullingMask = ~0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            camera.fieldOfView = 40.0f;
            cameraGo.AddComponent<CameraComponent>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] Output fixture written: {ScenePath}");
        }

        static Material CreateEmissiveCard(string assetPath, Shader litShader, Color emission)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            Material material = new Material(litShader);
            material.SetColor("_BaseColor", Color.black);
            material.SetFloat("_Roughness", 1.0f);
            material.SetFloat("_SpecularLevel", 0.0f);
            material.SetColor("_EmissionColor", emission);
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
