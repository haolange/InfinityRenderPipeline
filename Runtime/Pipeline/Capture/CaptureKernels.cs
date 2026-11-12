#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using InfinityTech.Core;
using UnityEngine;

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
            const string captureTexture = "Resources/Compute_CaptureTexture";
            ComputeShader textureShader = Resources.Load<ComputeShader>("Compute_CaptureTexture");
            texture = ComputeKernel.Resolve(textureShader, captureTexture, "CaptureTexture");
            surfaceMetadata = ComputeKernel.Resolve(textureShader, captureTexture, "CaptureSurfaceMetadata");
            depth = ComputeKernel.Resolve(Resources.Load<ComputeShader>("Compute_CaptureDepth"), "Resources/Compute_CaptureDepth", "CaptureDepth");
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
