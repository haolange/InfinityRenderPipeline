# Shadows

## Pipeline Asset

| Setting | Meaning |
|---|---|
| Cascade Shadow Map Resolution | Atlas of the shadowed directional light (2 × 2 cascades). |
| Cascade Count (1–4), Cascade Split Ratios, Cascade Blend Width | Splits as fractions of Shadow Distance; the last split ends at Shadow Distance. Pixels within Blend Width of a cascade's end blend into the next one. |
| Shadow Distance | Directional shadows end here (also the culling shadow distance). |
| Local Shadow Map Resolution | Atlas of local lights, 4 × 4 tiles. |
| Max Shadowed Local Lights | Light-count budget; with the tile budget it decides which local lights cast shadows (strength over distance, single-tile shapes first on ties). |
| Cached Shadow Atlas Width | Persistent atlas for OnEnable / OnDemand lights. |
| Shadow Filter Quality | Low / Medium / High / Ultra presets or Custom (PCF radius, PCSS blocker / filter samples and maximum penumbra, EVSM exponent, light-bleeding reduction, moment blur). |

## Per light

**Shadow Filter** (Infinity Light inspector → Shadows):

- **Hard**: one depth comparison.
- **PCF**: tent filter built from GatherRed and manual comparisons (no comparison sampler).
- **PCSS**: blocker search and penumbra estimate; the light size is the shape's Source Radius (local) or Source Angle
  (sun), the same size that shapes highlights and baked soft shadows.
- **VSM / EVSM**: a moment atlas at half resolution, built only for slices that use it, blurred separably, with
  light-bleeding reduction.

Every shape casts shadows: point, sphere, line and tube lights render six cube faces; cone, pyramid and box spots,
rect and disk lights one tile (box spots orthographic, rect / disk from behind the emitter with a 120° field of view).

**Shadow Update Mode** (local lights): Every Frame; On Enable (re-renders when enabled or when the light's shadow
parameters change); On Demand (re-renders on first use and after `InfinityAdditionalLightData.RequestShadowMapRendering()`).
Cached tiles hold static casters as of their last render.

## Shadowmask

A Mixed light in Shadowmask mode (Lighting Settings Mixed Lighting = Shadowmask, Quality Shadowmask Mode =
Shadowmask) renders realtime shadows from dynamic objects only and takes static occlusion from the shadowmask.
Distance Shadowmask renders every caster in realtime up to Shadow Distance and uses the shadowmask beyond it.

## Contact shadows

Enable **Contact Shadows** on the light and the **Contact Shadow** Volume component. The sun traces one long ray per
pixel; up to 24 local lights (largest on screen first) trace short rays through the clustered list. Deferred and
Forward surfaces use them; volumetrics do not. Quality presets set the step counts.

## Debugging

Rendering Debugger → Infinity Lighting: Cascade / Local / Cached Shadow Atlas, Shadow Moments, Contact Shadows views,
cached tile counters. Fixture: Window → Infinity → Create Shadows Fixture.
