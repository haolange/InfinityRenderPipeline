using System;
using UnityEngine;
using UnityEditor.ShaderGraph;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // InfinityUnlit as a graph: an emissive translucent surface drawn in the T2 stage, with motion vectors and reactive
    // mask like InfinityUnlit.shader.
    sealed class InfinityUnlitSubTarget : InfinitySubTarget
    {
        static readonly GUID kSourceCodeGuid = new GUID("364e95d048654395820f179db1a6c842"); // InfinityUnlitSubTarget.cs

        public InfinityUnlitSubTarget()
        {
            displayName = "Unlit";
        }

        public override bool IsActive() => true;

        public override void Setup(ref TargetSetupContext context)
        {
            base.Setup(ref context);
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
            var subShader = new SubShaderDescriptor
            {
                pipelineTag = InfinityTarget.kPipelineTag,
                renderType = $"{RenderType.Transparent}",
                renderQueue = $"{UnityEditor.ShaderGraph.RenderQueue.Transparent}",
                generatesPreview = true,
                passes = new PassCollection
                {
                    InfinityPasses.RasterTranslucentT2Pass(target, InfinityPasses.ColorBlocks),
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
            context.AddBlock(BlockFields.SurfaceDescription.Emission);
            context.AddBlock(BlockFields.SurfaceDescription.Alpha);
            context.AddBlock(BlockFields.SurfaceDescription.AlphaClipThreshold, target.alphaClip);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo)
        {
            target.GetTranslucencyPropertiesGUI(ref context, onChange, registerUndo, false);
        }
    }
}
