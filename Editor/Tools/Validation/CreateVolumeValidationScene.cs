using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class CreateVolumeValidationScene
    {
        const string SceneDirectory = "Assets/Scene/Validation";
        const string ScenePath = SceneDirectory + "/Validation_Volume.unity";
        const string GlobalProfilePath = SceneDirectory + "/Validation_Volume_Global.asset";
        const string LocalProfilePath = SceneDirectory + "/Validation_Volume_Local.asset";

        [MenuItem("Infinity/Validation/Create Volume Fixture", false, 50)]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Create;
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scene/Validation"));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            int layerDefault = 0;
            int layerWater = LayerMask.NameToLayer("Water");
            if (layerWater < 0)
            {
                layerWater = 4;
            }

            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube";
            cube.transform.position = new Vector3(0.0f, 0.5f, 0.0f);

            VolumeProfile globalProfile = CreateOrReplaceProfile(GlobalProfilePath, new Vector4(1.6f, 0.35f, 0.35f, 1.0f), addVolumetricFog: false);
            VolumeProfile localProfile = CreateOrReplaceProfile(LocalProfilePath, new Vector4(0.35f, 1.4f, 1.4f, 1.0f), addVolumetricFog: true);

            GameObject globalVolumeGo = new GameObject("Global Volume");
            globalVolumeGo.layer = layerDefault;
            Volume globalVolume = globalVolumeGo.AddComponent<Volume>();
            globalVolume.isGlobal = true;
            globalVolume.priority = 0.0f;
            globalVolume.sharedProfile = globalProfile;

            GameObject cameraAGo = new GameObject("CameraA");
            Camera cameraA = cameraAGo.AddComponent<Camera>();
            cameraA.tag = "MainCamera";
            cameraA.rect = new Rect(0.0f, 0.0f, 0.5f, 1.0f);
            cameraA.transform.position = new Vector3(-1.5f, 1.0f, -4.0f);
            cameraA.transform.LookAt(cube.transform);
            cameraA.cullingMask = ~0;
            cameraA.clearFlags = CameraClearFlags.SolidColor;
            cameraA.backgroundColor = new Color(0.42f, 0.36f, 0.30f, 1.0f);
            CameraComponent cameraAComponent = cameraAGo.AddComponent<CameraComponent>();
            cameraAComponent.volumeLayerMask = 1 << layerDefault;

            GameObject cameraBGo = new GameObject("CameraB");
            Camera cameraB = cameraBGo.AddComponent<Camera>();
            cameraB.rect = new Rect(0.5f, 0.0f, 0.5f, 1.0f);
            cameraB.transform.position = new Vector3(1.5f, 1.0f, -4.0f);
            cameraB.transform.LookAt(cube.transform);
            cameraB.cullingMask = ~0;
            cameraB.clearFlags = CameraClearFlags.SolidColor;
            cameraB.backgroundColor = new Color(0.24f, 0.32f, 0.40f, 1.0f);
            CameraComponent cameraBComponent = cameraBGo.AddComponent<CameraComponent>();
            cameraBComponent.volumeLayerMask = (1 << layerDefault) | (1 << layerWater);
            DebugViewCapture.EnsureLitLivenessMarker(cameraA);

            GameObject localVolumeGo = new GameObject("Local Volume");
            localVolumeGo.layer = layerWater;
            localVolumeGo.transform.position = cameraB.transform.position;
            BoxCollider box = localVolumeGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4.0f, 4.0f, 4.0f);
            Volume localVolume = localVolumeGo.AddComponent<Volume>();
            localVolume.isGlobal = false;
            localVolume.priority = 10.0f;
            localVolume.blendDistance = 2.0f;
            localVolume.sharedProfile = localProfile;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] Volume fixture written: {ScenePath}");
        }

        static VolumeProfile CreateOrReplaceProfile(string assetPath, Vector4 colorSaturation, bool addVolumetricFog)
        {
            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(assetPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ColorGrading grading = profile.Add<ColorGrading>(true);
            grading.active = true;
            grading.ColorSaturation.overrideState = true;
            grading.ColorSaturation.value = colorSaturation;

            if (addVolumetricFog)
            {
                VolumetricFog fog = profile.Add<VolumetricFog>(true);
                fog.active = true;
                fog.Density.overrideState = true;
                fog.Density.value = 0.25f;
                fog.MaxDistance.overrideState = true;
                fog.MaxDistance.value = 64.0f;
                fog.Albedo.overrideState = true;
                fog.Albedo.value = new Color(0.2f, 0.9f, 0.95f, 1.0f);
                fog.DepthSlices.overrideState = true;
                fog.Height.overrideState = true;
                fog.HeightFalloff.overrideState = true;
                fog.Anisotropy.overrideState = true;
                fog.AmbientIntensity.overrideState = true;
                fog.TemporalWeight.overrideState = true;
            }

            AssetDatabase.CreateAsset(profile, assetPath);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
