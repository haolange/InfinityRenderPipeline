using System;
using UnityEngine;
using UnityEditor;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;
using Expression = System.Linq.Expressions.Expression;
using UnityEngine.Rendering;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class LightOverrideEditor : LightEditor
    {
        #region TargetObject
        LightComponent m_LightComponent;
        SerializedObject m_SerializeLight;
        #endregion //TargetObject

        #region General
        public SerializedProperty state;
        public SerializedProperty lightType;
        public SerializedProperty lightLayer;
        #endregion //General

        #region Emission
        public SerializedProperty intensity;
        public SerializedProperty color;
        public SerializedProperty temperature;
        public SerializedProperty range;
        public SerializedProperty diffuse;
        public SerializedProperty specular;
        public SerializedProperty radius;
        public SerializedProperty length;
        public SerializedProperty innerAngle;
        public SerializedProperty outerAngle;
        public SerializedProperty width;
        public SerializedProperty height;
        #endregion //Emission

        #region IndirectLighting
        public SerializedProperty enableIndirect;
        public SerializedProperty indirectIntensity;
        #endregion //IndirectLighting

        #region IESCookie
        public SerializedProperty IESTexture;
        public SerializedProperty cookieTexture;
        #endregion //IESCookie

        #region Shadow
        public SerializedProperty enableShadow;
        public SerializedProperty shadowLayer;
        public SerializedProperty shadowType;
        public SerializedProperty resolution;
        public SerializedProperty nearPlane;
        public SerializedProperty minSoftness;
        public SerializedProperty maxSoftness;
        #endregion //Shadow

        #region ContactShadow
        public SerializedProperty enableContactShadow;
        public SerializedProperty contactShadowLength;
        #endregion //ContactShadow

        #region VolumetricFog
        public SerializedProperty enableVolumetric;
        public SerializedProperty volumetricIntensity;
        public SerializedProperty volumetricOcclusion;
        #endregion //VolumetricFog

        #region Performance
        public SerializedProperty maxDrawDistance;
        public SerializedProperty maxDrawDistanceFade;
        #endregion //Performance

        #region ShowPannalProperty
        public bool showGeneral { get { return m_LightComponent.showGeneral; } set { m_LightComponent.showGeneral = value; } }
        public bool showEmission { get { return m_LightComponent.showEmission; } set { m_LightComponent.showEmission = value; } }
        public bool showIndirect { get { return m_LightComponent.showIndirect; } set { m_LightComponent.showIndirect = value; } }
        public bool showLightMask { get { return m_LightComponent.showLightMask; } set { m_LightComponent.showLightMask = value; } }
        public bool showShadow { get { return m_LightComponent.showShadow; } set { m_LightComponent.showShadow = value; } }
        public bool showContactShadow { get { return m_LightComponent.showContactShadow; } set { m_LightComponent.showContactShadow = value; } }
        public bool showVolumetricFog { get { return m_LightComponent.showVolumetricFog; } set { m_LightComponent.showVolumetricFog = value; } }
        public bool showPerformance { get { return m_LightComponent.showPerformance; } set { m_LightComponent.showPerformance = value; } }
        #endregion //ShowPannalProperty

        #region Temperature
        Texture2D m_TemperatureLUT;
        Func<Vector3, Vector3, float, float> SliderSizeFunc;
        Action<GUIContent, SerializedProperty> SliderWithTextureFunc;
        #endregion //Temperature

        protected override void OnEnable()
        {
            InitTemperatureInfo();
            InitSerializeComponent();
            InitSerializePeroperty();
        }

        public override void OnInspectorGUI()
        {
            m_SerializeLight.Update();

            DrawGeneral();
            DrawEmission();
            DrawIndirect();
            DrawLightMask();
            DrawShadow();
            DrawVoluemtric();
            DrawPerformance();

            m_LightComponent.OnGUIChange();
            m_SerializeLight.ApplyModifiedProperties();
        }

        protected override void OnSceneGUI()
        {
            
        }

        private void DrawGeneral()
        {
            showGeneral = EditorGUILayout.BeginFoldoutHeaderGroup(showGeneral, "General");
            if (showGeneral)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(state, new GUIContent("State"));
                EditorGUILayout.PropertyField(lightType, new GUIContent("Type"));
                EditorGUILayout.PropertyField(lightLayer, new GUIContent("Layer"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawEmission()
        {
            showEmission = EditorGUILayout.BeginFoldoutHeaderGroup(showEmission, "Emission");
            if (showEmission)
            {
                EditorGUI.indentLevel++;

                #region LightColor&&Intensity
                float indent = 15 * EditorGUI.indentLevel;

                Rect LineRect = EditorGUILayout.GetControlRect();
                Rect ColorRect = LineRect;
                ColorRect.width += indent - 70;
                Rect IntensityRect = ColorRect;
                IntensityRect.x += ColorRect.width - indent + 5;
                IntensityRect.width = 70 - 5;

                color.colorValue = EditorGUI.ColorField(ColorRect, new GUIContent("Color"), color.colorValue);
                intensity.floatValue = EditorGUI.FloatField(IntensityRect, intensity.floatValue);
                #endregion

                SliderWithTextureFunc(new GUIContent("Temperature"), temperature);

                if (state.enumValueIndex != (int)ELightState.Static)
                {
                    EditorGUILayout.Slider(diffuse, 0, 1, new GUIContent("Diffuse"));
                    EditorGUILayout.Slider(specular, 0, 1, new GUIContent("Specular"));
                }

                switch (lightType.enumValueIndex)
                {
                    case (int)ELightType.Directional:
                        EditorGUILayout.PropertyField(radius, new GUIContent("Radius"));
                        break;

                    case (int)ELightType.Point:
                        EditorGUILayout.PropertyField(range, new GUIContent("Range"));
                        EditorGUILayout.PropertyField(radius, new GUIContent("Radius"));
                        EditorGUILayout.PropertyField(length, new GUIContent("Length"));
                        break;

                    case (int)ELightType.Spot:
                        EditorGUILayout.PropertyField(range, new GUIContent("Range"));
                        EditorGUILayout.PropertyField(radius, new GUIContent("Radius"));
                        EditorGUILayout.Slider(innerAngle, 0, 90, new GUIContent("InnerAngle"));
                        EditorGUILayout.Slider(outerAngle, 0, 90, new GUIContent("OuterAngle"));
                        break;

                    case (int)ELightType.Rect:
                        EditorGUILayout.PropertyField(range, new GUIContent("Range"));
                        EditorGUILayout.PropertyField(width, new GUIContent("Width"));
                        EditorGUILayout.PropertyField(height, new GUIContent("Height"));
                        break;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawIndirect()
        {
            showIndirect = EditorGUILayout.BeginFoldoutHeaderGroup(showIndirect, "Indirect Lighting");
            if (showIndirect)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(enableIndirect, new GUIContent("Enable"));
                using (new EditorGUI.DisabledScope(!enableIndirect.boolValue))
                {
                    EditorGUILayout.Slider(indirectIntensity, 0, 16, new GUIContent("Intensity"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLightMask()
        {
            if (state.enumValueIndex != (int)ELightState.Static)
            {
                showLightMask = EditorGUILayout.BeginFoldoutHeaderGroup(showLightMask, "Mask");
                if (showLightMask)
                {
                    EditorGUI.indentLevel++;

                    if (lightType.enumValueIndex != (int)ELightType.Directional)
                    {
                        EditorGUILayout.PropertyField(IESTexture, new GUIContent("IES Texture"));
                    }

                    EditorGUILayout.PropertyField(cookieTexture, new GUIContent("Cookie Texture"));
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
        }

        private void DrawShadow()
        {
            showShadow = EditorGUILayout.BeginFoldoutHeaderGroup(showShadow, "Shadow");
            if (showShadow)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(enableShadow, new GUIContent("Enable"));
                using (new EditorGUI.DisabledScope(!enableShadow.boolValue))
                {
                    if (state.enumValueIndex != (int)ELightState.Static)
                    {
                        EditorGUILayout.PropertyField(shadowType, new GUIContent("Type"));
                        EditorGUILayout.PropertyField(shadowLayer, new GUIContent("Layer"));
                        EditorGUILayout.PropertyField(resolution, new GUIContent("Resolution"));
                        EditorGUILayout.Slider(nearPlane, 0, 10, new GUIContent("Near Plane"));
                        switch (shadowType.enumValueIndex)
                        {
                            case (int)EShadowType.PCSS:
                                EditorGUILayout.Slider(minSoftness, 0, 2, new GUIContent("Min Softness"));
                                EditorGUILayout.Slider(maxSoftness, 0, 2, new GUIContent("Max Softness"));
                                break;
                        }

                        showContactShadow = EditorGUILayout.Foldout(showContactShadow, "Contact Shadow", true, EditorStyles.foldoutHeader);
                        if (showContactShadow)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(enableContactShadow, new GUIContent("Enable"));
                            using (new EditorGUI.DisabledScope(!enableContactShadow.boolValue))
                            {
                                EditorGUILayout.Slider(contactShadowLength, 0, 1, new GUIContent("Length"));
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawVoluemtric()
        {
            if (state.enumValueIndex != (int)ELightState.Static)
            {
                showVolumetricFog = EditorGUILayout.BeginFoldoutHeaderGroup(showVolumetricFog, "Volumetric Fog");
                if (showVolumetricFog)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(enableVolumetric, new GUIContent("Enable"));
                    using (new EditorGUI.DisabledScope(!enableVolumetric.boolValue))
                    {
                        EditorGUILayout.Slider(volumetricIntensity, 0, 32, new GUIContent("Intensity"));
                        if (enableShadow.boolValue == true)
                        {
                            EditorGUILayout.Slider(volumetricOcclusion, 0, 1, new GUIContent("Occlusion"));
                        }
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
        }

        private void DrawPerformance()
        {
            if (state.enumValueIndex != (int)ELightState.Static)
            {
                if (lightType.enumValueIndex != (int)ELightType.Directional)
                {
                    showPerformance = EditorGUILayout.BeginFoldoutHeaderGroup(showPerformance, "Performance");
                    if (showPerformance)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(maxDrawDistance, new GUIContent("MaxDrawDistance"));
                        EditorGUILayout.PropertyField(maxDrawDistanceFade, new GUIContent("MaxDrawDistanceFade"));
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                }
            }
        }

        private void InitTemperatureInfo()
        {
            m_TemperatureLUT = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.infinity.render-pipeline/Editor/Resources/ColorTemperature.png");
            var sliderMethod = typeof(EditorGUILayout).
                       GetMethod(
                            "SliderWithTexture",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                            null,
                            System.Reflection.CallingConventions.Any,
                            new[] { typeof(GUIContent), typeof(SerializedProperty), typeof(float), typeof(float), typeof(float), typeof(Texture2D), typeof(GUILayoutOption[]) },
                            null);
            var paramLabel = Expression.Parameter(typeof(GUIContent), "label");
            var paramProperty = Expression.Parameter(typeof(SerializedProperty), "property");
            var call = Expression.Call(sliderMethod, paramLabel, paramProperty,
                                    Expression.Constant(1000f),
                                    Expression.Constant(20000.0f),
                                    Expression.Constant(2.4f),
                                    Expression.Constant(m_TemperatureLUT),
                                    Expression.Constant(null, typeof(GUILayoutOption[])));
            SliderWithTextureFunc = Expression.Lambda<Action<GUIContent, SerializedProperty>>(call, paramLabel, paramProperty).Compile();

            var sizeSliderMethod = typeof(Handles).GetMethod(
                                    "SizeSlider",
                                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                                    null,
                                    System.Reflection.CallingConventions.Any,
                                    new[] { typeof(Vector3), typeof(Vector3), typeof(float) },
                                    null);
            var position = Expression.Parameter(typeof(Vector3), "position");
            var direction = Expression.Parameter(typeof(Vector3), "direction");
            var size = Expression.Parameter(typeof(float), "size");
            call = Expression.Call(sizeSliderMethod, position, direction, size);
            SliderSizeFunc = Expression.Lambda<Func<Vector3, Vector3, float, float>>(call, position, direction, size).Compile();
        }

        private void InitSerializeComponent()
        {
            Light light = (Light)target;
            m_LightComponent = light.gameObject.GetComponent<LightComponent>();
            if (m_LightComponent == null) 
            {
                light.gameObject.AddComponent<LightComponent>();
                m_LightComponent = light.gameObject.GetComponent<LightComponent>();
            } else {
                m_LightComponent = light.gameObject.GetComponent<LightComponent>();
            }

            m_SerializeLight = new SerializedObject(m_LightComponent);
        }

        private void InitSerializePeroperty()
        {
            state = m_SerializeLight.FindProperty("state");
            lightType = m_SerializeLight.FindProperty("lightType");
            lightLayer = m_SerializeLight.FindProperty("lightLayer");

            intensity = m_SerializeLight.FindProperty("intensity");
            color = m_SerializeLight.FindProperty("color");
            temperature = m_SerializeLight.FindProperty("temperature");
            range = m_SerializeLight.FindProperty("range");
            diffuse = m_SerializeLight.FindProperty("diffuse");
            specular = m_SerializeLight.FindProperty("specular");
            radius = m_SerializeLight.FindProperty("radius");
            length = m_SerializeLight.FindProperty("length");
            innerAngle = m_SerializeLight.FindProperty("innerAngle");
            outerAngle = m_SerializeLight.FindProperty("outerAngle");
            width = m_SerializeLight.FindProperty("width");
            height = m_SerializeLight.FindProperty("height");

            enableIndirect = m_SerializeLight.FindProperty("enableIndirect");
            indirectIntensity = m_SerializeLight.FindProperty("indirectIntensity");

            IESTexture = m_SerializeLight.FindProperty("IESTexture");
            cookieTexture = m_SerializeLight.FindProperty("cookieTexture");

            enableShadow = m_SerializeLight.FindProperty("enableShadow");
            shadowType = m_SerializeLight.FindProperty("shadowType");
            shadowLayer = m_SerializeLight.FindProperty("shadowLayer");
            resolution = m_SerializeLight.FindProperty("resolution");
            nearPlane = m_SerializeLight.FindProperty("nearPlane");
            minSoftness = m_SerializeLight.FindProperty("minSoftness");
            maxSoftness = m_SerializeLight.FindProperty("maxSoftness");

            enableContactShadow = m_SerializeLight.FindProperty("enableContactShadow");
            contactShadowLength = m_SerializeLight.FindProperty("contactShadowLength");

            enableVolumetric = m_SerializeLight.FindProperty("enableVolumetric");
            volumetricIntensity = m_SerializeLight.FindProperty("volumetricIntensity");
            volumetricOcclusion = m_SerializeLight.FindProperty("volumetricOcclusion");

            maxDrawDistance = m_SerializeLight.FindProperty("maxDrawDistance");
            maxDrawDistanceFade = m_SerializeLight.FindProperty("maxDrawDistanceFade");
        }
    }
}
