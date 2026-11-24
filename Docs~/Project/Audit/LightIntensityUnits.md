# Light intensity unit proposal

InfinityRP currently packs Unity's `VisibleLight.finalColor` as the source of realtime radiance. The IES importer writes punctual intensity in candela, and the lightmapper extracts Unity light values before applying Infinity's emitter dimensions. A unit selector must preserve all three paths for existing lights. [Light record](../../../Runtime/Lighting/LightElement.cs) · [IES import](../../../Editor/Lighting/Integration/InfinityIESImporter.cs) · [Lightmapper](../../../Runtime/Pipeline/Integration/InfinityLightmapperDelegate.cs)

Expose physical units as editor representations of the existing Unity intensity value. Resolve conversions from the effective light shape and a positive reference distance for punctual lux. Prefer the public Core RP conversion functions wherever their inputs match Infinity's shape semantics. Verify Unity's `Light.lightUnit` and `luxAtDistance` behavior before persisting a unit choice through those properties.

| Emitter | First unit candidates | Conversion input and case to prove |
|---|---|---|
| Directional | lx | Keep the existing irradiance scale. Compare a fixed surface at two receiver distances. |
| Point | cd, lm, lx at distance | Use the full-sphere solid angle for a point. Verify inverse-square falloff and an IES-imported control. |
| Spot cone and pyramid | cd, lm, lx at distance | Use the corresponding solid angle and verify reflector behavior. Cover two outer angles and two pyramid aspect ratios. |
| Spot box | lx | Check the parallel projection and the lightmapper's box path separately. |
| Rect | nit, lm | Use the authored width and height and verify the emitted flux for two areas. |
| Disk | nit, lm | Use the authored disk radius. The native Rectangle carrier would supply the wrong emitter area to a type-based converter. |

Use fresh, unsaved fixtures and an existing-scene control. For each eligible unit pair, compare `VisibleLight.finalColor`, the packed light record, samples from an untonemapped fixed-camera render, and extracted or baked lighting as applicable. A display-only unit change must preserve the authored intensity. For numeric edits, check unit round trips and expected changes in radiance. Include mixed selections, Undo/Redo, Prefab overrides, and zero-area emitters. Keep EV100 and elongated point sources outside the first unit selector until their conversion semantics are verified.
