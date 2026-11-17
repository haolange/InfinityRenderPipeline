# InfinityRP contributor and agent specification

This document is normative for `com.infinity.render-pipeline`: principles, rules, conventions and work method. System contracts are normative in [ARCHITECTURE.md](ARCHITECTURE.md) (§16: known limits). [FeatureStatus](Docs~/Project/Status/FeatureStatus.md) records verification, the [Manual](Docs~/Manual/index.md) covers authoring, [History](Docs~/Record/History) is immutable evidence. Each fact lives in exactly one of them.

## 1. Model and principles

A program is facts (quantities and orders it must know), their representations (fields, buffers, keys, branches, keywords, copies), readers and writers, and clocks (CPU frame, GPU queue, submission epoch, transaction generation). It is correct when every reader sees the right version of each fact when it reads. Its cost is each representation times its evaluation rate times its unit cost, plus maintenance that grows with representations.

Correctness (every path, hot or cold):

- **P1 Single authority.** Each fact has one writer. Every other representation is derived, names its source and version, and is rebuilt or invalidated by that source, never written independently. A silent fallback (default value, dummy resource, `return false`, a dispatch of zero) is a non-owner forging the fact.
- **P2 References carry time.** A reference carries a version or generation and is rejected when stale.
- **P3 Atomic publication.** Prepare completely, publish once. Readers see the old or the new state, never half. A failure before publication rolls back completely.
- **P4 Lifetime is the last reader.** A representation lives until its last reader finishes on that reader's clock, and is reused after. Keeping it longer is a new ownership with its own name. A defensive copy means the last reader is unknown.

Structure:

- **P5 Dependencies point down.** A lower layer defines a generic slot; the upper layer adapts its concepts into it. A lower layer never names an upper concept.
- **P6 Semantics and optimization are separate.** What changes only cost, never results (occlusion, batching, caching, backend choice), stays out of the caller's semantic interface and is owned by the mechanism.

Economy (weighted by rate; cold paths may relax it):

- **P7 Rate matching.** Evaluate a fact at min(change rate, read rate). A reader is a current caller, an in-development consumer with a named owner, or a shared skeleton contract (one module lifecycle for every scene type). Uncalled code is classified by R15, never deleted for that alone.

Knowledge:

- **P8 Evidence strength.** A claim is as strong as the cheapest check that could falsify it. A lower-level check does not close a higher-level claim: static checks do not prove runtime. A silent fallback also destroys the evidence.

On conflict: P1–P4, then P5–P6, then P7, then local readability. P8 decides what may be claimed.

## 2. Design rules

| # | Rule | Test |
|---|---|---|
| R1 | Variation is data. The mechanism offers a slot (key, context, descriptor); callers fill their policy. | Deleting a parameter breaks only one kind of caller: it is a special-case knob and stays out of the shared API. |
| R2 | Parameters and results are different facts. | One type carries both inputs and result state. |
| R3 | A handle is created next to its consumer; its scope is its lifetime. | A handle crosses more than one parameter hop to reach its consumer. |
| R4 | One verb per lifecycle transition. | One object has Create, Request and Prepare. |
| R5 | Read an engine-owned fact once from its snapshot (`CullingResults`, `VisibleLight`, `TransformAccessArray`, camera setup); downstream reads plain data. | An engine getter inside a hot loop, or live getters mixed with a snapshot. |
| R6 | The container follows its reader: native for Burst or GPU readers, reused managed arrays for managed APIs. | A per-frame conversion between the two. |
| R7 | A property costs what a field costs. A lookup or computation is a method with a verb. | A property that calls `GetComponent`, a dictionary or a computation. |
| R8 | The owner of an invariant raises its failure once, where the invariant is established. Downstream neither rechecks nor substitutes. | One condition checked twice; `?.` or `TryGet` that silently continues. |
| R9 | A replaced path is deleted in the same change that moves its last reader. No commit holds old and new side by side; no shim, transitional overload, `[Obsolete]` or self-registering bridge. | Two paths for one responsibility are reachable. |
| R10 | A pile of special cases means the representation is wrong. Change the representation before adding a patch. | A third flag or branch of the same kind. |
| R11 | Mirror a platform abstraction that is right (renderer lists, BatchRendererGroup); deviate only for a named reason. | A parallel concept beside a platform one. |
| R12 | Diagnostics are observers installed at construction, outside the success path and the measured scope, produced at failure or request rate. | Validation or capture calls in the core loop; a query that writes debug state. |
| R13 | Code duplicated per kind becomes one generic or one core. | Two or more method bodies identical line for line. |
| R14 | Setup-time facts (kernel index, property ID, keyword, sampler, pass index) are resolved once into a table when resources change. | A string lookup in record or execute. |
| R15 | Classify code without callers before acting: an in-development feature, a domain type's own query or a mechanism's API completeness: keep, off other hot paths; a remnant of a replaced design, a duplicate of a dependency or unowned vendored code: delete (R9); a write-only field or meaningful unwired code: wire it; read only by tests: test the real path; diagnostics: R16. | Code deleted only because nothing calls it today. |
| R16 | Diagnostics and validation exist only in Editor and Development builds; a release build has no branch to them. | A validation, capture or debug symbol reachable in a release build. |
| R17 | Scratch lives as long as its last reader (P4) and never touches the managed heap at evaluation rate. Use the shortest class covering the last reader: none (write the target); `stackalloc` or a fixed list when small and bounded; `Allocator.Temp`; `TempJob` across scheduled jobs; the graph arena across record and execute; across frames, a persistent container on a named owner, cleared by count, its high water bounded by a named quantity. A managed API that needs a managed array gets one array owned by its caller (R6). | At evaluation rate: a managed `new`, a capturing lambda, boxing, string building or `ArrayPool.Shared`; a persistent scratch field whose last reader is in the same call. |
| R18 | Order is derived. A dependency is a parameter or a declared read; a call means the same whatever ran before it. | A registry where the latest entry wins, a second identical call that changes meaning, or a key whose referent changes with record order. |

## 3. Conventions

Conventions are uniform choices, not principles; static checks enforce them.

| Kind | Convention | Examples |
|---|---|---|
| Namespace | `InfinityTech.<Domain>[.<Sub>][.Editor]` | `InfinityTech.Rendering.MeshPipeline` |
| Enum | `E` prefix | `EPassType`, `EMeshDrawBackendPolicy` |
| GPU record | Same name in C# and HLSL (a pass cbuffer adds `CBV_`), tagged `// GPU-RECORD: <HlslStruct> <hlsl file>` | `LightRecord` |
| Handle | `Handle` suffix | `TextureHandle`, `RGMeshDrawListHandle` |
| Render-graph type | `RG` prefix | `RGBuilder`, `RGRasterPassHandle` |
| Job | `*Job` suffix | `MeshDrawGatherJob` |
| Private instance field | `m_` + PascalCase | `m_RGBuilder` |
| Static field | `s_` prefix; IDs, keywords and samplers are `static readonly` | `s_NameID` |
| Shader property IDs | One ID per property name; shared names on a `*ShaderIDs` holder | `PassShaderIDs.SRV_DepthTexture` |
| Shader bindings | `SRV_` / `UAV_` / `CBV_` | `SRV_DepthTexture` |

- A name says what a type holds or does. Do not borrow a type prefix from another engine. On a collision choose a more descriptive name; never add a prefix or a number. No generation version in a name, file or document title. `Version` and `Revision` name a data snapshot.
- A variable keeps its type's head noun and may drop the domain prefix (`MeshCullingContext cullingContext`). A Handle type keeps the suffix (`RGTextureHandle depthTextureHandle`). No abbreviations. Time words are `previous` and `current`.
- Acronyms stay uppercase in type and method names; a camelCase identifier that starts with an acronym lowercases the whole acronym. A verb is not repeated in one name; an ordinal does not stand in for meaning. `Draw` alone is a verb, never a noun prefix.
- Correct a misspelled symbol when a change touches it. A misspelled serialized field is corrected only inside an approved migration (§5). Do not add a divergent spelling or an old-name alias.
- Extend `InfinityRenderPipeline` only through partial files under `Runtime/Pipeline/Pass/<domain>/`. A pass file holds an `XxxPassData` struct, an optional `XxxPassUtilityData`, and the pipeline method. Volume components live in `Runtime/Volume/<domain>/`. One primary type per file unless a pass or feature group is clearer together.
- Layout follows [Docs~/Project/Style](Docs~/Project/Style) (Page, Type, Flow, Data, Editor, Shaders).
- Assemblies are `Unity.RenderPipelines.Infinity.*`. Components use `[ExecuteAlways]`, `AddComponentMenu("InfinityRenderer/...")`. Asset fields are `[SerializeField] private`, names kept (§5).
- Shader names start with `InfinityPipeline/`; the tag is `RenderPipeline=InfinityRenderPipeline`; compute files are `Compute_<Feature>.compute`. Infinity LightModes and their pass names are `RasterXXXPass` and match `InfinityPassIDs`; other owned raster shader names use the Raster prefix; Unity `ShadowCaster`, `Meta` and `SRPDefaultUnlit` stay. GPU work types are Raster (draw), Compute (dispatch), Copy (transfer), Upload (CPU upload). Pass names come from the pass `ProfilingSampler`.
- Comments are English. A TODO is `// TODO: <action>`. No large blocks of commented-out code.

## 4. Work method

Start from the behavior the user wants and why the code cannot produce it. Every planned item traces to the request, a contract or an observed defect; every load-bearing decision to code, documents, logs, tests or an explicit instruction. Piled-up flags, guards or patches share a cause (R10). A structural change with a demonstrated cause is in scope; an unrelated refactor is not. Each branch, state, option, retry and fallback needs a reader (P7).

Decide which conditions are preconditions, which failures are raised and who handles them (R8). A layer that cannot handle a failure passes it to the owner its contract names. A workaround is marked `// TODO: <root cause>` and not the final design; ownership, order and lifetime precede an API migration.

Deliver the requested scope in the same interaction: decide, change, verify and leave a connected result, not a sample or a plan. Stop and ask only when continuing could lose data, break a serialized schema or another external contract, force an irreversible change, move a permission boundary, or no safe default exists; otherwise state the default, reason, risk and rollback and finish the reversible work.

Verification: a check must be able to fail for a meaningful error; cover a mechanism with representative cases. The implementer owns its checks; an independent verifier reviews a batch and returns it on failure, which locks dependent batches. Without an Editor, `Tools/StaticCheck/` plus independent review is the static gate, consumer closure is a repository-wide search including `Editor/Cli`, docs and tests, and runtime and image results stay `TODO(UNVERIFIED)`.

Living documents: after a change, update `AGENTS.md` and `ARCHITECTURE.md`: add what became true, delete what is no longer true, rewrite what changed. No iteration log; a fact appears in one document only; `AGENTS.md` stays the shortest. `DESIGN.md` is absent on purpose (no visual design system).

Intermediate data lives in `<Unity project root>/intermediate/<task-id>/<run-id>/` (git-ignored), never in the package root, `/tmp` or home, and is never committed. Bound large captures and check free space first. After a task, stop producers, drain readbacks and delete that task's paths; keep failure evidence while diagnosis needs it. Delete by recorded path only, never a shared `intermediate/`, user source, baked data, `Library`, caches, logs or another task's files; moving is not cleanup. The delivery note states removed paths, reclaimed space and blockers.

Track active work in the conversation task list, not a repository ledger. Branch only when asked or prescribed; History authorizes no work.

## 5. Repository safety

- Editor load and script reload never migrate, rebuild, delete or save user assets. Preflight never activates source scenes or instantiates prefabs.
- A serialized schema change needs approval, a written intent, a fresh backup, an exact allowed delta, stable GUIDs and local IDs, a byte-identical second save and a separate no-op; prove every non-approved field unchanged. Renaming a serialized field resets user values (`FormerlySerializedAs` is not used): prefer adding a field and list every unavoidable rename in the verification notes. A replaced serialized type is deleted after consumer closure and an explicit Example-project migration. Schema, compilation and image checks are separate gates.
- Player builds and packaging require explicit user authorization.
- Automated Editor interaction uses the Unity CLI (`Editor/Cli`, `INFINITY_UNITY_CLI`) against an explicitly selected project on `127.0.0.1` with bearer authentication; commands take explicit arguments, return status and evidence paths and never save assets. Never print or archive credentials.
- Never run `unity run`, a batch build or a second Editor against a project an Editor holds; confirm the project and process ID first, and use the authorized task mirror when the source project is occupied. A timeout is not permission to resubmit, release resources or restart.
- Scene views are created, closed, docked and restored on the Editor update pump, never inside their own render or `OnGUI` stack; the render callback may only advance a bounded motion pose. Preserve tab ownership and position (`EditorWindow.position` can undock a view).
- Capture uses the graph transfer capture of an explicitly selected active Game camera (or the single one, never by MainCamera tag) and observes the real window; never `com.unity.pipeline` `screenshot`, which replaces `targetTexture`. Long-running operations expose status and cancellation.
- After a code or shader change: confirm the owned Editor, refresh with the Unity CLI or `Tools/RefreshUnityEditor.sh`/`.ps1`, wait for compilation and domain reload, confirm the candidate DLL loaded, read only the new window of `Application.consoleLogPath`, fix the first real owner, order, lifetime or shader-contract defect, and capture with `Tools/CaptureUnityWindow.sh`/`.ps1`. A change to clears, depth, attachments, pass order or present is unverified until the image is inspected. Repeat until the new log window has no InfinityRP flood and the frame is correct; log-only results stay `TODO(UNVERIFIED)`.
