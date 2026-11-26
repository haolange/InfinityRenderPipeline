using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// Unified Temporal Reconstruction (TRC) entry pass in the camera frame graph (PRD §9.1): selects execution
    /// from the planned backend and requirements rather than duplicating algorithm branching in the pipeline.
    /// </summary>
    internal sealed class TemporalReconstructionPass
    {
        readonly AntiAliasingPass m_NativeTaaPass;
        readonly SuperResolutionPass m_NativeTaauPass;

        internal TemporalReconstructionPass(AntiAliasingPass nativeTaaPass, SuperResolutionPass nativeTaauPass)
        {
            m_NativeTaaPass = nativeTaaPass;
            m_NativeTaauPass = nativeTaauPass;
        }

        internal void ComputeTemporalReconstruction(in ViewContext context)
        {
            ETemporalBackend backend = context.temporalRequirements.activeBackend;
            if (backend == ETemporalBackend.NativeTAAU)
            {
                using (PipelineCpuMarkers.s_RecordComputeSuperResolution.Auto())
                {
                    m_NativeTaauPass.ComputeSuperResolution(in context);
                }
            }
            else if (backend == ETemporalBackend.NativeTAA)
            {
                using (PipelineCpuMarkers.s_RecordComputeAntiAliasing.Auto())
                {
                    m_NativeTaaPass.ComputeAntiAliasing(in context);
                }
            }
        }
    }
}
