# C1 implementation checkpoint — not accepted

C0 candidate 2 independently passed the baseline gate. C1 is implemented in part and remains open. C2–C7 are locked by the approved sequential plan. No Player build, packaging, branch, commit or push has been performed.

## Changes under validation

- GlobalSettings `Require()` only resolves/validates existing registered containers. Deleted the old Ensure/auto-create/resource-reload/save route and the factory's load-or-create/force-overrides helpers. Unity's explicit creation flow populates new settings; existing settings are not repaired during loading. The Asset declares native compatible-GlobalSettings requirements and its Ensure hook is read-only.
- `ValidateAndComplete` adds only missing types from a fresh default template, preserves existing component objects/values/flags, returns the addition count and supports Undo. Explicit changes notify the effective Volume cache.
- Replaced the invalid CustomEditor on the plain settings container with a real PropertyDrawer. It exposes New/Clone/Complete/confirmed Reset and native CoreRP component editors. `InfinityVolumeProfileEditor` uses VolumeComponentListEditor directly, because CoreRP VolumeProfileEditor automatically repairs global-default profiles on opening. Existing serialized `volumeProfile` remains the sole backing field; the CoreRP interface accesses that field without a schema alias/migration.
- Reset has current source/meta backup, effective-value/identity snapshot, complete-object Undo and cache notification. Persisted Reset/Undo/reopen acceptance remains pending. Backups must be removed after the named verification consumer; this is not a cleanup PASS.
- Pipeline construction validates required default/texture/material inputs before global mutations and releases constructed resources after failure. It stores its supplied Asset, installed quality profile, blit/BestFit resources and batching value; teardown compares installed references, not mutable configuration. Replacement graphics state is preserved; Infinity shared-resource retirement is separate from the SupportedRenderingFeatures sentinel. Actual lifecycle matrix remains pending.
- Stripper operates only in Infinity scope, preserves compatible Shader passes instead of conflating passName with LightMode, and retains referenced compute kernels. Removed unused-pass filtering, fake compute counters and one-off Player receipt writer. Preprocessor resource validation can be exercised by Editor tests. No build has been run.

## Evidence obtained

Task evidence root: `/Volumes/DataDisk/Projects/Unity/InfinityExample/intermediate/editor-convergence/`.

- `c1-tests-a`: 280 passed, zero failed/skipped/inconclusive. This predates later independent-review fixes and is not proof of the final C1 candidate.
- `c1-authoring/receipt.json`: explicit fresh GlobalSettings/resources creation; incomplete copied Profile adds exactly Exposure, preserves 13 existing components and their values/flags/GUID/local IDs; reopen, second-save byte identity and independent no-op pass. Original default profile and GlobalSettings bytes are unchanged. Unity-required transient Assets fixture directory was deleted in finally.
- Main inspected actual Graphics Settings > Pipeline Specific Settings > Infinity RP. The default Profile field, four action buttons and embedded components are visible. Film Grain's unoverridden parameters remain unoverridden. Earlier navigation overshot the tab strip and incorrectly suggested no Infinity area; that observation is corrected here. The native requirements hook is not claimed as the cause of tab visibility.
- Latest Editor recompile completed without compiler errors. Earlier test compilation mistakenly assigned read-only VolumeManager.globalDefaultProfile; corrected to SetGlobalDefaultProfile. Existing obsolete/Metal warnings remain assigned C2/C3/C6/C7, not hidden.
- Independent Astra code review found implicit CoreRP profile repair, unsafe partial global-state install, coupled graphics/resource ownership, mutable teardown comparisons and stale defaults after Reset. Main addressed these; a final source-bound C1 acceptance is still required.

## Current blocker and remaining gates

`c1-tests-b` did not discover/run tests: Unity requested saving modified Spazon before TestRunner could proceed. Main clicked Cancel, then requested test cancellation. Its receipt remains CancellationRequested with no XML, never PASS. No Save or Don't Save action was taken.

A read-only query identifies modified `Directional Light` (with a space), including GameObject/Transform/Light/InfinityAdditionalLightData; local IDs 1873545565/1873545568/1873545567/1873545566 in scene GUID 52096513fd20eba4fadae74e1bbe51db. The user has been asked whether these are their changes. The dirty scene is preserved; no dirty flags were cleared and no scene reload/discard was used. Live pipeline switches and full TestRunner execution must wait for that answer.

Remaining C1 gates: latest complete test run, persisted Reset/Undo/cache-refresh/reopen evidence, missing-slot and assignment UI cases, actual Infinity→Builtin→Infinity and quality/reload/failure lifecycle checks, Spazon regression against C0, fresh log/source+asset hash binding, final independent Astra verdict, and C1 intermediate-data cleanup after consumers finish. Generic CoreRP controls visible in the Infinity tab (e.g. Core RenderGraph/VRS) require consumer review under C3; their presence is not accepted as meaningful Infinity functionality.

One read-only-diagnostic attempt to invoke Unity's internal TryCreateNewGlobalSettingsContainer was rejected by automatic approval review because it might create/modify assets. It was not executed. Subsequent diagnostics inspected existing Inspector/cache/serialized-map objects only; no approval bypass or native-API compatibility shim was added.

## Additional bounded Reset verification

`c1-reset-a/receipt.json` now proves Reset on an isolated saved Profile, including current-byte backup, all 14 component identities, immediate effective slope 0.42→0.88→Undo 0.42→Redo 0.88, exact Undo value preservation, no implicit source save during Reset, explicit fixture save/reopen/second-save byte identity, original default asset unchanged, temporary Assets fixture removed, and the original dirty scene preserved. It did not run TestRunner or switch/save/reload Spazon. The backup remains in c1-reset-a/backup until independent C1 asset verification consumes it.

Current whole preimage asset comparison reports no changes to any preexisting Example/package asset bytes, including meta. No transient InfinityEditorConvergence Assets directory or sidecar remains. Task data is approximately 1.305 GB; required baseline/migration/test consumers remain active. Superseded C0 cleanup reclaimed 2,421,310 bytes with independent, hash-bound proof.

Installed Test Framework source confirms runSynchronously still invokes SaveModifiedSceneTask and excludes multi-frame UnityTests, so it cannot replace the blocked full-suite gate. No test lifecycle bypass, forced save, dirty-flag clearing or narrower PASS was used.
