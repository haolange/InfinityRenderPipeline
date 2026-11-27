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

- **Light Channels**: a clustered decal affects only the surfaces inside its box that share one of its four light
  channels. DBuffer and GBuffer-target decals affect every opaque surface inside the box.
- **Target** (`_DecalTarget` on the decal material): DBuffer draws into the DBuffer before the GBuffer, so baked
  indirect light and forward materials follow the decal. GBuffer blends albedo and metallic onto the GBuffer after it
  is written; roughness, specular and the octahedral normal are not blended. Set Replace Normal to clip-replace the
  whole GBuffer sample, including the normal. Use the DBuffer target for soft roughness, specular and normal edges. GBuffer-target decals
  leave baked indirect light in the old color.

Unity Terrain writes depth in the GBuffer. DBuffer reconstructs surface positions from the earlier prepass depth,
so it cannot project onto terrain. Clustered decals use the terrain's world position, and GBuffer-target decals
use the depth after terrain draws. Meshes behind terrain can incur extra overdraw because terrain does not provide
depth to the early occlusion stage.

Lit, Shader Graph Lit and terrain apply clustered decals in the GBuffer, and forward Lit applies them again before
shading, so deferred and forward surfaces receive them the same way. Indirect diffuse is computed from the decaled
albedo. InfinitySkin does not receive DBuffer or clustered decals. A direct GBuffer decal changes albedo after that
light was written and does not retint it: its baked indirect light keeps the color of the surface beneath. Use a
DBuffer decal where the indirect light must follow the decal color.
