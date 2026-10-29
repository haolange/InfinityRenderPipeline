# Volumes

Infinity Volume components declare `[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]`.

Optional features implement `IsActive()`. Screen-space reflection, screen-space indirect diffuse, horizon ambient occlusion, ray-traced ambient occlusion, volumetric fog, volumetric clouds, contact shadows and subsurface scattering each have an enable flag. Bloom, vignette and grain are active when intensity is greater than zero. Film tonemap is active when the mode is ACES. The supported modes are None and ACES.

`overrideState` marks a scene override. It does not turn a feature on, and it does not gate required exposure. The default profile contains every consumed component type. Window > Infinity > Validate Default Volume Profile checks that profile and does not rewrite a complete one. Window > Infinity > Append Missing Default Volume Components adds absent types and keeps existing components, identities and values. Reset to packaged defaults is a confirmed action on the Global Settings default-profile drawer. It preserves subasset identities and unrelated components.

Atmosphere is not a Volume. It is an `AtmosphericalProfile` on the pipeline asset.

## Quality

Screen-space reflection, screen-space indirect diffuse, horizon ambient occlusion, ray-traced ambient occlusion, contact shadows, volumetric fog and volumetric clouds have a Quality parameter.

- Low, Medium, High and Ultra read a preset in `Runtime/RenderPipeline/Quality/QualityPresets.<Feature>.cs`. The inspector shows those values read-only. Copy Preset To Custom writes them onto the Volume.
- Custom, the default, uses the values on the Volume. Profiles authored before that control keep their values.

Intensity, radius, density, color and distance always come from the Volume. Passes read the resolved settings struct and do not branch on the quality level.

## Screen-space reflection

1. Classify each 8×8 trace tile by its smallest roughness. Tiles with no reflective deferred pixel are skipped. Tiles whose smoothest pixel is below Linear Roughness use the Hi-Z walk. The others use a 2D march plus Binary Search Steps. Forward-route pixels, and pixels rougher than Max Roughness, are not traced.
2. Trace Samples GGX visible-normal rays per trace pixel, from 1 to 4. The ray stops at the near plane, the far plane or the screen edge. Trace Behind keeps going after a surface. Toward Ray allows rays that travel toward the camera; otherwise those rays miss. Half Resolution traces one pixel of each 2×2 quad and rotates through the quad. BRDF Bias trims the lobe. Low and Medium use 1 ray. High and Ultra use 2.
3. Resolve at full resolution reads the trace sample records. It does not sample screen color. The center pixel uses all Samples records. Neighbors on the Poisson disk, inside Resolve Radius, contribute only their sample 0. Resolve Samples is that reuse count, including the center, and is not the traced ray count. High and Ultra use 16. A positive inverse pdf is a hit. Its point is that sample's origin plus `Dir * HitT`. A direction into the receiver is dropped. An inverse pdf of zero is an empty sample (back face, zero edge fade, or invalid previous color) and is ignored. A negative inverse pdf is a miss and is also ignored: it does not darken the hit or lower the blend weight. The blend weight is the average screen-edge fade of the hits. No hit leaves the weight at 0, and the environment term stays. The hits also produce an unpremultiplied RGB mean and standard deviation. Pixels smoother than Mirror Roughness keep one mirror ray.
4. Temporal accumulation samples the surface motion vector and the hit point. Each history color is weighted by `exp2(-10 * luma(dist))` against the reconstruction mean and deviation, then clamped to mean ± deviation. A history sample whose confidence is below half this frame's blend weight is dropped. A kept history is not darker than this frame's hit color. The stored alpha is this frame's edge fade. Age is tracked up to Temporal Weight.
5. An optional bilateral cleanup runs only where the reconstruction variance, smoothed against the previous variance with hysteresis 0.5, is high. It does not feed history.

The reflected color is the accepted unsharpened previous frame. Debug views show tile class, ray hit, confidence and history age.

## Screen-space indirect diffuse

This component has no resolve-sample count and no resolve radius. Those controls belonged to an earlier ray-reuse path and are not read.

1. Trace Samples cosine-distributed rays per half-resolution work pixel, from 1 to 4, and average them. The march is the Hi-Z walk. Trace Behind and Toward Ray use the same meaning as reflections. Each hit samples full-resolution accepted previous color. The work grid is always half resolution and rotates through the 2×2 quad. Intensity Scale is 0 to 10. Low and Medium use 1 ray. High and Ultra use 2.
2. The first temporal stage reads the trace and the previous wide-stage history, then a world-space filter with a 6 m base radius and no normal weight.
3. The second temporal stage reads that mix and the previous narrow-stage history, then a world-space filter with a 3 m base radius and `saturate(dot)` normal weight. Each stage stores `lerp(filtered, temporal, 0.5 * accumSpeed)`. With a mature history, `accumSpeed` is 0.93.
4. Bilateral reconstruction always writes full resolution from the rotating quad representatives, including the shorter last interval on an odd edge.

Missed rays add no radiance. That portion of the pixel keeps its lightmap, probe or sky ambient. The two histories are the half-resolution mixes. Surface history is an integer texture: exact depth bits, a packed octahedral normal, age and a validity bit. Invalid and off-screen history contribute nothing.

The gathered light is one bounce from the accepted previous color. It does not read the current frame's lighting buffer.
