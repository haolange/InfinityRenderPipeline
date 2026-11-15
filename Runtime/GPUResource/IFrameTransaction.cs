namespace InfinityTech.Rendering.GPUResource
{
    // An owner of GPU state that a frame writes speculatively. Its owner registers it once and drives every owner in
    // one order: BeginFrame; CameraFailed when a camera's recording or execution throws; CommitFrame when the frame's
    // submission succeeded, RollbackPending otherwise; FlushRetired once the submission's reuse fence is known.
    internal interface IFrameTransaction
    {
        void BeginFrame();
        // queueUncertain: a queue submission failed, so work accepted before the failure may still write.
        void CameraFailed(bool queueUncertain);
        void CommitFrame();
        void RollbackPending();
        void FlushRetired(ResourceReuseFence fence);
    }
}
