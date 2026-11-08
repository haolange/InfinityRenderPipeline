# Upgrade 0.4 to 0.5

- Run **Window → Infinity → Append Missing Default Volume Components** once: it adds the new Volume types to the
  default profile without touching existing components.
- **Film tonemapper**: the mode formerly named Film is **ACES** (same value); only None and ACES exist.
- **Screen-space reflection** was rebuilt: NumRays, SpatialRadius, SpatialSample and the Bilateral parameters are gone. See [Volumes](volumes.md). Screen-space indirect diffuse no longer has a ray-reuse resolve. ResolveSamples and ResolveRadius on that component are obsolete and are not recreated on load.
- **Lights**: the analytic shape lives on `InfinityAdditionalLightData` (`LightSourceShape`) and is written to the native Light on edits and setters, not on load. Directional intensity is light color times intensity. Box, pyramid and disk native types render. Run the Render Pipeline Wizard to sync existing shapes.
- **Shadows**: cascade count, ratios, blend width, filter quality and local budgets moved to the RP Asset; per light
  shadow filter and update mode.
- **Reflection probes** are blended in the clustered list with a probe atlas; sphere influence comes from
  `InfinityAdditionalReflectionProbeData`.
- **Debug views** apply to Game and Scene view cameras only; reflection probe captures and previews ignore them.
- Asset create menus moved from Create → InfinityRenderPipeline to Create → Rendering → Infinity.
- **MeshComponent** draws a `MeshAsset` (a mesh per LOD level, screen sizes, material slots and fade width) instead of a
  Mesh, and its mobility field is spelled correctly. Loading never converts the old fields: until you migrate, a 0.4
  MeshComponent draws nothing and a dynamic one reads as static. Back up the project, save and open every scene that
  has MeshComponents (the tool refuses while one is closed), then run **Window → Infinity → Migrate MeshComponents to
  MeshAsset (0.5)**. It assigns each component
  the shared one-level MeshAsset of its old Mesh (created under `Assets/InfinityMeshAssets/`), copies the mobility,
  clears the old Mesh reference, rebuilds MeshAssets made from a prefab, re-saves every MeshAsset, saves the touched
  scenes and prefabs and prints a report. The tool and the old fields exist only at commit `6703ab8`; the
  next 0.5 commits remove them. Check out that commit, migrate, save, then move to the current version. Opening a 0.4
  project directly on a later version drops the old fields, and its MeshComponents draw nothing. Components keep their own materials; a submesh draws
  the material at its slot.
- **MeshComponent scripts**: `MeshAsset` is a `MeshAsset` instead of a `Mesh`, `EStateType` is `EMeshMobility`, and
  `NotifyInstanceChanged` is `NotifyEntityChanged`. `NotifyGeometryChanged(Mesh)` re-reads every MeshAsset that draws
  the mesh.
