using System;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal interface IFrameSubmission
    {
        void Queue(CommandBuffer commands);
        void Submit();
    }

    internal readonly struct ContextFrameSubmission : IFrameSubmission
    {
        readonly ScriptableRenderContext m_Context;
        internal ContextFrameSubmission(ScriptableRenderContext context) => m_Context = context;
        public void Queue(CommandBuffer commands) => m_Context.ExecuteCommandBuffer(commands);
        public void Submit()
        {
            m_Context.Submit();
            RenderFaultValidation.AfterSubmit();
        }
    }

    internal static class FrameSubmission
    {
        internal static bool Execute<T>(T submission, CommandBuffer commands, ref Exception firstError, out bool submitted)
            where T : IFrameSubmission
        {
            bool queued = false;
            submitted = false;
            try { submission.Queue(commands); queued = true; }
            catch (Exception error) { if (firstError == null) firstError = error; }
            // Queue failure cannot undo earlier accepted work from other passes/cameras.
            try { submission.Submit(); submitted = true; }
            catch (Exception error) { if (firstError == null) firstError = error; }
            return queued && submitted;
        }
    }
}
