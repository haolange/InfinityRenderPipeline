# InfinityRP 0.3.0 upgrade

## Current execution state

N01 is in progress. Version/assembly declarations and seven binary assets / 22 serialized class identifiers have been migrated; the existing Unity Editor compiled/reloaded Infinity assemblies and the targeted source/no-op checks completed. Final live all-script type verification and independent N01 acceptance remain pending; Player has not yet been built or run. The execution status is owned by `PLAN.md`.

## Version contract

The supported editor is Unity `6000.5.3f1`. The package manifest declares `unity: 6000.5` and `unityRelease: 3f1`, package version `0.3.0`, Core RP / Shader Graph / Visual Effect Graph `17.5.0`. Dependencies are aligned with the Example project's already resolved versions: Burst `1.8.29`, Collections `6.5.0`, Mathematics `1.4.0`, Terrain Tools `5.3.2`, Addressables `2.9.1`, Test Framework `1.7.0`, and Jobs `0.70.0-preview.7`.

## Assembly contract

Runtime and Editor assembly identities are `Unity.RenderPipelines.Infinity.Runtime` and `Unity.RenderPipelines.Infinity.Editor`; the existing shader assembly definition name is aligned consistently (it contains no C# scripts). The test assembly remains `Unity.RenderPipelines.Infinity.Tests`. Assembly references and friend declarations use the resulting names; version definitions refer to the actual package ID `com.infinity.render-pipeline`.

This changes assembly-qualified type identities. It does not change script GUIDs. There is no old-assembly shim or compatibility attribute. A project carrying assembly-qualified managed references must complete an explicit, validated migration before using the new assembly identities; unresolved assets are blockers, not silently omitted objects.

## Asset preservation and migration gate

The source package and Example have separate repositories. Preserve their current working-tree bytes, including prior uncommitted changes. Do not restore the repositories or bulk-reserialize them.

The temporary T02 preflight is explicitly invoked in the running editor through `Infinity > Validation > Migration > Run Assembly Preflight`. It snapshots source bytes and metadata before importing any isolated copies. Only a uniquely owned staging directory is cleaned afterward. All evidence remains outside the Unity project.

The preflight first parses the source assets and records object identities, local file IDs, MonoScript references, class identifiers, managed-reference type names and known identity mappings. It then compares each native copy against that source baseline before and after canonical serialization and reimport, and requires byte-idempotence on a second save. YAML and binary assets use the same parsed metadata rules. Imported assets are classified and inspected for relevant importer or generated managed-object metadata; affected inputs that cannot use a proven native migration route fail explicitly. No assembly identity is edited during this gate. Reconnection to the new assembly is proven only by the separate gate after the actual rename.

The current revision starts with a small smoke run for the exact tool source hash. Only a matching smoke pass unlocks a full run through the same menu. Work is scheduled in short editor-update batches; progress and a cancellation-file path are written immediately. The progress bar or the run’s `cancel.request` file cancels the operation, closes snapshot writers, checks captured sources and removes only the owned staging directory. Source scenes are not opened, prefabs are not instantiated, and changes to the existing scene setup or dirty state stop the run. Native source backups and canonical copies are read offline by the matching Editor’s `Contents/Helpers/binary2text`, with explicit output paths. The reader supports both the current binary format and Unity YAML without activating scene objects. Normal decimal output owns IDs/references; a second `-hexfloat` capture from the same unchanged input preserves exact float bits, because that option also changes later integer formatting. Raw captures, object-ID indices and structural summaries are retained. Standalone reader smoke succeeded; unchanged completed source/copy evidence is reused through a current-input composite, with verified native source replacements and exact invalid-VFX retirements. New migration results are recorded in PLAN.md.

No preflight result proves the final migration. The final gate must verify target runtime/editor types, script GUID and local-file identities, zero missing scripts, unchanged semantic content apart from approved identities/configuration, and unchanged bytes on a second migration. Save only individually verified affected assets. Temporary old-name migration code is removed after closure.

## Explicit configuration

Editor loading, validation and pipeline creation no longer populate compute shader references. Select the intended RP asset and use `Infinity > Validation > Configure Selected Pipeline Compute Shaders` when preparing its configuration. The operation backs up the exact asset/meta bytes, validates a complete candidate first and saves only the selected asset. It fills missing references; existing shader references are preserved and invalid assigned shaders fail validation. An asset with unsaved edits is rejected so those edits are not accidentally saved by configuration.

The asset's Volume and Atmosphere profiles remain required. No runtime default profile or shader substitution is introduced.

## Player build and platform evidence

`Infinity > Validation > Build macOS Player` builds the reference Spazon scene in the current editor through `BuildPipeline.BuildPlayer`. It passes the scene list directly rather than changing `EditorBuildSettings`, creates a unique directory under the operating system temporary directory, and writes start/build reports with Unity version, graphics APIs, scene list, warnings, errors and build messages. A successful build is distinct from successful Player launch, render correctness and performance acceptance.

macOS/Metal Editor and Standalone results remain unverified until the real outputs have been inspected. Windows/D3D12, Windows/Vulkan, Linux/Vulkan and HDR require their separate real-machine gates. Do not infer any platform result from compilation on another platform.
