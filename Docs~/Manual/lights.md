# Lights

## Ownership

| Data | Owner |
|---|---|
| Color, intensity, emission display unit, color temperature, range, spot angles, lux reference distance, shadow mode, strength, bias, near plane, culling mask, rendering-layer mask, bake mode, bounce intensity, cookie | Unity `Light` |
| Analytic source shape, intensity edit mode, shadow filter, update mode, light-layer mask, diffuse and specular weights, contact shadows, volumetrics, maximum distance, IES profile | `InfinityAdditionalLightData` |

`LightSourceShape` is the analytic emitter and distribution: `kind` (`Directional` or `Local`), `emitter` (`Round`, `Rectangle`, `Disk`), `distribution` (`Omnidirectional`, `Hemisphere`, `Cone`, `Pyramid`, `ParallelBox`), source radius, source length, source width and height, barn-door angle and length, sun angle, pyramid aspect and box size. When the Infinity component has authored that shape, it is the authority. Edits and API setters write the derived native fields (`type`, `shapeRadius`, `areaSize`, `shadowAngle`) so the lightmapper, culling and gizmos match the renderer. Loading or inspecting a light does not write those fields. A light without the component, or without an authored shape, renders the shape described by its native fields. Editing an Infinity property on a bare light adds the component with no distance limit; editing a shape authors it. Existing unauthored shapes continue to follow the native Light until a shape edit. Unity keeps area size and sun angle only in the Editor, so a player build stores the shape of every scene light with it; a light in a prefab spawned at run time renders the shape only if it was authored by a shape edit or `SetShape`.

Color, intensity and color temperature reach the pipeline as the light's final linear color from Unity's culling result. Canonical `Light.intensity` is stored in **Lux** (`Directional`, `ParallelBox`), **Candela** (`Point`, `Sphere`, `Line`, `Tube`, `Cone`, `Pyramid`) or **Nits** (`Rectangle`, `Disk`, `Disk + Cone`). Switching the display unit converts the shown value without altering emission. **Intensity Edit Mode** (`PhysicallyBased`, `PreserveFlux`, `Artistic`) controls how canonical intensity responds when editing emitter dimensions or spot angles. A camera that sees no directional light has no sun: the sky and the sky ambient are black until one exists.

A bare light uses Hard filtering when its native shadow mode is Hard, and PCF otherwise. The first Infinity edit stores that filter; subsequent changes to the native shadow mode do not change the stored filter. A disabled additional-data component is not used by realtime lighting, but its authored shape can still reach the lightmapper.

The GPU light record has no shape enum. Shape is the combination of these parameters and record flags.

When applying or reverting a Prefab instance's shape, include overrides on both Light and Additional Light Data.

## Shapes

| Kind / Emitter | Distribution | Parameters | Native carrier |
|---|---|---|---|
| Directional / Round | Omnidirectional | Sun Diameter (default 0.5357°) | Directional |
| Local / Round | Omnidirectional | Source Radius (`0` = Point, `>0` = Sphere), Source Length (`>0` = Line or Tube along local X) | Point, or Tube when length `> 0` |
| Local / Round | Cone, Pyramid, ParallelBox | Spot angles + Source Radius (`Cone`/`Pyramid`), Aspect Ratio (`Pyramid`), Beam Width/Height (`ParallelBox`) | Spot, Pyramid or Box |
| Local / Rectangle | Hemisphere | Source Width, Source Height, Barn Door Angle and Length along forward | Rectangle |
| Local / Disk | Hemisphere | Source Radius, one-sided emission | Rectangle for realtime culling; Disc to lightmapper |
| Local / Disk | Cone | Source Radius + Outer/Inner Spot Angles (shifted-apex rim cone, Realtime only) | Rectangle for realtime culling |

A tube's diameter is twice its source radius. Line, Tube and Disk + Cone lights are realtime only; the lightmapper skips them (and prompts to switch Mixed/Baked lights to Realtime when edited in the Inspector).

Specular evaluation uses continuous Karis representative-point construction and quadrature-widened energy normalization so large sources converge to the punctual result at distance. Diffuse uses horizon-clipped Sutherland-Hodgman polygon integration for rectangles, exact horizon-clipped tilted-ellipse integration for disks and Frostbite V3 Lambertian capsule integration for tubes and lines. Volumetric fog and clouds share `EvaluateVolumeLocalLight`. There is no LTC table. Barn-door length narrows rectangle emission. Every shape can cast a realtime shadow. See [Shadows](shadows.md).

## Cookies and IES

The cookie is the native Light cookie. Directional lights tile it over Cookie Size. Spot, pyramid and box lights project a 2D cookie. Point, sphere, line and tube lights use a cube cookie. Rectangle and disk lights use the cookie as an emission texture (including diffuse mip tinting in volumetrics). The IES profile on the Infinity component is the texture produced by the Core RP IES importer (`InfinityIESImporter`): a cube for any local light, and a 2D texture for cone, pyramid and disk-cone spots. Importing an `.ies` file while Infinity is active preserves IES photometric scaling and creates a light prefab. Cookies and profiles share one atlas. Resolution and maximum cookie size are on the pipeline asset.

## Layers and distance

Light Layers on the Infinity component must intersect a receiver's rendering-layer mask. The native Light rendering-layer mask selects casters.

Local lights fade across Distance Fade meters before Maximum Distance and are skipped beyond it. Zero disables the limit. Rendering Debugger > Infinity Lighting reports how many lights were skipped.

## Baking

While Infinity is the active pipeline, its lightmapper delegate sends the analytic shape to Unity's lightmapper: sphere radius as the baked soft-shadow radius, sun angle as the penumbra, pyramid and box spots, inverse-square falloff and the inner spot angle. Intensities are not multiplied by π. The surface BRDF is normalized.

## Disk carrier

An authored disk uses a native Rectangle carrier because a native Disc is omitted from realtime culling and can revert to Baked during Undo deserialization. Disk identity and radius stay on the additional-data component. The lightmapper still receives a disk. Explicit shape edits, or changing a native Disc to a realtime or mixed baking mode, establish the carrier. Loading or inspecting a native Disc does not convert it. A bare native Disc keeps Unity's baked support. Its native fields alone cannot represent an authored realtime disk.
