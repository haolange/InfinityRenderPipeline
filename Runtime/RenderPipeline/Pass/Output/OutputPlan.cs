using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Resolved once per camera. An encoded output target never aliases a display-linear resource.
    internal readonly struct FOutputPlan
    {
        internal readonly bool linearIntermediate;
        internal readonly bool uiOverlay;
        internal readonly bool wireOverlay;
        internal readonly bool gizmos;
        internal bool editorDepth => wireOverlay || gizmos;

        internal FOutputPlan(bool uiOverlay, EOutputEncodePolicy policy, bool debugView,
            bool linearCapture, bool wireOverlay, bool gizmos)
        {
            this.uiOverlay = uiOverlay;
            this.wireOverlay = wireOverlay;
            this.gizmos = gizmos;
            linearIntermediate = debugView || linearCapture || wireOverlay || gizmos ||
                (uiOverlay && policy != EOutputEncodePolicy.HardwareSRGB);
        }

        internal static bool CoversTarget(Rect normalizedViewport) =>
            normalizedViewport.x == 0 && normalizedViewport.y == 0 &&
            normalizedViewport.width == 1 && normalizedViewport.height == 1;
    }
}
