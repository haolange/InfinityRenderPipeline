# Upgrade 0.4 to 0.5

- Run **Window → Infinity → Append Missing Default Volume Components** once: it adds the new Volume types to the
  default profile without touching existing components.
- **Film tonemapper**: the mode formerly named Film is **ACES** (same value); only None and ACES exist.
- **Screen Space Reflection** was rebuilt: NumRays, SpatialRadius, SpatialSample and the Bilateral* parameters are gone;
  see [Volumes](volumes.md) for the new parameters and quality levels. SSGI follows the same design.
- **Lights**: the shape is authored on `InfinityAdditionalLightData` in Unreal form and synced to the native Light on
  edits. Directional intensity now uses only the light color × intensity (exposure may change). Box, pyramid and disc
  native types render instead of failing. Run the Render Pipeline Wizard to sync existing light shapes.
- **Shadows**: cascade count, ratios, blend width, filter quality and local budgets moved to the RP Asset; per light
  shadow filter and update mode.
- **Reflection probes** are blended in the clustered list with a probe atlas; sphere influence comes from
  `InfinityAdditionalReflectionProbeData`.
- **Debug views** apply to Game and Scene view cameras only; reflection probe captures and previews ignore them.
- Asset create menus moved from Create → InfinityRenderPipeline to Create → Rendering → Infinity.
