using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Resolved once per camera. An encoded output target never aliases a display-linear resource.
    internal readonly struct OutputPlan
    {
        internal readonly bool linearIntermediate;
        internal readonly bool uiOverlay;
        internal readonly bool wireOverlay;
        internal readonly bool gizmos;
        // A diagnostics pass (debug view) writes the output from the linear intermediate; false in release builds.
        internal readonly bool debugOutput;
        internal bool editorDepth => wireOverlay || gizmos;

        internal OutputPlan(bool uiOverlay, EOutputEncodePolicy policy, bool debugOutput,
            bool linearCapture, bool wireOverlay, bool gizmos)
        {
            this.uiOverlay = uiOverlay;
            this.wireOverlay = wireOverlay;
            this.gizmos = gizmos;
            this.debugOutput = debugOutput;
            linearIntermediate = debugOutput || linearCapture || wireOverlay || gizmos ||
                (uiOverlay && policy != EOutputEncodePolicy.HardwareSRGB);
        }

        internal static bool CoversTarget(Rect normalizedViewport) =>
            normalizedViewport.x == 0 && normalizedViewport.y == 0 &&
            normalizedViewport.width == 1 && normalizedViewport.height == 1;
    }
}
