# Shadows

## Pipeline Asset

| Setting | Meaning |
|---|---|
| Cascade Shadow Map Resolution | Atlas of the shadowed directional light (2 × 2 cascades). |
| Cascade Count (1–4), Cascade Split Ratios, Cascade Blend Width | Splits as fractions of Shadow Distance; the last split ends at Shadow Distance. Pixels within Blend Width of a cascade's end blend into the next one. |
| Shadow Distance | Directional shadows end here (also the culling shadow distance). |
| Local Shadow Map Resolution | Shared power-of-two quadtree atlas of local lights (`minTile`–`maxTile`, e.g., `128`–`1024` at `2048`). |
| Max Shadowed Local Lights | Light-count budget; high-priority lights keep requested screen-size tiers while lower-priority lights downgrade by power-of-two tiers under contention before dropping. |
| Cached Shadow Atlas Width | Variable-resolution persistent atlas (`ShadowAtlasLayout`) for OnEnable / OnDemand lights. |
| Shadow Filter Quality | Low / Medium / High / Ultra presets or Custom (PCF radius, PCSS blocker / filter samples and maximum penumbra, EVSM exponent, light-bleeding reduction, moment blur). |

## Per light

**Shadow Filter** (Infinity Light inspector → Shadows):

- **Hard**: one depth comparison.
- **PCF**: tent filter built from GatherRed and manual comparisons (no comparison sampler).
- **PCSS**: shape-aware blocker search and penumbra estimate: oriented anisotropic box for Rect, projected segment + transverse disk for Line/Tube, isotropic disk for Sphere/Cone/Pyramid/Disk, with emitter-plane depth baseline (`apexOffset` subtracted) on Rect/Disk/DiskCone so near-emitter occluders produce full penumbra width, and integer tile clamping to avoid cross-tile bleed.
- **VSM / EVSM**: a moment atlas at half resolution matching variable tile rectangles, built only for slices that use it, blurred separably, with light-bleeding reduction.

Every shape casts shadows: point, sphere, line and tube lights render six cube faces (capped at `atlasRes / 4` per face); cone, pyramid, disk-cone and box spots, rect and disk lights one tile (`SpotBox` orthographic, rect / disk from behind the emitter with a 120° field of view, `DiskCone` from its rim apex). Screen-space resolution tiers use 1.25× / 0.75× hysteresis and an 8-frame cooldown to avoid camera-jitter thrashing.

**Shadow Update Mode** (local lights): Every Frame; On Enable (re-renders when enabled or when shadow/pose parameters or tile resolution tier change); On Demand (re-renders on first use and after `InfinityAdditionalLightData.RequestShadowMapRendering()`, or when downgraded by contention). Resolution changes relocate cached tiles cleanly without sampling stale texels. Cached tiles hold static casters as of their last render.

## Shadowmask

A Mixed light in Shadowmask mode (Lighting Settings Mixed Lighting = Shadowmask, Quality Shadowmask Mode =
Shadowmask) renders realtime shadows from dynamic objects only and takes static occlusion from the shadowmask.
Distance Shadowmask renders every caster in realtime up to Shadow Distance and uses the shadowmask beyond it.

## Contact shadows

Enable **Contact Shadows** on the light and the **Contact Shadow** Volume component. The sun traces one long ray per
pixel; up to 24 local lights (largest on screen first) trace short rays through the clustered list. Deferred and
Forward surfaces use them; volumetrics do not. Quality presets set the step counts.

## Debugging

Rendering Debugger → Infinity Lighting: Cascade / Local / Cached Shadow Atlas, Shadow Moments, Contact Shadows views, local atlas occupancy, tile resolution tier counts, downgraded/dropped light counts, and cached shadow render/eviction/relocation counters. Fixtures: Window → Infinity → Create Shadows Fixture and Window → Infinity → Create Shape Shadows Fixture.
