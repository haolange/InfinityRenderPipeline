using System;
using UnityEngine;
using UnityEditor.ShaderGraph;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // InfinityLit as a graph: every LightMode of InfinityLit.shader, with the surface route and translucent stage as
    // material properties so MaterialRouteUtility enables the same passes it enables for InfinityLit.
    sealed class InfinityLitSubTarget : InfinitySubTarget
    {
        static readonly GUID kSourceCodeGuid = new GUID("2b18117aefbe4a0eacbeab9537b5274d"); // InfinityLitSubTarget.cs
        public const string kMaterialGUI = "InfinityTech.Rendering.Editor.InfinityShaderGraphLitGUI";

        public InfinityLitSubTarget()
        {
            displayName = "Lit";
        }

        public override bool IsActive() => true;

        public override void Setup(ref TargetSetupContext context)
        {
            base.Setup(ref context);
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
            if (!context.HasCustomEditorForRenderPipeline(InfinityTarget.kPipelineAssetType) && string.IsNullOrEmpty(target.customEditorGUI))
                context.AddCustomEditorForRenderPipeline(kMaterialGUI, InfinityTarget.kPipelineAssetType);

            var subShader = new SubShaderDescriptor
            {
                pipelineTag = InfinityTarget.kPipelineTag,
                renderType = target.renderType,
                renderQueue = target.renderQueue,
                generatesPreview = true,
                passes = new PassCollection
                {
                    InfinityPasses.ShadowPass(target),
                    InfinityPasses.ShadowCaster(target),
                    InfinityPasses.DepthPass(target),
                    InfinityPasses.GBufferPass(target),
                    InfinityPasses.ForwardPass(target),
                    InfinityPasses.MotionPass(target),
                    InfinityPasses.TranslucentDepthPass(target),
                    InfinityPasses.TranslucentT0Pass(target),
                    InfinityPasses.TranslucentT1Pass(target),
                    InfinityPasses.TranslucentT2Pass(target, InfinityPasses.LitBlocks),
                    InfinityPasses.Meta(target),
                }
            };
            context.AddSubShader(subShader);
        }

        public override void GetFields(ref TargetFieldContext context)
        {
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            context.AddBlock(BlockFields.SurfaceDescription.BaseColor);
            context.AddBlock(BlockFields.SurfaceDescription.NormalTS);
            context.AddBlock(BlockFields.SurfaceDescription.Metallic);
            context.AddBlock(InfinityBlockFields.SurfaceDescription.Roughness);
            context.AddBlock(InfinityBlockFields.SurfaceDescription.SpecularLevel);
            context.AddBlock(BlockFields.SurfaceDescription.Emission);
            context.AddBlock(BlockFields.SurfaceDescription.Alpha, target.translucent || target.alphaClip);
            context.AddBlock(BlockFields.SurfaceDescription.AlphaClipThreshold, target.alphaClip);
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            InfinityTarget.AddMaterialFloat(collector, "_SurfaceRoute", (float)target.surfaceRoute);
            InfinityTarget.AddMaterialFloat(collector, "_TranslucentStage", (float)target.translucentStage);
            InfinityTarget.AddMaterialFloat(collector, "_RefractionStrength", 0.04f);
            InfinityTarget.AddMaterialFloat(collector, "_Subsurface", 0.0f);
            InfinityTarget.AddMaterialFloat(collector, "_SSSProfileIndex", 0.0f);
            InfinityTarget.AddMaterialFloat(collector, "_SSSThickness", 0.0f);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo)
        {
            target.GetRoutePropertiesGUI(ref context, onChange, registerUndo);
        }
    }
}
