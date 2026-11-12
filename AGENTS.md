# InfinityRP contributor and agent specification

This document is normative for changes to `com.infinity.render-pipeline`. It holds the principles, design rules, conventions and work method. System contracts, the invariants this package's subsystems must keep, are normative in [ARCHITECTURE.md](ARCHITECTURE.md); its §16 lists the known limits. [Docs~/Project/Status/FeatureStatus.md](Docs~/Project/Status/FeatureStatus.md) records verification. [Docs~/Manual/index.md](Docs~/Manual/index.md) is the authoring manual. [Docs~/Record/History/](Docs~/Record/History) is immutable evidence. Each fact lives in exactly one of these documents.

## 1. Model and principles

A program is facts (quantities and orders it must know), representations of those facts (fields, buffers, keys, branches, keywords, strings, copies), readers and writers, and clocks (CPU frame, GPU queue, submission epoch, transaction generation). It is correct when every reader sees the right version of each fact at the moment it reads. Its cost is every representation times its evaluation rate times its unit cost, plus maintenance proportional to the number of representations and their interactions.

Correctness (every path, hot or cold):

- **P1 Single authority.** Each fact has one writer. Every other representation is derived, names its source and version, and is rebuilt or invalidated by that source, never written independently. A silent fallback (default value, dummy resource, `return false`, a dispatch of zero) is a non-owner forging the fact.
- **P2 References carry time.** A reference carries a version or generation and is rejected when stale.
- **P3 Atomic publication.** Prepare completely, publish once. Readers see the old or the new state, never half. A failure before publication rolls back completely.
- **P4 Lifetime is the last reader.** A representation lives until its last reader finishes on that reader's clock, and is reused after. Keeping it longer is a new ownership with its own name. A defensive copy means the last reader is unknown.

Structure:

- **P5 Dependencies point down.** A lower layer defines a generic slot; the upper layer adapts its concepts into it. A lower layer never names an upper concept.
- **P6 Semantics and optimization are separate.** What changes only cost, never results (occlusion, batching, caching, backend choice), stays out of the caller's semantic interface and is owned by the mechanism.

Economy (weighted by rate; cold paths may relax it):

- **P7 Rate matching.** Evaluate a fact at min(change rate, read rate). A representation whose read rate is zero costs only its existence: delete it. A reader is a current caller or an in-development consumer with a named owner; classify code with no caller by R15 before deleting it.

Knowledge:

- **P8 Evidence strength.** A claim is as strong as the cheapest check that could falsify it. A lower-level check does not close a higher-level claim: static checks do not prove runtime, compilation does not prove a frame. A silent fallback also destroys the evidence.

On conflict: P1–P4, then P5–P6, then P7, then local readability. P8 decides what may be claimed.

## 2. Design rules

| # | Rule | Test |
|---|---|---|
| R1 | Variation is data. The mechanism offers a slot (key, context, descriptor); callers fill their policy. | Deleting a parameter breaks only one kind of caller: it is a special-case knob and stays out of the shared API. |
| R2 | Parameters and results are different facts. | One type carries both inputs and result state. |
| R3 | A handle is created next to its consumer; its scope is its lifetime. | A handle crosses more than one parameter hop to reach its consumer. |
| R4 | One verb per lifecycle transition. | One object has Create, Request and Prepare. |
| R5 | Read an engine-owned fact once per change from its snapshot (`CullingResults`, `VisibleLight`, `TransformAccessArray`, camera setup); downstream reads plain data. | An engine getter inside a hot loop, or live getters mixed with a snapshot. |
| R6 | The container follows its reader: native for Burst or GPU readers, reused managed arrays for managed APIs. | A per-frame conversion between the two. |
| R7 | A property costs what a field costs. A lookup or computation is a method with a verb. | A property that calls `GetComponent`, a dictionary or a computation. |
| R8 | The owner of an invariant raises its failure once, where the invariant is established. Downstream neither rechecks nor substitutes. | One condition checked twice; `?.` or `TryGet` that silently continues. |
| R9 | A replaced path is deleted in the same change that moves its last reader. No commit holds old and new side by side; no shim, transitional overload, `[Obsolete]` or self-registering bridge. | Two paths for one responsibility are reachable. |
| R10 | A pile of special cases means the representation is wrong. Change the representation before adding a patch. | A third flag or branch of the same kind. |
| R11 | Mirror a platform abstraction that is right (renderer lists, BatchRendererGroup); deviate only for a named reason. | A parallel concept beside a platform one. |
| R12 | Diagnostics are observers installed at construction, outside the success path and the measured scope, produced at failure or request rate. | Validation or capture calls in the core loop; a query that writes debug state. |
| R13 | Code duplicated per kind becomes one generic or one core. | Two or more method bodies identical line for line. |
| R14 | Setup-time facts (kernel index, property ID, keyword, sampler, pass index) are resolved once into a table when resources change. | A string lookup in record or execute. |
| R15 | Code without callers is classified, not deleted mechanically: in-development feature (keep, no cost on other hot paths), duplicate of a dependency (delete), remnant of a replaced design (delete), meaningful but unwired (wire it), unused parameter or write-only field in live code (delete), runtime code read only by tests (test the real path, delete the shadow), diagnostics (keep under R16), unowned vendored code (delete). | — |
| R16 | Diagnostics and validation exist only in Editor and Development builds; a release build has no branch to them. | A validation, capture or debug symbol reachable in a release build. |

## 3. Conventions

Conventions are uniform choices, not principles; static checks enforce them.

| Kind | Convention | Examples |
|---|---|---|
| Namespace | `InfinityTech.<Domain>[.<Sub>][.Editor]` | `InfinityTech.Rendering.MeshPipeline` |
| Enum | `E` prefix | `EPassType`, `EMeshDrawBackendPolicy` |
| GPU record | Same name in C# and HLSL, tagged `// GPU-RECORD: <HlslStruct> <hlsl file>` | `LightRecord` |
| Handle | `Handle` suffix | `TextureHandle`, `RGMeshDrawListHandle` |
| Render-graph type | `RG` prefix | `RGBuilder`, `RGRasterPassHandle` |
| Job | `*Job` suffix | `MeshInstanceCullingJob` |
| Private instance field | `m_` + PascalCase | `m_RGBuilder` |
| Static field | `s_` prefix; IDs, keywords and samplers are `static readonly` | `s_Shader` |
| Shader property IDs | `ID_` or a name on `InfinityShaderIDs`, grouped by HLSL include | `TransformBuffer` |
| Shader bindings | `SRV_` / `UAV_` / `CBV_` | `SRV_DepthTexture` |

- A name says what a type holds or does. Do not borrow a type prefix from another engine. On a collision choose a more descriptive name; never add a prefix or a number. No generation version in a name, file or document title. `Version` and `Revision` name a data snapshot.
- A variable keeps its type's head noun and may drop the domain prefix: `MeshCullingContext cullingContext`, `TextureDescriptor gbufferDescriptor`. No abbreviations. Time words are `previous` and `current`.
- Acronyms stay uppercase in type and method names; a camelCase identifier that starts with an acronym lowercases the whole acronym. A verb is not repeated in one name; an ordinal does not stand in for meaning. `Draw` alone is a verb, never a noun prefix.
- Correct a misspelled symbol when a change touches it. A misspelled serialized field is corrected only inside an approved migration (§5). Do not add a divergent spelling.
- Extend `InfinityRenderPipeline` only through partial files under `Runtime/Pipeline/Pass/<domain>/`. A pass file holds an `XxxPassData` struct, an optional `XxxPassUtilityData`, and the pipeline method. Volume components live in `Runtime/Volume/<domain>/`. One primary type per file unless a pass or feature group is clearer together.
- Layout follows [Docs~/Project/Style](Docs~/Project/Style): Page for sections, Type for declarations, Flow for method bodies, Data for fields and records, Editor and Shaders where they apply. A `#region` marks a long C# section, its body indented once, its name repeated on `#endregion`.
- Assembly names are `Unity.RenderPipelines.Infinity.*`. Components prefer `[ExecuteAlways]` and `AddComponentMenu("InfinityRenderer/...")`. Asset fields are `[SerializeField] private`.
- Shader names start with `InfinityPipeline/`; the tag is `RenderPipeline=InfinityRenderPipeline`; compute files are `Compute_<Feature>.compute`. Infinity LightModes and their pass names are `RasterXXXPass` and match `InfinityPassIDs`; Unity `ShadowCaster`, `Meta` and `SRPDefaultUnlit` stay. GPU work types are Raster (draw), Compute (dispatch), Copy (transfer), Upload (CPU upload). Pass names come from the pass `ProfilingSampler`.
- Comments are English. A TODO is `// TODO: <action>`. No large blocks of commented-out code.

## 4. Work method

Start from the behavior the user wants and why the current code cannot produce it. Every planned item traces to the request, an existing contract or an observed defect, and every load-bearing decision to code, documents, logs, tests or an explicit user instruction. When a plan collects flags, guards or patches, find their shared cause first (R10). Size the change from evidence: a structural change with a demonstrated cause is in scope, an unrelated refactor is not. Every branch, state, option, retry and fallback needs a current reader with a named owner (P7).

Decide which conditions are preconditions, which failures are raised and who handles them (R8). A layer that cannot handle a failure passes it to the owner its contract names. A workaround is marked `// TODO: <root cause>` and is not the final design; ownership, order and lifetime come before an API migration.

Deliver the requested scope in the same interaction: decide, change, verify and leave a connected result. Do not shrink it to a sample or stop at a plan. Stop and ask only when continuing could lose data, break a serialized schema or another external contract, force an irreversible change, move a permission boundary, or when no safe default exists. For other ambiguity, state the default, the reason, the risk and the rollback, and finish the reversible work.

Verification: a check must be able to fail for a meaningful error. Cover a mechanism with representative cases. The implementer owns its checks; an independent verifier reviews a batch and returns it on failure, which locks dependent batches. Without an Editor, `Tools/StaticCheck/` plus independent review is the static gate, consumer closure is a repository-wide search including `Editor/Cli`, docs and tests, and runtime and image results stay `TODO(UNVERIFIED)` until an Editor run confirms them.

Living documents: after a change, update `AGENTS.md` and `ARCHITECTURE.md`: add what became true, delete what is no longer true, rewrite what changed. No iteration log. A fact appears in one document only. `AGENTS.md` stays the shortest. `DESIGN.md` is absent on purpose: this package has no visual design system.

Intermediate data lives in `<Unity project root>/intermediate/<task-id>/<run-id>/` (the Example project's `intermediate/`, git-ignored), never in the package root, `/tmp` or the home directory. Before a large capture, record the path, owner and estimated size and check free space. After a task, stop producers, drain readbacks, keep the required summary and delete the task's paths; data stays only for a named next consumer. Delete by recorded task path only. Never delete user source, baked data, `Library`, engine caches, logs or another task's files. The delivery note states the removed paths, the reclaimed space and any blocker.

Track active work in the conversation task list, not in a repository ledger. Create a git branch only when asked or when the session prescribes one. `Docs~/Record/History` does not authorize work.

## 5. Repository safety

- Editor load and script reload never migrate, rebuild, delete or save user assets. Preflight never activates source scenes or instantiates prefabs.
- A serialized schema change needs approval and a written intent, a fresh durable backup, an exact allowed delta, stable GUIDs and local IDs, a second save that is byte-identical, and a separate no-op. Native legacy fields and current effective values are different evidence; prove every non-approved field unchanged. Renaming a serialized field resets user values because `FormerlySerializedAs` is not used; prefer adding a field, and list every unavoidable rename in that change's verification notes. Schema, compilation and image checks are separate gates.
- Player builds and packaging require explicit user authorization.
- Automated Editor interaction uses the Unity CLI (`Editor/Cli`, compiled with `INFINITY_UNITY_CLI`) against an explicitly selected project on `127.0.0.1` with bearer authentication. Commands take explicit arguments, return status and evidence paths and never save assets. Never print or archive credentials.
- Never run `unity run`, a batch build or a second Editor against a project an Editor holds. Confirm the project and process ID first; when the source project is occupied, use the authorized independent task mirror and its `Library`. A timeout is not permission to resubmit, release resources or restart.
- Scene views are created, closed, docked and restored on the Editor update pump, never inside their own render or `OnGUI` stack; the render callback may advance a bounded motion pose only. Preserve tab ownership and position; setting `EditorWindow.position` can undock a view.
- Capture uses the graph transfer capture of an explicitly selected active Game camera, or the single one; never `com.unity.pipeline` `screenshot`, which replaces `targetTexture` and calls `Camera.Render`.
- After a code or shader change: confirm the owned Editor, refresh with the Unity CLI or `Tools/RefreshUnityEditor.sh`/`.ps1`, wait for compilation and domain reload, confirm the candidate DLL loaded, read only the new window of `Application.consoleLogPath`, fix the first real owner, order, lifetime or shader-contract defect, and capture with `Tools/CaptureUnityWindow.sh`/`.ps1`. A change to clears, depth, attachments, pass order or present is unverified until the image is inspected. UI inspection uses computer use on the verified owned window. Repeat until the new log window has no InfinityRP flood and the frame is correct; log-only results stay `TODO(UNVERIFIED)`. Identical screenshots of a Game view that is not redrawing are not liveness evidence: use Play mode or a differing pair.
- EditMode tests live under `Tests/Editor`.
