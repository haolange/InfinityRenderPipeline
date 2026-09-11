# UI

`SupportedRenderingFeatures.rendersUIOverlay` is enabled. After OutputTransform, a Raster `UIOverlay` pass calls `ScriptableRenderContext.DrawUIOverlay` onto `DisplayColorBuffer`.

Screen Space Camera and World Space canvases go through the T2 translucent RendererList (`SRPDefaultUnlit` / untagged). Overlay canvases use the UI overlay pass.

Create `Validation_UI` from `Window > Infinity > Create UI Fixture` on the Example project.
