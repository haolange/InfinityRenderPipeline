# Reflection probes

Unity's **Reflection Probe** component (Baked, Custom or Realtime) works unchanged. InfinityRP adds the probes to its
clustered light list and blends them per pixel in Deferred and Forward shading:

- **Priority**: probes are sorted by **Importance** (higher first), then by volume (smaller first). A pixel takes
  probes front to back until their weights reach 1; the remainder comes from the atmosphere sky.
- **Influence**: the probe box (with **Blend Distance** as a fade band) or, with *Infinity Additional Reflection Probe
  Data* → Influence = Sphere, a sphere of **Sphere Radius** (0 = the largest half extent of the box).
- **Box Projection** corrects the reflection against the probe box (sphere probes project on the sphere).
- Probes are axis aligned; rotation is not supported.

Probe cubes are GGX-filtered into one atlas whose face size and slot count are on the RP Asset (**Reflection Probe
Resolution**, **Max Reflection Probes**; 0 disables probes). A baked or custom cube is filtered once when it changes; a
realtime probe is filtered every frame it is visible. When more probes are visible than the atlas has slots, the lowest
priority ones are skipped.

Probe captures render with the RP Asset's reflection-probe Frame Settings (no SSR, SSGI, AO or contact shadows by
default) and store HDR radiance without exposure or tonemapping.

Verification state: see [FeatureStatus](../Docs/FeatureStatus.md).
