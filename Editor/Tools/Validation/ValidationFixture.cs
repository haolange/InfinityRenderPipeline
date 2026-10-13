using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    /// <summary>
    /// Shared builder of the 0.5.0 validation fixtures (Assets/Scene/Validation/Validation_&lt;name&gt;.unity with its
    /// materials and profile in Validation_&lt;name&gt;Assets). Re-running a creator replaces the scene and its assets.
    /// </summary>
    internal sealed class ValidationFixture
    {
        public delegate void ShapeEdit(ref FLightShape shape);

        const string SceneDirectory = "Assets/Scene/Validation";

        readonly string m_Name;
        readonly string m_AssetDirectory;
        readonly Scene m_Scene;
        Shader m_LitShader;

        public string ScenePath => SceneDirectory + "/Validation_" + m_Name + ".unity";
        public string AssetDirectory => m_AssetDirectory;

        // Stops Play mode first (fixture creation edits scenes and assets); returns false when it deferred the call.
        public static bool LeavePlayMode(Action create)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => create();
            return false;
        }

        public ValidationFixture(string name)
        {
            m_Name = name;
            m_AssetDirectory = SceneDirectory + "/Validation_" + name + "Assets";
            Directory.CreateDirectory(Path.GetFullPath(m_AssetDirectory));
            if (File.Exists(Path.GetFullPath(ScenePath)))
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }
            AssetDatabase.Refresh();
            m_Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public string AssetPath(string fileName) => m_AssetDirectory + "/" + fileName;

        public T SaveAsset<T>(T asset, string fileName) where T : UnityEngine.Object
        {
            string path = AssetPath(fileName);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public Material Lit(string name, Color color, float roughness, float metallic = 0, Color? emission = null)
        {
            m_LitShader ??= Shader.Find("InfinityPipeline/InfinityLit");
            if (m_LitShader == null)
            {
                throw new InvalidOperationException("InfinityRP Validation: InfinityPipeline/InfinityLit shader missing.");
            }
            var material = new Material(m_LitShader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Roughness", roughness);
            material.SetFloat("_Reflectance", metallic);
            material.SetFloat("_SpecularLevel", 0.5f);
            material.SetColor("_EmissionColor", emission ?? Color.black);
            MaterialRouteUtility.ApplyPassState(material);
            return SaveAsset(material, name + ".mat");
        }

        public GameObject Primitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, bool isStatic = false, Vector3? euler = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler ?? Vector3.zero));
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (isStatic)
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
            return go;
        }

        public Light Light(string name, Vector3 position, Vector3 euler, EInfinityLightType type, Color color, float intensity, float range = 10, ShapeEdit edit = null)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            Light light = go.AddComponent<Light>();
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = 60;
            light.shadows = LightShadows.None;
            InfinityAdditionalLightData data = InfinityAdditionalLightData.GetOrCreate(light, false);
            FLightShape shape = FLightShape.Default(type);
            edit?.Invoke(ref shape);
            data.SetShape(shape);
            return light;
        }

        public Camera Camera(Vector3 position, Vector3 lookAt, Color background)
        {
            var go = new GameObject("Camera");
            Camera camera = go.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            go.AddComponent<InfinityAdditionalCameraData>();
            ValidationSceneUtility.EnsureLitLivenessMarker(camera);
            return camera;
        }

        // A global volume whose profile (saved with the fixture) holds the given components, every parameter overridden
        // with its default unless configure changes it.
        public VolumeProfile GlobalVolume(Action<VolumeProfile> configure)
        {
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            configure(profile);
            SaveAsset(profile, "Validation_" + m_Name + "_Profile.asset");
            foreach (VolumeComponent component in profile.components)
            {
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            var go = new GameObject("Global Volume");
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = profile;
            return profile;
        }

        public static T AddComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.active = true;
            return component;
        }

        public void Save()
        {
            EditorSceneManager.SaveScene(m_Scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InfinityRP][Validation] {m_Name} fixture written: {ScenePath}");
        }
    }
}
