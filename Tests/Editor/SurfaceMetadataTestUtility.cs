using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    internal static class SurfaceMetadataTestUtility
    {
        internal static RenderTexture Pack(Texture source)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/SurfaceMetadataContract.compute");
            var output = new RenderTexture(source.width, source.height, 0, GraphicsFormat.R32G32_UInt) { enableRandomWrite = true };
            output.Create();
            int kernel = shader.FindKernel("Pack");
            shader.SetTexture(kernel, "Source", source); shader.SetTexture(kernel, "Packed", output);
            shader.Dispatch(kernel, (source.width + 7) / 8, (source.height + 7) / 8, 1);
            return output;
        }

        internal static RenderTexture DecodeTarget(CommandBuffer commands, Texture source)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/SurfaceMetadataContract.compute");
            var target = new RenderTexture(source.width, source.height, 0, GraphicsFormat.R32G32B32A32_SFloat) { enableRandomWrite = true };
            target.Create(); int kernel = shader.FindKernel("Decode");
            commands.SetComputeTextureParam(shader, kernel, "PackedSource", source);
            commands.SetComputeTextureParam(shader, kernel, "Decoded", target);
            commands.DispatchCompute(shader, kernel, (source.width + 7) / 8, (source.height + 7) / 8, 1);
            return target;
        }

        internal static float[] Read(RenderTexture texture)
        {
            using (var commands = new CommandBuffer())
            {
                var target = DecodeTarget(commands, texture);
                try
                {
                    Graphics.ExecuteCommandBuffer(commands);
                    var request = AsyncGPUReadback.Request(target); request.WaitForCompletion();
                    if (request.hasError) throw new InvalidOperationException("Surface metadata readback failed.");
                    return request.GetData<float>().ToArray();
                }
                finally { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
        }
    }
}
