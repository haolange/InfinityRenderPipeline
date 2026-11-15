using UnityEditor;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Editor
{
    // A release player carries no capture diagnostics (R16). Development players keep the container for
    // -infinityCaptureRequest; the decision follows the Build Settings Development flag.
    sealed class CaptureShadersStripper : IRenderPipelineGraphicsSettingsStripper<InfinityRenderPipelineCaptureShaders>
    {
        public bool active => true;

        public bool CanRemoveSettings(InfinityRenderPipelineCaptureShaders settings) => !EditorUserBuildSettings.development;
    }
}
