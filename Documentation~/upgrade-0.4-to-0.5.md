# Upgrade 0.4 to 0.5

- Run **Window → Infinity → Append Missing Default Volume Components** once: it adds the new Volume types to the
  default profile without touching existing components.
- **Film tonemapper**: the mode formerly named Film is **ACES** (same value); only None and ACES exist.
- **Screen-space reflection** was rebuilt: NumRays, SpatialRadius, SpatialSample and the Bilateral parameters are gone. See [Volumes](volumes.md). Screen-space indirect diffuse no longer has a ray-reuse resolve. ResolveSamples and ResolveRadius on that component are obsolete and are not recreated on load.
- **Lights**: the analytic shape lives on `InfinityAdditionalLightData` (`FLightShape`) and is written to the native Light on edits and setters, not on load. Directional intensity is light color times intensity. Box, pyramid and disk native types render. Run the Render Pipeline Wizard to sync existing shapes.
- **Shadows**: cascade count, ratios, blend width, filter quality and local budgets moved to the RP Asset; per light
  shadow filter and update mode.
- **Reflection probes** are blended in the clustered list with a probe atlas; sphere influence comes from
  `InfinityAdditionalReflectionProbeData`.
- **Debug views** apply to Game and Scene view cameras only; reflection probe captures and previews ignore them.
- Asset create menus moved from Create → InfinityRenderPipeline to Create → Rendering → Infinity.
