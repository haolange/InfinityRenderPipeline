# Lights

## Ownership

| Data | Owner |
|---|---|
| Color, intensity, color temperature, range, spot angles, shadow mode, strength, bias, near plane, culling mask, rendering-layer mask, bake mode, bounce intensity, cookie | Unity `Light` |
| Analytic source shape, shadow filter, update mode, light-layer mask, diffuse and specular weights, contact shadows, volumetrics, maximum distance, IES profile | `InfinityAdditionalLightData` |

`LightSourceShape` is the analytic emitter: type, spot shape, source radius, soft source radius, source length, source width and height, barn-door angle and length, source angle, soft source angle, pyramid aspect and box size. When the Infinity component has authored that shape, it is the authority. Edits and API setters write the derived native fields (`type`, `shapeRadius`, `areaSize`, `shadowAngle`) so the lightmapper, culling and gizmos match the renderer. Loading or inspecting a light does not write those fields. A light without the component, or without an authored shape, renders the shape described by its native fields. Editing an Infinity property on a bare light adds the component with no distance limit; editing a shape authors it. Existing unauthored shapes continue to follow the native Light until a shape edit. Unity keeps area size and sun angle only in the Editor, so a player build stores the shape of every scene light with it; a light in a prefab spawned at run time renders the shape only if it was authored by a shape edit or `SetShape`.

Color, intensity and color temperature reach the pipeline as the light's final linear color from Unity's culling result. A camera that sees no directional light has no sun: the sky and the sky ambient are black until one exists.

A bare light uses Hard filtering when its native shadow mode is Hard, and PCF otherwise. The first Infinity edit stores that filter; subsequent changes to the native shadow mode do not change the stored filter. A disabled additional-data component is not used by realtime lighting, but its authored shape can still reach the lightmapper.

The GPU light record has no shape enum. Shape is the combination of these parameters and record flags.

When applying or reverting a Prefab instance's shape, include overrides on both Light and Additional Light Data.

## Shapes

| Type | Parameters | Native carrier |
|---|---|---|
| Directional | Sun Diameter (default 0.5357°), Extra Spread | Directional |
| Point | Source radius (sphere), Extra Radius, source length (line when the radius is 0, tube otherwise) | Point, or Tube when the length is positive |
| Spot | Cone, pyramid (aspect) or box (size). Cone and pyramid can set Source Radius and Extra Radius | Spot, Pyramid or Box |
| Rectangle | Source width, source height, barn-door angle and length. Emission is along the light forward axis | Rectangle |
| Disk | Source radius. Emission is one-sided | Rectangle for realtime culling. The lightmapper receives a disk |

A tube's diameter is twice its source radius. Tube and line lights are realtime only. The lightmapper skips them.

Specular evaluation picks a representative point on the emitter and normalizes energy so a large source matches the punctual result at distance. Sphere and disk diffuse use a horizon-clipped form factor. Lines and tubes use line irradiance. Rectangles use a polygon form factor. There is no LTC table. Extra Spread in degrees and Extra Radius in meters widen the realtime highlight and PCSS penumbra. Barn-door length narrows the rectangle emission cone. Every shape can cast a realtime shadow. See [Shadows](shadows.md).

## Cookies and IES

The cookie is the native Light cookie. Directional lights tile it over Cookie Size. Spot, pyramid and box lights project a 2D cookie. Point lights use a cube cookie. Rectangle and disk lights use the cookie as an emission texture. The IES profile on the Infinity component is the texture produced by the Core RP IES importer: a cube for any local light, and a 2D texture for cone and pyramid spots. Importing an `.ies` file while Infinity is active also creates a light prefab. Cookies and profiles share one atlas. Resolution and maximum cookie size are on the pipeline asset.

## Layers and distance

Light Layers on the Infinity component must intersect a receiver's rendering-layer mask. The native Light rendering-layer mask selects casters.

Local lights fade across Distance Fade meters before Maximum Distance and are skipped beyond it. Zero disables the limit. Rendering Debugger > Infinity Lighting reports how many lights were skipped.

## Baking

While Infinity is the active pipeline, its lightmapper delegate sends the analytic shape to Unity's lightmapper: sphere radius as the baked soft-shadow radius, sun angle as the penumbra, pyramid and box spots, inverse-square falloff and the inner spot angle. Intensities are not multiplied by π. The surface BRDF is normalized.

## Disk carrier

An authored disk uses a native Rectangle carrier because a native Disc is omitted from realtime culling and can revert to Baked during Undo deserialization. Disk identity and radius stay on the additional-data component. The lightmapper still receives a disk. Explicit shape edits, or changing a native Disc to a realtime or mixed baking mode, establish the carrier. Loading or inspecting a native Disc does not convert it. A bare native Disc keeps Unity's baked support. Its native fields alone cannot represent an authored realtime disk.
