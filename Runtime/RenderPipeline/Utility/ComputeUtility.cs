using UnityEngine;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    // Records the shared utility kernels (Shaders/Utility/Compute_Utility.compute). The pass that calls these must
    // declare the target buffer with WriteBuffer.
    internal static class ComputeUtility
    {
        internal const string ClearBufferKernelName = "ClearBufferUInt";
        internal const string ClearIndirectArgsKernelName = "ClearIndirectArgs";
        internal const int ThreadGroupSize = 64;
        // Bytes per dispatch-argument triplet (three uints).
        internal const int IndirectDispatchArgsStride = 12;

        static readonly int s_ClearTarget = Shader.PropertyToID("_ClearTarget");
        static readonly int s_ClearCount = Shader.PropertyToID("_ClearCount");
        static readonly int s_ClearValue = Shader.PropertyToID("_ClearValue");

        internal static bool HasKernels(ComputeShader shader)
        {
            return GraphicsUtility.HasRequiredKernels(shader, ClearBufferKernelName, ClearIndirectArgsKernelName);
        }

        // Writes value into the first count uints of buffer.
        internal static void ClearBuffer<T>(in T commands, ComputeShader shader, RGBufferHandle buffer, int count, uint value)
            where T : struct, IComputeCommands
        {
            if (count <= 0)
            {
                return;
            }

            int kernel = shader.FindKernel(ClearBufferKernelName);
            commands.SetComputeBufferParam(shader, kernel, s_ClearTarget, buffer);
            commands.SetComputeIntParam(shader, s_ClearCount, count);
            commands.SetComputeIntParam(shader, s_ClearValue, unchecked((int)value));
            commands.DispatchCompute(shader, kernel, (count + ThreadGroupSize - 1) / ThreadGroupSize, 1, 1);
        }

        // Resets argsCount dispatch triplets to (0, 1, 1).
        internal static void ClearIndirectDispatchArgs<T>(in T commands, ComputeShader shader, RGBufferHandle argsBuffer, int argsCount)
            where T : struct, IComputeCommands
        {
            if (argsCount <= 0)
            {
                return;
            }

            int kernel = shader.FindKernel(ClearIndirectArgsKernelName);
            commands.SetComputeBufferParam(shader, kernel, s_ClearTarget, argsBuffer);
            commands.SetComputeIntParam(shader, s_ClearCount, argsCount);
            commands.DispatchCompute(shader, kernel, (argsCount + ThreadGroupSize - 1) / ThreadGroupSize, 1, 1);
        }
    }
}
