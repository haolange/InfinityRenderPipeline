using System;
using UnityEngine;
using UnityEditor;
using InfinityTech.Component;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;
using Expression = System.Linq.Expressions.Expression;

namespace InfinityTech.Editor.Component
{
    [CanEditMultipleObjects]
    [CustomEditorForRenderPipeline(typeof(Light), typeof(InfinityRenderPipelineAsset))]
    public class LightOverrideEditor : LightEditor
    {
        #region Variable
        #region TargetObject
        Light UnityLight { get { return target as Light; } }
        LightComponent IntinityLight;
        SerializedObject IntinityLightObject;
        #endregion //TargetObject

        #region General
        public SerializedProperty LightState;
        public SerializedProperty LightType;
        public SerializedProperty LightLayer;
        #endregion //General

        #region Emission
        public SerializedProperty LightIntensity;
        public SerializedProperty LightColor;
        public SerializedProperty Temperature;
        public SerializedProperty LightRange;
        public SerializedProperty LightDiffuse;
        public SerializedProperty LightSpecular;
        public SerializedProperty SourceRadius;
        public SerializedProperty SourceLength;
        public SerializedProperty SourceInnerAngle;
        public SerializedProperty SourceOuterAngle;
        public SerializedProperty SourceWidth;
        public SerializedProperty SourceHeight;
        #endregion //Emission

        #region IndirectLighting
        public SerializedProperty EnableGlobalIllumination;
        public SerializedProperty GlobalIlluminationIntensity;
        #endregion //IndirectLighting

        #region IESCookie
        public SerializedProperty EnableIES;
        public SerializedProperty IESIntensity;
        public SerializedProperty IESTexture;
        public SerializedProperty EnableCookie;
        public SerializedProperty CookieTexture;
        #endregion //IESCookie

        #region Shadow
        public SerializedProperty EnableShadow;
        public SerializedProperty ShadowLayer;
        public SerializedProperty ShadowType;
        public SerializedProperty ShadowResolution;
        public SerializedProperty ShadowColor;
        public SerializedProperty ShadowIntensity;
        public SerializedProperty ShadowBias;
        public SerializedProperty ShadowNormalBias;
        public SerializedProperty ShadowNearPlane;
        public SerializedProperty MinSoftness;
        public SerializedProperty MaxSoftness;
        public SerializedProperty CascadeType;
        public SerializedProperty ShadowDistance;
        #endregion //Shadow

        #region ContactShadow
        public SerializedProperty EnableContactShadow;
        public SerializedProperty ContactShadowLength;
        #endregion //ContactShadow

        #region VolumetricFog
        public SerializedProperty EnableVolumetric;
        public SerializedProperty VolumetricScatterIntensity;
        public SerializedProperty VolumetricScatterOcclusion;
        #endregion //VolumetricFog

        #region Performance
        public SerializedProperty MaxDrawDistance;
        public SerializedProperty MaxDrawDistanceFade;
        #endregion //Performance

        #region ShowPannalProperty
        public bool ShowGeneral = true;
        public bool ShowEmission = true;
        public bool ShowGlobalillumination = true;
        public bool ShowLightMask = false;
        public bool ShowIES = true;
        public bool ShowCookie = true;
        public bool ShowShadow = false;
        public bool ShowContactShadow = true;
        public bool ShowVolumetricFog = true;
        public bool ShowPerformance = true;
        #endregion //ShowPannalProperty

        #region Temperature
        Texture2D TemperatureLUT;
        Action<GUIContent, SerializedProperty> TemperatureSlider;
        Func<Vector3, Vector3, float, float> TemperatureSliderSize;
        #endregion //Temperature

        #endregion //Variable


        #region Function
        protected override void OnEnable()
        {
            IntinityLight = UnityLight.gameObject.GetComponent<LightComponent>();
            if (IntinityLight == null) {
                UnityLight.gameObject.AddComponent<LightComponent>();
                IntinityLight = UnityLight.gameObject.GetComponent<LightComponent>();
            } else {
                IntinityLight = UnityLight.gameObject.GetComponent<LightComponent>();
            }

            IntinityLightObject = new SerializedObject(IntinityLight);

            LightState = IntinityLightObject.FindProperty("LightState");
            LightType = IntinityLightObject.FindProperty("LightType");
            LightLayer = IntinityLightObject.FindProperty("LightLayer");

            LightIntensity = IntinityLightObject.FindProperty("LightIntensity");
            LightColor = IntinityLightObject.FindProperty("LightColor");
            Temperature = IntinityLightObject.FindProperty("Temperature");
            LightRange = IntinityLightObject.FindProperty("LightRange");
            LightDiffuse = IntinityLightObject.FindProperty("LightDiffuse");
            LightSpecular = IntinityLightObject.FindProperty("LightSpecular");
            SourceRadius = IntinityLightObject.FindProperty("SourceRadius");
            SourceLength = IntinityLightObject.FindProperty("SourceLength");
            SourceInnerAngle = IntinityLightObject.FindProperty("SourceInnerAngle");
            SourceOuterAngle = IntinityLightObject.FindProperty("SourceOuterAngle");
            SourceWidth = IntinityLightObject.FindProperty("SourceWidth");
            SourceHeight = IntinityLightObject.FindProperty("SourceHeight");

            EnableGlobalIllumination = IntinityLightObject.FindProperty("EnableGlobalIllumination");
            GlobalIlluminationIntensity = IntinityLightObject.FindProperty("GlobalIlluminationIntensity");

            EnableIES = IntinityLightObject.FindProperty("EnableIES");
            IESTexture = IntinityLightObject.FindProperty("IESTexture");
            IESIntensity = IntinityLightObject.FindProperty("IESIntensity");
            EnableCookie = IntinityLightObject.FindProperty("EnableCookie");
            CookieTexture = IntinityLightObject.FindProperty("CookieTexture");

            EnableShadow = IntinityLightObject.FindProperty("EnableShadow");
            ShadowLayer = IntinityLightObject.FindProperty("ShadowLayer");
            ShadowType = IntinityLightObject.FindProperty("ShadowType");
            ShadowResolution = IntinityLightObject.FindProperty("ShadowResolution");
            ShadowColor = IntinityLightObject.FindProperty("ShadowColor");
            ShadowIntensity = IntinityLightObject.FindProperty("ShadowIntensity");
            ShadowBias = IntinityLightObject.FindProperty("ShadowBias");
            ShadowNormalBias = IntinityLightObject.FindProperty("ShadowNormalBias");
            ShadowNearPlane = IntinityLightObject.FindProperty("ShadowNearPlane");
            MinSoftness = IntinityLightObject.FindProperty("MinSoftness");
            MaxSoftness = IntinityLightObject.FindProperty("MaxSoftness");
            CascadeType = IntinityLightObject.FindProperty("CascadeType");
            ShadowDistance = IntinityLightObject.FindProperty("ShadowDistance");

            EnableContactShadow = IntinityLightObject.FindProperty("EnableContactShadow");
            ContactShadowLength = IntinityLightObject.FindProperty("ContactShadowLength");

            EnableVolumetric = IntinityLightObject.FindProperty("EnableVolumetric");
            VolumetricScatterIntensity = IntinityLightObject.FindProperty("VolumetricScatterIntensity");
            VolumetricScatterOcclusion = IntinityLightObject.FindProperty("VolumetricScatterOcclusion");

            MaxDrawDistance = IntinityLightObject.FindProperty("MaxDrawDistance");
            MaxDrawDistanceFade = IntinityLightObject.FindProperty("MaxDrawDistanceFade");

            InitTemperature();
        }

        public override void OnInspectorGUI()
        {
            DrawInspector();
        }

        protected override void OnSceneGUI()
        {
            
        }

        private void DrawInspector()
        {
            IntinityLightObject.Update();
            //////////////////////
            ShowGeneral = EditorGUILayout.BeginFoldoutHeaderGroup(ShowGeneral, "General");
            if (ShowGeneral)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(LightState, new GUIContent("Light State"));
                EditorGUILayout.PropertyField(LightType, new GUIContent("Light Type"));
                EditorGUILayout.PropertyField(LightLayer, new GUIContent("Light Layer"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            //////////////////////
            ShowEmission = EditorGUILayout.BeginFoldoutHeaderGroup(ShowEmission, "Emission");
            if (ShowEmission)
            {
                EditorGUI.indentLevel++;

                #region MergeLightColorandIntensity
                float indent = 15 * EditorGUI.indentLevel;

                Rect LineRect = EditorGUILayout.GetControlRect();
                Rect ColorRect = LineRect;
                ColorRect.width += indent - 70;
                Rect IntensityRect = ColorRect;
                IntensityRect.x += ColorRect.width - indent + 5;
                IntensityRect.width = 70 - 5;

                LightColor.colorValue = EditorGUI.ColorField(ColorRect, new GUIContent("Light Color"), LightColor.colorValue);
                LightIntensity.floatValue = EditorGUI.FloatField(IntensityRect, LightIntensity.floatValue);
                #endregion //MergeLightColorandIntensity

                TemperatureSlider(new GUIContent("Temperature"), Temperature);

                if (LightState.enumValueIndex != (int)ELightState.Static)
                {
                    EditorGUILayout.Slider(LightDiffuse, 0, 1, new GUIContent("Light Diffuse"));
                    EditorGUILayout.Slider(LightSpecular, 0, 1, new GUIContent("Light Specular"));
                }

                switch (LightType.enumValueIndex)
                {
                    case (int)ELightType.Directional:
                        EditorGUILayout.PropertyField(SourceRadius, new GUIContent("Source Radius"));
                        break;

                    case (int)ELightType.Point:
                        EditorGUILayout.PropertyField(LightRange, new GUIContent("Light Range"));
                        EditorGUILayout.PropertyField(SourceRadius, new GUIContent("Source Radius"));
                        EditorGUILayout.PropertyField(SourceLength, new GUIContent("Source Length"));
                        break;

                    case (int)ELightType.Spot:
                        EditorGUILayout.PropertyField(LightRange, new GUIContent("Light Range"));
                        EditorGUILayout.PropertyField(SourceRadius, new GUIContent("Source Radius"));
                        EditorGUILayout.Slider(SourceInnerAngle, 0, 90, new GUIContent("Source InnerAngle"));
                        EditorGUILayout.Slider(SourceOuterAngle, 0, 90, new GUIContent("Source OuterAngle"));
                        break;

                    case (int)ELightType.Rect:
                        EditorGUILayout.PropertyField(LightRange, new GUIContent("Light Range"));
                        EditorGUILayout.PropertyField(SourceWidth, new GUIContent("Source Width"));
                        EditorGUILayout.PropertyField(SourceHeight, new GUIContent("Source Height"));
                        break;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            //////////////////////
            ShowGlobalillumination = EditorGUILayout.BeginFoldoutHeaderGroup(ShowGlobalillumination, "Indirect Lighting");
            if (ShowGlobalillumination)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(EnableGlobalIllumination, new GUIContent("Enable"));
                using (new EditorGUI.DisabledScope(!EnableGlobalIllumination.boolValue))
                {
                    EditorGUILayout.Slider(GlobalIlluminationIntensity, 0, 16, new GUIContent("Intensity"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            //////////////////////
            if (LightState.enumValueIndex != (int)ELightState.Static)
            {
                ShowLightMask = EditorGUILayout.BeginFoldoutHeaderGroup(ShowLightMask, "Light Mask");
                if (ShowLightMask)
                {
                    EditorGUI.indentLevel++;
                    if (LightType.enumValueIndex != (int)ELightType.Directional)
                    {
                        ShowIES = EditorGUILayout.Foldout(ShowIES, "IES", true, EditorStyles.foldoutHeader);
                        if (ShowIES)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(EnableIES, new GUIContent("Enable"));
                            using (new EditorGUI.DisabledScope(!EnableIES.boolValue))
                            {
                                EditorGUILayout.PropertyField(IESTexture, new GUIContent("Texture"));
                                EditorGUILayout.Slider(IESIntensity, 0, 1, new GUIContent("Intensity"));
                            }
                            EditorGUI.indentLevel--;
                        }
                    }

                    ShowCookie = EditorGUILayout.Foldout(ShowCookie, "Cookie", true, EditorStyles.foldoutHeader);
                    if (ShowCookie)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(EnableCookie, new GUIContent("Enable"));
                        using (new EditorGUI.DisabledScope(!EnableCookie.boolValue))
                        {
                            EditorGUILayout.PropertyField(CookieTexture, new GUIContent("Texture"));
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            //////////////////////
            ShowShadow = EditorGUILayout.BeginFoldoutHeaderGroup(ShowShadow, "Shadow");
            if (ShowShadow)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(EnableShadow, new GUIContent("Enable"));
                using (new EditorGUI.DisabledScope(!EnableShadow.boolValue))
                {
                    if (LightState.enumValueIndex != (int)ELightState.Static)
                    {
                        EditorGUILayout.PropertyField(ShadowLayer, new GUIContent("Layer"));
                        EditorGUILayout.PropertyField(ShadowType, new GUIContent("Type"));
                        EditorGUILayout.PropertyField(ShadowColor, new GUIContent("Color"));
                        EditorGUILayout.Slider(ShadowIntensity, 0, 1, new GUIContent("Opacity"));
                        EditorGUILayout.PropertyField(ShadowResolution, new GUIContent("Resolution"));
                        EditorGUILayout.Slider(ShadowBias, 0, 2, new GUIContent("Depth Bias"));
                        EditorGUILayout.Slider(ShadowNormalBias, 0, 3, new GUIContent("Normal Bias"));
                        EditorGUILayout.Slider(ShadowNearPlane, 0, 10, new GUIContent("Near Plane"));
                        switch (ShadowType.enumValueIndex)
                        {
                            case (int)EShadowType.PCSS:
                                EditorGUILayout.Slider(MinSoftness, 0, 2, new GUIContent("Min Softness"));
                                EditorGUILayout.Slider(MaxSoftness, 0, 2, new GUIContent("Max Softness"));
                                break;
                        }

                        switch (LightType.enumValueIndex)
                        {
                            case (int)ELightType.Directional:
                                EditorGUILayout.PropertyField(CascadeType, new GUIContent("Type"));
                                EditorGUILayout.PropertyField(ShadowDistance, new GUIContent("Distance"));
                                break;
                        }

                        ShowContactShadow = EditorGUILayout.Foldout(ShowContactShadow, "Contact Shadow", true, EditorStyles.foldoutHeader);
                        if (ShowContactShadow)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(EnableContactShadow, new GUIContent("Enable"));
                            using (new EditorGUI.DisabledScope(!EnableContactShadow.boolValue))
                            {
                                EditorGUILayout.Slider(ContactShadowLength, 0, 1, new GUIContent("Length"));
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            //////////////////////
            if (LightState.enumValueIndex != (int)ELightState.Static)
            {
                ShowVolumetricFog = EditorGUILayout.BeginFoldoutHeaderGroup(ShowVolumetricFog, "Volumetric Fog");
                if (ShowVolumetricFog)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(EnableVolumetric, new GUIContent("Enable"));
                    using (new EditorGUI.DisabledScope(!EnableVolumetric.boolValue))
                    {
                        EditorGUILayout.Slider(VolumetricScatterIntensity, 0, 32, new GUIContent("Intensity"));
                        if (EnableShadow.boolValue == true)
                        {
                            EditorGUILayout.Slider(VolumetricScatterOcclusion, 0, 1, new GUIContent("Occlusion"));
                        }
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();

                //////////////////////
                if (LightType.enumValueIndex != (int)ELightType.Directional)
                {
                    ShowPerformance = EditorGUILayout.BeginFoldoutHeaderGroup(ShowPerformance, "Performance");
                    if (ShowPerformance)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(MaxDrawDistance, new GUIContent("MaxDrawDistance"));
                        EditorGUILayout.PropertyField(MaxDrawDistanceFade, new GUIContent("MaxDrawDistanceFade"));
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                }
            }

            //////////////////////
            IntinityLight.OnGUIChange();
            IntinityLightObject.ApplyModifiedProperties();
        }

        private void InitTemperature()
        {
            TemperatureLUT = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.unity.render-pipelines.infinity/Editor/Resources/ColorTemperature.png");
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
                                    Expression.Constant(TemperatureLUT),
                                    Expression.Constant(null, typeof(GUILayoutOption[])));
            TemperatureSlider = Expression.Lambda<Action<GUIContent, SerializedProperty>>(call, paramLabel, paramProperty).Compile();

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
            TemperatureSliderSize = Expression.Lambda<Func<Vector3, Vector3, float, float>>(call, position, direction, size).Compile();
        }
        
        #endregion //Function
    }
}
