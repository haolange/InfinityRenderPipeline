using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.ShaderGraph.Serialization;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // Values match MaterialRouteUtility (_SurfaceRoute, _TranslucentStage) and InfinityLit.
    enum InfinitySurfaceRoute
    {
        Deferred = 0,
        Forward = 1,
    }

    enum InfinityTranslucentStage
    {
        None = 0,
        T0 = 1,
        T1 = 2,
        T2 = 3,
    }

    enum InfinityBlendMode
    {
        Alpha,
        Premultiply,
        Additive,
    }

    // Values match UnityEngine.Rendering.CullMode.
    enum InfinityRenderFace
    {
        Front = 2,
        Back = 1,
        Both = 0,
    }

    /// <summary>
    /// Shader Graph target of the Infinity Render Pipeline. This code compiles into Unity.ShaderGraph.Editor through the
    /// asmref next to it (AGENTS.md, Integration exceptions): Target and SubTarget are internal in Shader Graph 17.6.
    /// It references Infinity only by strings, which Tools/StaticCheck/static_check.py verifies: the pipeline asset
    /// type name, package include and template paths, LightMode tags and material GUI class names.
    /// </summary>
    sealed class InfinityTarget : Target
    {
        static readonly GUID kSourceCodeGuid = new GUID("1a15634840a54fe1b327af47162afd61"); // InfinityTarget.cs

        public const string kPipelineAssetType = "InfinityTech.Rendering.Pipeline.InfinityRenderPipelineAsset";
        public const string kPipelineTag = "InfinityRenderPipeline";
        public const string kPackagePath = "Packages/com.infinity.render-pipeline";
        public const string kTemplatePath = kPackagePath + "/Editor/ShaderGraph/Templates/ShaderPass.template";
        public const string kIncludePath = kPackagePath + "/Editor/ShaderGraph/Includes/";
        public static readonly string[] kSharedTemplateDirectories =
            GenerationUtils.GetDefaultSharedTemplateDirectories().Union(new[] { kPackagePath + "/Editor/ShaderGraph/Templates" }).ToArray();

        List<SubTarget> m_SubTargets;
        List<string> m_SubTargetNames;
        PopupField<string> m_SubTargetField;
        TextField m_CustomGUIField;

        [SerializeField]
        JsonData<SubTarget> m_ActiveSubTarget;

        [SerializeField]
        InfinitySurfaceRoute m_SurfaceRoute = InfinitySurfaceRoute.Deferred;

        [SerializeField]
        InfinityTranslucentStage m_TranslucentStage = InfinityTranslucentStage.None;

        [SerializeField]
        InfinityBlendMode m_BlendMode = InfinityBlendMode.Alpha;

        [SerializeField]
        InfinityRenderFace m_RenderFace = InfinityRenderFace.Front;

        [SerializeField]
        bool m_AlphaClip;

        [SerializeField]
        string m_CustomEditorGUI;

        public InfinityTarget()
        {
            displayName = "Infinity";
            m_SubTargets = TargetUtils.GetSubTargets(this);
            m_SubTargetNames = m_SubTargets.Select(x => x.displayName).ToList();
            TargetUtils.ProcessSubTargetList(ref m_ActiveSubTarget, ref m_SubTargets);
        }

        public override SubTarget activeSubTarget
        {
            get => m_ActiveSubTarget.value;
            set => m_ActiveSubTarget = value;
        }

        int activeSubTargetIndex => m_SubTargets.IndexOf(m_ActiveSubTarget);

        public InfinitySurfaceRoute surfaceRoute { get => m_SurfaceRoute; set => m_SurfaceRoute = value; }
        public InfinityTranslucentStage translucentStage { get => m_TranslucentStage; set => m_TranslucentStage = value; }
        public InfinityBlendMode blendMode { get => m_BlendMode; set => m_BlendMode = value; }
        public InfinityRenderFace renderFace { get => m_RenderFace; set => m_RenderFace = value; }
        public bool alphaClip { get => m_AlphaClip; set => m_AlphaClip = value; }
        public string customEditorGUI { get => m_CustomEditorGUI; set => m_CustomEditorGUI = value; }

        public bool translucent => m_TranslucentStage != InfinityTranslucentStage.None;

        // Renderer-list queues: opaque routes draw in the opaque range, translucent stages in the transparent range.
        public string renderType => translucent ? $"{UnityEditor.ShaderGraph.RenderType.Transparent}" : $"{UnityEditor.ShaderGraph.RenderType.Opaque}";
        public string renderQueue => translucent
            ? $"{UnityEditor.ShaderGraph.RenderQueue.Transparent}"
            : (m_AlphaClip ? $"{UnityEditor.ShaderGraph.RenderQueue.AlphaTest}" : $"{UnityEditor.ShaderGraph.RenderQueue.Geometry}");

        internal static bool IsInfinityPipeline(RenderPipelineAsset asset) => asset != null && asset.GetType().FullName == kPipelineAssetType;

        public override bool IsActive() => IsInfinityPipeline(GraphicsSettings.currentRenderPipeline) && activeSubTarget.IsActive();

        public override bool WorksWithSRP(RenderPipelineAsset scriptableRenderPipeline) => IsInfinityPipeline(scriptableRenderPipeline);

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
            TargetUtils.ProcessSubTargetList(ref m_ActiveSubTarget, ref m_SubTargets);
            if (m_ActiveSubTarget.value == null)
                return;
            m_ActiveSubTarget.value.target = this;
            m_ActiveSubTarget.value.Setup(ref context);

            if (!string.IsNullOrEmpty(m_CustomEditorGUI))
                context.AddCustomEditorForRenderPipeline(m_CustomEditorGUI, kPipelineAssetType);
        }

        public override void OnAfterMultiDeserialize(string json)
        {
            TargetUtils.ProcessSubTargetList(ref m_ActiveSubTarget, ref m_SubTargets);
            m_ActiveSubTarget.value.target = this;
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            context.AddField(Fields.GraphVertex);
            context.AddField(Fields.GraphPixel);
            m_ActiveSubTarget.value.GetFields(ref context);
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            context.AddBlock(BlockFields.VertexDescription.Position);
            context.AddBlock(BlockFields.VertexDescription.Normal);
            context.AddBlock(BlockFields.VertexDescription.Tangent);
            m_ActiveSubTarget.value.GetActiveBlocks(ref context);
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            base.CollectShaderProperties(collector, generationMode);
            activeSubTarget.CollectShaderProperties(collector, generationMode);
        }

        public override void ProcessPreviewMaterial(Material material)
        {
            m_ActiveSubTarget.value.ProcessPreviewMaterial(material);
        }

        public override object saveContext => m_ActiveSubTarget.value?.saveContext;

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo)
        {
            if (m_ActiveSubTarget.value == null)
                return;

            m_SubTargetField = new PopupField<string>(m_SubTargetNames, activeSubTargetIndex);
            context.AddProperty("Material", m_SubTargetField, (evt) =>
            {
                if (Equals(activeSubTargetIndex, m_SubTargetField.index))
                    return;
                registerUndo("Change Material");
                m_ActiveSubTarget = m_SubTargets[m_SubTargetField.index];
                onChange();
            });

            m_ActiveSubTarget.value.GetPropertiesGUI(ref context, onChange, registerUndo);

            m_CustomGUIField = new TextField("") { value = customEditorGUI };
            m_CustomGUIField.RegisterCallback<FocusOutEvent>(s =>
            {
                if (Equals(customEditorGUI, m_CustomGUIField.value))
                    return;
                registerUndo("Change Custom Editor GUI");
                customEditorGUI = m_CustomGUIField.value;
                onChange();
            });
            context.AddProperty("Custom Editor GUI", m_CustomGUIField, (evt) => { });
        }

        // Surface routing of lit graphs: the defaults written into new materials (MaterialRouteUtility applies the pass
        // enables from the material properties).
        public void GetRoutePropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo)
        {
            context.AddProperty("Surface Route", new EnumField(InfinitySurfaceRoute.Deferred) { value = surfaceRoute }, !translucent, (evt) =>
            {
                if (Equals(surfaceRoute, evt.newValue))
                    return;
                registerUndo("Change Surface Route");
                surfaceRoute = (InfinitySurfaceRoute)evt.newValue;
                onChange();
            });
            GetTranslucencyPropertiesGUI(ref context, onChange, registerUndo, true);
        }

        public void GetTranslucencyPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo, bool allowOpaque)
        {
            if (allowOpaque)
            {
                context.AddProperty("Translucent Stage", new EnumField(InfinityTranslucentStage.None) { value = translucentStage }, (evt) =>
                {
                    if (Equals(translucentStage, evt.newValue))
                        return;
                    registerUndo("Change Translucent Stage");
                    translucentStage = (InfinityTranslucentStage)evt.newValue;
                    onChange();
                });
            }
            context.AddProperty("Blending Mode", new EnumField(InfinityBlendMode.Alpha) { value = blendMode }, translucent || !allowOpaque, (evt) =>
            {
                if (Equals(blendMode, evt.newValue))
                    return;
                registerUndo("Change Blend");
                blendMode = (InfinityBlendMode)evt.newValue;
                onChange();
            });
            context.AddProperty("Render Face", new EnumField(InfinityRenderFace.Front) { value = renderFace }, (evt) =>
            {
                if (Equals(renderFace, evt.newValue))
                    return;
                registerUndo("Change Render Face");
                renderFace = (InfinityRenderFace)evt.newValue;
                onChange();
            });
            context.AddProperty("Alpha Clipping", new Toggle() { value = alphaClip }, (evt) =>
            {
                if (Equals(alphaClip, evt.newValue))
                    return;
                registerUndo("Change Alpha Clip");
                alphaClip = evt.newValue;
                onChange();
            });
        }

        public bool TrySetActiveSubTarget(Type subTargetType)
        {
            if (!subTargetType.IsSubclassOf(typeof(SubTarget)))
                return false;
            foreach (SubTarget subTarget in m_SubTargets)
            {
                if (subTarget.GetType() == subTargetType)
                {
                    m_ActiveSubTarget = subTarget;
                    return true;
                }
            }
            return false;
        }

        // Hidden float property declared in UnityPerMaterial (SRP Batcher compatible) and in the Properties block.
        internal static void AddMaterialFloat(PropertyCollector collector, string referenceName, float defaultValue)
        {
            collector.AddShaderProperty(new Vector1ShaderProperty
            {
                floatType = FloatType.Default,
                hidden = true,
                overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.UnityPerMaterial,
                value = defaultValue,
                overrideReferenceName = referenceName,
            });
        }
    }

    abstract class InfinitySubTarget : SubTarget<InfinityTarget>
    {
        static readonly GUID kSourceCodeGuid = new GUID("4fe78fab7d48413eb4f9f29ec9ad7f87"); // InfinityPasses.cs

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
        }
    }
}
