using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
        readonly string m_SceneDirectory;
        readonly string m_AssetDirectory;
        readonly Scene m_Scene;
        readonly List<UnityEngine.Object> m_OwnedAssets = new List<UnityEngine.Object>();
        Shader m_LitShader;

        public string ScenePath => m_SceneDirectory + "/Validation_" + m_Name + ".unity";
        public string AssetDirectory => m_AssetDirectory;

        // Stops Play mode first (fixture creation edits scenes and assets); returns false when it deferred the call.
        public static bool LeavePlayMode(Action create)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => create();
            return false;
        }

        public ValidationFixture(string name, string sceneDirectory = SceneDirectory)
        {
            if (string.IsNullOrWhiteSpace(sceneDirectory) || !sceneDirectory.StartsWith("Assets/", StringComparison.Ordinal) ||
                sceneDirectory.Contains("..") || sceneDirectory.Contains('\\') || sceneDirectory.EndsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("Fixture directory must be a project-relative Assets subdirectory.", nameof(sceneDirectory));
            m_Name = name;
            m_SceneDirectory = sceneDirectory;
            m_AssetDirectory = m_SceneDirectory + "/Validation_" + name + "Assets";
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
            m_OwnedAssets.Add(asset);
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
            Color emissionColor = emission ?? Color.black;
            material.SetColor("_EmissionColor", emissionColor);
            material.globalIlluminationFlags = emissionColor.maxColorComponent > 0
                ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
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

        public void Save() => SaveSceneAndAssets(m_Scene, ScenePath, m_OwnedAssets);

        internal static void SaveSceneAndAssets(Scene scene, string scenePath, IEnumerable<UnityEngine.Object> ownedAssets)
        {
            UnityEngine.Object[] assets = ownedAssets.Where(asset => asset != null).Distinct().ToArray();
            var ownedPaths = new HashSet<string>(assets.Select(AssetDatabase.GetAssetPath), StringComparer.Ordinal);
            string[] unrelatedDirty = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .Where(asset => asset != null && EditorUtility.IsPersistent(asset) && AssetDatabase.IsNativeAsset(asset) && EditorUtility.IsDirty(asset))
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) &&
                    (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)) &&
                    !ownedPaths.Contains(path))
                .Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
            // Unity SaveScene also saves unrelated dirty native assets; imported shader compilation state is not such an edit. Reject before entering that native API.
            if (unrelatedDirty.Length != 0)
                throw new InvalidOperationException("Resolve unrelated unsaved asset edits before saving the fixture: " + string.Join(", ", unrelatedDirty));
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("Could not save fixture scene: " + scenePath);
            foreach (UnityEngine.Object asset in assets)
                AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log($"[InfinityRP][Validation] Fixture written: {scenePath}");
        }
    }
}
