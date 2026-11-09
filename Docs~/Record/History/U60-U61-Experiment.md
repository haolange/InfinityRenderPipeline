# U60 / U61 experiment close-out

These experiments stay **data-gated**. This cloud VM has no Unity GPU, so the code lands the A/B switches and keeps the proven paths as default.

## U60 Native motion

- Custom `NativeViewMotionHistory` remains the production owner.
- `InfinityDebugDisplaySettings.temporal.preferNativeMotionVectors` adds `PerObjectData.MotionVectors` on Motion and T2 lists for A/B.
- Replace the custom path only after Game / SceneView / dual-camera / skinned / instanced / force-no-motion fixtures match existing TAA tolerances **and** SRP Batcher batches increase.
- Current decision: **retain custom path**. TODO(UNVERIFIED) on Mac.

## U61 TAA + Sharpen fusion

- Two-pass TAA then `ComputeTemporalSharpen` remains the production owner. History stays unsharpened.
- `fuseTemporalSharpen` is reserved and defaults false. Do not ship a half-fused kernel.
- Keep fusion only if history is bit-identical, output max abs error ≤ 1/1024, and GPU time improves.
- Current decision: **retain two-pass**. TODO(UNVERIFIED) on Mac.
