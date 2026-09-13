# UI

`SupportedRenderingFeatures.rendersUIOverlay` is enabled. A Raster `RenderUIOverlay` pass records `CreateUIOverlayRendererList` and draws that list onto display-linear `DisplayColorBuffer` before final Present encoding. It runs only for Game cameras with no explicit target texture, matching the Editor display-overlay contract. SceneView and RenderTexture cameras do not own this display overlay list.

Screen Space Camera and World Space canvases go through the T2 translucent RendererList (`SRPDefaultUnlit` / untagged). Their rendering does not depend on the display-overlay pass.

Create `Validation_UI` from `Window > Infinity > Create UI Fixture` on the Example project.
