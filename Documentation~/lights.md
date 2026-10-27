# Lights

## Ownership

| Data | Owner |
|---|---|
| Color, intensity, color temperature, range, spot angles, shadow mode, strength, bias, near plane, culling mask, rendering-layer mask, bake mode, bounce intensity, cookie | Unity `Light` |
| Analytic source shape, shadow filter, update mode, light-layer mask, diffuse and specular weights, contact shadows, volumetrics, maximum distance, IES profile | `InfinityAdditionalLightData` |

`FLightShape` is the analytic emitter: type, spot shape, source radius, soft source radius, source length, source width and height, barn-door angle and length, source angle, soft source angle, pyramid aspect and box size. When the Infinity component has authored that shape, it is the authority. Edits and API setters write the derived native fields (`type`, `shapeRadius`, `areaSize`, `shadowAngle`) so the lightmapper, culling and gizmos match the renderer. Loading a scene does not write those fields. A light without the component, or without an authored shape, renders the shape described by its native fields. Adding the component adopts that shape.

The GPU light record has no shape enum. Shape is the combination of these parameters and record flags.

## Shapes

| Type | Parameters | Native carrier |
|---|---|---|
| Directional | Source angle (sun disk, default 0.5357°), soft source angle | Directional |
| Point | Source radius (sphere), soft source radius, source length (line when the radius is 0, tube otherwise) | Point, or Tube when the length is positive |
| Spot | Cone, pyramid (aspect) or box (size). Cone and pyramid may set a source radius | Spot, Pyramid or Box |
| Rectangle | Source width, source height, barn-door angle and length. Emission is along the light forward axis | Rectangle |
| Disk | Source radius. Emission is one-sided | Rectangle for realtime culling. The lightmapper receives a disk |

A tube's diameter is twice its source radius. Tube and line lights are realtime only. The lightmapper skips them.

Specular evaluation picks a representative point on the emitter and normalizes energy so a large source matches the punctual result at distance. Sphere and disk diffuse use a horizon-clipped form factor. Lines and tubes use line irradiance. Rectangles use a polygon form factor. There is no LTC table. Soft source radius and soft source angle widen the highlight without moving it. Barn-door length narrows the rectangle emission cone. Every shape can cast a realtime shadow. See [Shadows](shadows.md).

## Cookies and IES

The cookie is the native Light cookie. Directional lights tile it over Cookie Size. Spot, pyramid and box lights project a 2D cookie. Point lights use a cube cookie. Rectangle and disk lights use the cookie as an emission texture. The IES profile on the Infinity component is the texture produced by the Core RP IES importer: a cube for any local light, and a 2D texture for cone and pyramid spots. Importing an `.ies` file while Infinity is active also creates a light prefab. Cookies and profiles share one atlas. Resolution and maximum cookie size are on the pipeline asset.

## Layers and distance

Light Layers on the Infinity component must intersect a receiver's rendering-layer mask. The native Light rendering-layer mask selects casters.

Local lights fade across Distance Fade meters before Maximum Distance and are skipped beyond it. Zero disables the limit. Rendering Debugger > Infinity Lighting reports how many lights were skipped.

## Baking

While Infinity is the active pipeline, its lightmapper delegate sends the analytic shape to Unity's lightmapper: sphere radius as the baked soft-shadow radius, sun angle as the penumbra, pyramid and box spots, inverse-square falloff and the inner spot angle. Intensities are not multiplied by π. The surface BRDF is normalized.

## Disk carrier

An authored disk uses a native Rectangle carrier because a native Disc is omitted from realtime culling and can revert to Baked during Undo deserialization. Disk identity and radius stay on the additional-data component. The lightmapper still receives a disk. Explicit shape or baking-mode edits establish the carrier. Loading a scene, or adopting an existing native Disc in the Inspector, does not convert it. A bare native Disc keeps Unity's baked support. Its native fields alone cannot represent an authored realtime disk.
