using System;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal interface IFrameSubmission
    {
        bool NativeSubmitted { get; }
        void Queue(CommandBuffer commands);
        void Submit();
    }

    internal struct ContextFrameSubmission : IFrameSubmission
    {
        readonly ScriptableRenderContext m_Context;
        public bool NativeSubmitted { get; private set; }
        internal ContextFrameSubmission(ScriptableRenderContext context) { m_Context = context; NativeSubmitted = false; }
        public void Queue(CommandBuffer commands) => m_Context.ExecuteCommandBuffer(commands);
        public void Submit()
        {
            using (new PipelineFrameMeasurement.NativeSubmitScope(true)) m_Context.Submit();
            NativeSubmitted = true;
            RenderFaultValidation.AfterSubmit();
        }
    }

    internal static class FrameSubmission
    {
        internal static bool Execute<T>(T submission, CommandBuffer commands, ref Exception firstError, out bool submitted, out bool fenceCommandsQueued, out bool nativeSubmitted)
            where T : IFrameSubmission
        {
            bool queued = false;
            submitted = false;
            try { submission.Queue(commands); queued = true; }
            catch (Exception error) { if (firstError == null) firstError = error; }
            // Queue failure cannot undo earlier accepted work from other passes/cameras.
            try { submission.Submit(); submitted = true; }
            catch (Exception error) { if (firstError == null) firstError = error; }
            fenceCommandsQueued = queued;
            nativeSubmitted = submission.NativeSubmitted;
            return queued && submitted;
        }
    }
}
