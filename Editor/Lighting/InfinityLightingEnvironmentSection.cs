using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.Pipeline.Editor;

namespace InfinityTech.Rendering.Editor
{
    // Lighting window → Environment under Infinity. The realtime sky and ambient come from the RP Asset's atmosphere;
    // the lightmapper reads RenderSettings.skybox, which the baking sky ties to the same atmosphere.
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityLightingEnvironmentSection : LightingWindowEnvironmentSection
    {
        SerializedObject m_Asset;

        public override void OnInspectorGUI()
        {
            var asset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            if (asset == null) return;
            if (m_Asset == null || m_Asset.targetObject != asset)
                m_Asset = new SerializedObject(asset);

            EditorGUILayout.LabelField("Realtime Environment", EditorStyles.boldLabel);
            m_Asset.Update();
            EditorGUILayout.PropertyField(m_Asset.FindProperty("atmosphericalProfile"), new GUIContent("Atmosphere Profile",
                "Sky, sun disk, aerial perspective and the sky IBL / SH of the pipeline (RP Asset)."));
            m_Asset.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Sun: the brightest shadowed directional light. Reflections: reflection probes, then the atmosphere sky. Fog and clouds are Volume components.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baked Environment (lightmapper)", EditorStyles.boldLabel);
            bool bakingSky = InfinityBakingSky.SceneUsesBakingSky();
            EditorGUILayout.LabelField("Baking Sky", bakingSky ? "Atmosphere (in sync on every bake)" : "Scene skybox (" + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "none") + ")");
            if (!bakingSky && GUILayout.Button("Use Atmosphere As Baking Sky"))
                InfinityBakingSky.UseAtmosphereAsBakingSky();

            EditorGUI.BeginChangeCheck();
            float ambientIntensity = EditorGUILayout.Slider(new GUIContent("Baked Ambient Intensity", "RenderSettings.ambientIntensity used by the lightmapper."), RenderSettings.ambientIntensity, 0, 8);
            float reflectionIntensity = EditorGUILayout.Slider(new GUIContent("Baked Reflection Intensity", "RenderSettings.reflectionIntensity."), RenderSettings.reflectionIntensity, 0, 1);
            if (EditorGUI.EndChangeCheck())
            {
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
        }
    }
}
