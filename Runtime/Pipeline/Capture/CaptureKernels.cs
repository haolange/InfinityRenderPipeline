#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using InfinityTech.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Capture kernels, resolved with PipelineKernels. Capture is a diagnostic request: a missing kernel fails the request
    // that needs it, never pipeline construction.
    internal sealed class CaptureKernels
    {
        internal readonly ComputeKernel texture;
        internal readonly ComputeKernel surfaceMetadata;
        internal readonly ComputeKernel depth;
        internal readonly ComputeKernel bufferUIntBytes;

        internal CaptureKernels(InfinityRenderPipelineRuntimeShaders shaders)
        {
            // The container is optional: a player built without Development has none, and capture then fails per request.
            bool hasContainer = GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineCaptureShaders capture);
            const string source = "GlobalSettings CaptureShaders.";
            ComputeShader textureShader = hasContainer ? capture.captureTextureShader : null;
            ComputeShader depthShader = hasContainer ? capture.captureDepthShader : null;
            string textureSource = hasContainer ? source + nameof(InfinityRenderPipelineCaptureShaders.captureTextureShader) : "GlobalSettings has no CaptureShaders container";
            string depthSource = hasContainer ? source + nameof(InfinityRenderPipelineCaptureShaders.captureDepthShader) : "GlobalSettings has no CaptureShaders container";
            texture = ComputeKernel.Resolve(textureShader, textureSource, "CaptureTexture");
            surfaceMetadata = ComputeKernel.Resolve(textureShader, textureSource, "CaptureSurfaceMetadata");
            depth = ComputeKernel.Resolve(depthShader, depthSource, "CaptureDepth");
            bufferUIntBytes = ComputeKernel.Resolve(shaders.utilityShader, "GlobalSettings RuntimeShaders." + nameof(InfinityRenderPipelineRuntimeShaders.utilityShader), "CaptureBufferUIntBytes");
        }

        internal static ComputeKernel Require(in ComputeKernel kernel)
        {
            if (!kernel.isValid)
            {
                throw new InvalidOperationException("InfinityRP: capture requires a kernel: " + kernel.missingReason);
            }
            return kernel;
        }
    }
}
#endif
