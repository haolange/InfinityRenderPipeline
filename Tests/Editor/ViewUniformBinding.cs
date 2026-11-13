using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Binds a ViewUniform record the way the pipeline does at runtime: through ViewUniformBuffer, a global constant
    // buffer that compute shaders read without per-shader parameters. Dispose after the last dispatch that reads it.
    internal sealed class ViewUniformBinding : IDisposable
    {
        readonly ViewUniformBuffer m_Buffer = new ViewUniformBuffer();

        // Binds now, for shaders dispatched directly.
        internal ViewUniformBinding(in ViewUniform record)
        {
            using var commands = new CommandBuffer { name = "ViewUniformBinding" };
            m_Buffer.Upload(commands, record);
            Graphics.ExecuteCommandBuffer(commands);
        }

        // Binds in order with dispatches recorded into the test's own command buffer.
        internal ViewUniformBinding(in ViewUniform record, CommandBuffer commands) => m_Buffer.Upload(commands, record);

        public void Dispose() => m_Buffer.Dispose();
    }
}
