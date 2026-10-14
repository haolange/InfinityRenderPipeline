# Decals

Add **Decal Component** to a GameObject with a MeshRenderer and a decal material (InfinityPipeline/InfinityDecal or an
Infinity Shader Graph with the Decal material). The GameObject's box (unit cube scaled by the transform) is the
projector: it projects along its -Y axis onto every opaque surface inside the box before the GBuffer is written
(DBuffer), changing albedo, normal, roughness and metallic.

- **Size**: the transform scale; the box handle in the Scene view resizes it while keeping the opposite face in place.
- **Draw Order**: higher draws later (on top).
- **UV Scale / UV Bias**: applied to the projected UVs (object xz + 0.5).
- **Start / End Angle Fade**: surfaces turned away from the projector's up axis fade out between the two angles
  (180 / 180 disables the fade).

Decal layers are not supported: a decal affects every opaque surface inside its box. Forward-route and translucent
materials do not receive decals.
