using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Binds a camera uniform record (ViewUniform, ShadowUniform, LightLoopUniform) the way the pipeline does at runtime:
    // through UniformBuffer<T>, a global constant buffer every shader reads without per-shader parameters. A bound
    // global constant buffer takes precedence over values set by name, so a test that dispatches a shader reading one
    // binds its own. Dispose after the last dispatch that reads it.
    internal sealed class UniformBinding<T> : IDisposable where T : unmanaged
    {
        readonly UniformBuffer<T> m_Buffer = new UniformBuffer<T>();

        // Binds now, for shaders dispatched directly.
        internal UniformBinding(in T record)
        {
            using var commands = new CommandBuffer { name = "UniformBinding" };
            m_Buffer.Upload(commands, record);
            Graphics.ExecuteCommandBuffer(commands);
        }

        // Binds in order with dispatches recorded into the test's own command buffer.
        internal UniformBinding(in T record, CommandBuffer commands) => m_Buffer.Upload(commands, record);

        public void Dispose() => m_Buffer.Dispose();
    }
}
