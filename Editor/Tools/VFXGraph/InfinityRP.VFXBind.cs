using System;
using UnityEditor.VFX;
using InfinityTech.Rendering.Pipeline;
using UnityEngine;

namespace InfinityTech.Tool.Editor
{
    class VFXInfinityRPBinder : VFXSRPBinder
    {
        public override string templatePath { get { return "Packages/com.infinity.render-pipeline/Editor/Tool/VFXGraph/Shaders"; } }
        public override string runtimePath { get { return "Packages/com.infinity.render-pipeline/Runtime/Tool/VFXGraph/Shaders"; } }
        public override string SRPAssetTypeStr { get { return typeof(InfinityRenderPipelineAsset).Name; } }
        public override Type SRPOutputDataType { get { return null; } }

        public override bool IsShaderVFXCompatible(Shader shader)
        {
            // InfinityRP does not implement a VFX Graph binder. This is not a compatibility test.
            return false;
        }
    }
}