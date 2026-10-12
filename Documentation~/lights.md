# Lights

## Who owns what

| Data | Owner |
|---|---|
| Color, intensity, color temperature, range, spot angles, shadow mode / strength / bias / near plane, culling mask, caster layers, bake mode, bounce intensity, cookie | Unity `Light` |
| Source shape (type, radius, length, width / height, barn doors, sun angle, spot shape), shadow filter, light layers, diffuse / specular weights, contact shadows, volumetrics, maximum distance | `InfinityAdditionalLightData` |

The Infinity Light inspector edits the shape in Unreal Engine form and writes the native fields Unity derives from it
(`type`, `shapeRadius`, `areaSize`, `shadowAngle`), so Unity's lightmapper, culling and gizmos agree with the renderer.
The native fields are written only on edits, never when a scene loads. A light without the Infinity component (created
by a script or imported) renders the shape its native fields describe. Adding the component adopts that shape.

## Types and shapes

| Type | Shape parameters | Native type |
|---|---|---|
| Directional | Source Angle (sun disk diameter, default 0.5357°), Soft Source Angle | Directional |
| Point | Source Radius (sphere), Soft Source Radius, Source Length (line when the radius is 0, tube otherwise) | Point, or Tube when the length is positive |
| Spot | Spot Shape: Cone, Pyramid (aspect ratio) or Box (size); cone and pyramid may have a Source Radius | Spot, Pyramid, Box |
| Rect | Source Width, Source Height, Barn Door Angle / Length; one-sided, emits along the light's forward axis | Rectangle |
| Disk | Source Radius; one-sided | Disc |

A tube's diameter is twice its Source Radius; there is no separate thickness parameter. Tube and line lights are
realtime only (the lightmapper skips them, as in HDRP).

Shading (no LTC): specular uses the Karis representative point for every shape (closest point on the sphere, segment,
tube, rect, disk or sun disk to the reflection ray) with energy normalization; diffuse uses the Frostbite
horizon-clipped form factor for spheres and disks, the Karis line irradiance for lines and tubes and the polygon form
factor for rects. Every shape converges to the punctual result far away, so changing a light's size does not change
its overall brightness. **Soft Source Radius / Angle** widen highlights without moving them. Rect barn doors narrow the
emission cone as Barn Door Length grows relative to the emitter. Pyramid, box, rect and disk lights do not cast
realtime shadows until S9.

## Baking

InfinityRP installs its own lightmapper delegate while it is the active pipeline: Unity's lightmapper receives the
Infinity shape (sphere radius as the baked soft-shadow radius, sun angle as the penumbra, pyramid and box spots),
inverse-square falloff and the real inner spot angle. Intensities are not multiplied by π because InfinityRP's BRDF is
physically normalized.

## Maximum distance

Local lights fade out over **Distance Fade** meters before **Maximum Distance** from the camera and are skipped beyond
it (0 disables the limit). Rendering Debugger → Infinity Lighting shows how many lights were skipped.
