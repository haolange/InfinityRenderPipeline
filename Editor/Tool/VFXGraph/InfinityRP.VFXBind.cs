using System;
using UnityEditor.VFX;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Tool.Editor
{
    class VFXInfinityRPBinder : VFXSRPBinder
    {
        public override string templatePath { get { return "Packages/com.unity.render-pipelines.infinity/Editor/Tools/VFXGraph/Shaders"; } }
        public override string runtimePath { get { return "Packages/com.unity.render-pipelines.infinity/Runtime/Tools/VFXGraph/Shaders"; } }
        public override string SRPAssetTypeStr { get { return typeof(InfinityRenderPipelineAsset).Name; } }
        public override Type SRPOutputDataType { get { return null; } }
    }
}