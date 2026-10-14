using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    // Dispatch helpers of Shaders/RenderingFeature/CubeFilter/Compute_CubeFilter.compute (GGX prefiltering into cube
    // arrays). One mip per call: roughness = mip / (mipCount - 1).
    internal static class CubeFilterUtilityData
    {
        internal const string KernelCubeToArray = "FilterCubeToArray";
        internal const string KernelArrayToArray = "FilterArrayToArray";
        static readonly int s_Params = Shader.PropertyToID("CubeFilter_Params");
        static readonly int s_Source = Shader.PropertyToID("CubeFilter_Source");
        static readonly int s_CubeSource = Shader.PropertyToID("SRV_CubeSource");
        static readonly int s_ArraySource = Shader.PropertyToID("SRV_CubeArraySource");
        static readonly int s_Target = Shader.PropertyToID("UAV_CubeArray");

        internal static float MipRoughness(int mip, int mipCount) => mipCount > 1 ? mip / (float)(mipCount - 1) : 0.0f;

        // The sky: a cube array without mips (pipeline face layout) into the target's slices [baseSlice, baseSlice + 6).
        internal static void FilterArrayToArray<TCommands>(in TCommands commands, ComputeShader shader, RenderTargetIdentifier source,
            RenderTargetIdentifier target, int size, int mip, float roughness, int baseSlice, int samples) where TCommands : struct, IComputeCommands
        {
            int kernel = shader.FindKernel(KernelArrayToArray);
            commands.SetComputeVectorParam(shader, s_Params, new Vector4(size, mip, roughness, baseSlice));
            commands.SetComputeVectorParam(shader, s_Source, new Vector4(size, 1, 1, samples));
            commands.SetComputeTextureParam(shader, kernel, s_ArraySource, source);
            commands.SetComputeTextureParam(shader, kernel, s_Target, target, mip);
            Dispatch(commands, shader, kernel, size, mip);
        }

        // A TextureCube (with its own mips) into the target's slices [baseSlice, baseSlice + 6), scaled by intensity.
        internal static void FilterCubeToArray<TCommands>(in TCommands commands, ComputeShader shader, Texture source, float intensity,
            RenderTargetIdentifier target, int size, int mip, float roughness, int baseSlice, int samples) where TCommands : struct, IComputeCommands
        {
            int kernel = shader.FindKernel(KernelCubeToArray);
            commands.SetComputeVectorParam(shader, s_Params, new Vector4(size, mip, roughness, baseSlice));
            commands.SetComputeVectorParam(shader, s_Source, new Vector4(source.width, Mathf.Max(1, source.mipmapCount), intensity, samples));
            commands.SetComputeTextureParam(shader, kernel, s_CubeSource, source);
            commands.SetComputeTextureParam(shader, kernel, s_Target, target, mip);
            Dispatch(commands, shader, kernel, size, mip);
        }

        static void Dispatch<TCommands>(in TCommands commands, ComputeShader shader, int kernel, int size, int mip) where TCommands : struct, IComputeCommands
        {
            int mipSize = Mathf.Max(1, size >> mip);
            int groups = (mipSize + 7) / 8;
            commands.DispatchCompute(shader, kernel, groups, groups, 6);
        }
    }
}
