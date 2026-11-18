using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // One Render call's engine facts, read once when the call begins (R5) and passed down by value. Nothing stores it past
    // the call: the scriptable render context is valid only inside it.
    internal readonly struct FrameContext
    {
        internal readonly ScriptableRenderContext scriptableRenderContext;
        // Time.frameCount for this call: camera recycling, atlas residency, temporal sequences and capture evidence.
        internal readonly int frame;
        internal readonly ShadowmaskMode shadowmaskMode;

        internal FrameContext(ScriptableRenderContext scriptableRenderContext)
        {
            this.scriptableRenderContext = scriptableRenderContext;
            frame = Time.frameCount;
            shadowmaskMode = QualitySettings.shadowmaskMode;
        }
    }
}
