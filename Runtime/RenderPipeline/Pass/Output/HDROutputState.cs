using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        HDROutputSettings m_OwnedHdrOutput;
        bool m_PreviousAutomaticHdrTonemapping;

        void InheritHdrTonemappingOwnership(InfinityRenderPipeline previous)
        {
            if (previous == null || previous.m_OwnedHdrOutput == null) return;
            m_OwnedHdrOutput = previous.m_OwnedHdrOutput;
            m_PreviousAutomaticHdrTonemapping = previous.m_PreviousAutomaticHdrTonemapping;
            previous.m_OwnedHdrOutput = null;
        }

        void UpdateHdrTonemapping()
        {
            if (pipelineAsset.outputMode != EOutputMode.HDR)
            {
                RestoreHdrTonemapping();
                return;
            }
            var state = OutputTransformUtility.ReadHdrOutput();
            if (!state.active || state.changeRequested) return;
            var output = HDROutputSettings.main;
            if (m_OwnedHdrOutput != output)
            {
                RestoreHdrTonemapping();
                m_OwnedHdrOutput = output;
                m_PreviousAutomaticHdrTonemapping = output.automaticHDRTonemapping;
            }
            output.automaticHDRTonemapping = false;
        }

        void RestoreHdrTonemapping()
        {
            if (m_OwnedHdrOutput == null) return;
            if (m_OwnedHdrOutput.available) m_OwnedHdrOutput.automaticHDRTonemapping = m_PreviousAutomaticHdrTonemapping;
            m_OwnedHdrOutput = null;
        }
    }
}
