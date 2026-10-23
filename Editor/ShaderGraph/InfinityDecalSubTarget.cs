using System;
using UnityEngine;
using UnityEditor.ShaderGraph;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // InfinityDecal as a graph: a DBufferPass projected through the decal box (object space -0.5..0.5, projecting along
    // -Y). The graph sees the projected position, uv0 = object xz + 0.5 and the projector's tangent frame.
    sealed class InfinityDecalSubTarget : InfinitySubTarget
    {
        static readonly GUID kSourceCodeGuid = new GUID("e8aaf6db0f084cab919a993eb4d01e68"); // InfinityDecalSubTarget.cs

        public InfinityDecalSubTarget()
        {
            displayName = "Decal";
        }

        public override bool IsActive() => true;

        public override void Setup(ref TargetSetupContext context)
        {
            base.Setup(ref context);
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
            var subShader = new SubShaderDescriptor
            {
                pipelineTag = InfinityTarget.kPipelineTag,
                renderType = $"{RenderType.Opaque}",
                renderQueue = "Geometry+100",
                generatesPreview = false,
                passes = new PassCollection
                {
                    InfinityPasses.RasterDBufferPass(target),
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
            context.AddBlock(BlockFields.SurfaceDescription.Occlusion);
            context.AddBlock(BlockFields.SurfaceDescription.Alpha);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo)
        {
        }
    }
}
