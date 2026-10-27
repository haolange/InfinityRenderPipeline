# UI

`SupportedRenderingFeatures.rendersUIOverlay` is enabled.

Screen-space overlay canvases are drawn with `CreateUIOverlayRendererList` for a Game camera that has no target texture. Scene view and cameras with a target texture do not receive that list. The overlay pass does not use a native render pass, because Unity does not expose the overlay list as a render-pass attachment.

- When output keeps a display-linear intermediate, the overlay is drawn into `DisplayColorBuffer` before the encode.
- When output writes the camera target directly, the overlay is drawn onto that hardware-sRGB target.

Screen-space camera and world-space canvases use the T2 translucent renderer list (`SRPDefaultUnlit` and untagged passes). They do not use the overlay pass.

Create the UI fixture from Window > Infinity > Create UI Fixture in the Example project.
