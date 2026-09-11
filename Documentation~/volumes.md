# Volumes

All Infinity Volume components use `[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]`.

Optional features (`SSR`, `SSGI`, `SSAO`, fog, cloud, contact shadow, SSS, RTAO) expose an `enable` flag and implement `IsActive()`. Bloom / Vignette / Grain activate when `intensity > 0`. Film tonemap activates when `mode == Film`.

`overrideState` is only a scene-override marker. The default profile must contain every consumed type. `Validate & Complete` appends missing types. `Reset to packaged defaults` is a separate confirmed action.
