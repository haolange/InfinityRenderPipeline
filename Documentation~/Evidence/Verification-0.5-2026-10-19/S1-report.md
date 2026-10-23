> Delivery correction (2026-10-23): this report and its evidence were previously present locally but ignored by Git. This S17 delivery adds the report and the explicitly indexed [EarlyStages evidence archive](EarlyStages-closure/manifest.json). Earlier failures and acceptance boundaries below remain historical facts.

# Staged Editor checkpoint — 2026-10-20

The overall S0–S17 task is still in progress. Historical S1 mandatory compile/test/behavior/image evidence has
received independent review. Local main6b57678 now carries the uncommitted FixS1 candidate; its script compilation
succeeds, but main integration exposed later-stage blockers listed below. Independent verification accepts the exact S1 source-control checkpoint and permits S2 historical replay after
its FixS1 commit; the cumulative main failures still block final delivery. Fix S1 committed as `88933dcfd068fa739737b350da4c18572a1f31f6`; PR branch remote SHA verified equal; origin/main remains c92be77.
Original Example and package paths are unchanged. FeatureStatus and Unverified markers remain unchanged;
PR #11 stays Draft. The sections below retain historical failures as failures.

| Stage | Frozen endpoint | Verdict at S1 checkpoint |
|---|---|---|
| S0 | `d2ca231b1726` | Bounded offline PASS; Fix S0 6b57678 |
| S1 | `227609417100` | Historical gates accepted; Fix S1 `88933dc`; main integration FAIL retained |
| S2 | `aaf975126a01` | Locked; not replayed in Editor |
| S3 | `80327044caee` | Locked; not replayed in Editor |
| S4 | `f70837766999` | Locked; not replayed in Editor |
| S5 | `b8a582d287fa` | Locked; not replayed in Editor |
| S6 | `834f41de69b2` | Locked; not replayed in Editor |
| S7 | `e6155f32fbb6` | Locked; not replayed in Editor |
| S8 | `7bbf86db97b5` | Locked; not replayed in Editor |
| S9 | `939361c7b185` | Locked; not replayed in Editor |
| S10 | `02c1dff784b5` | Locked; not replayed in Editor |
| S11 | `6e0f3717d95f` | Locked; not replayed in Editor |
| S12 | `ccc04407a348` | Locked; not replayed in Editor |
| S13 | `2d524d7fb715` | Locked; not replayed in Editor |
| S14 | `4c2e6485ce85` | Locked; not replayed in Editor |
| S15 | `e57efcb9c471` | Locked; not replayed in Editor |
| S16 | `38b4620be064` | Locked; not replayed in Editor |
| S17 | `eab18e5ff74b` | Locked; not replayed in Editor |

## S1 identity and compile gate

- Historical endpoint: 227609417100c060fc109ce22a76a11a07eb9762.
- Replay: S0 checker repairs, shared runner repairs, typed ShaderTagId comparison and single-pass shader fixtures.
  Historical replay excludes main's S2 test and later APV production changes. Its checker exemption is restricted
  to the intentional stripping-fixture directory; main already has a test-fixture exception.
- Unity 6000.6.0f1, macOS Metal. Real NUnit execution proves current scripts load; this is not a clean-Console gate.
  Earlier fixture imports recorded `attempt to write a readonly database`. The latest accepted 12/329 runs have no such error.
  Current candidate main offline check is separate from this native failure.
- The original S1 source contains no Unverified-0.5 test categories. Zero matching categories are not counted as
  passing tests. Existing targeted default-volume tests completed 22/22; this does not supply visual acceptance.

## Tests and repair evidence

| Run | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| targeted-original | 22 | 22 | 0 | 0 |
| gbuffer-isolated | 1 | 1 | 0 | 0 |
| infinity-callback-fixed | 324 | 319 | 5 | 0 |
| example-before-shader-fix | 1529 | 1271 | 205 | 53 |
| shader-candidate-targeted | 12 | 3 | 9 | 0 |
| static-fixture-targeted | 12 | 3 | 9 | 0 |
| shader-cache-wal-targeted | 12 | 3 | 9 | 0 |
| shader-cache-apfs-targeted | 12 | 3 | 9 | 0 |
| lightweight-library-targeted | 12 | 12 | 0 | 0 |
| lightweight-library-infinity | 329 | 329 | 0 | 0 |

The completed 324-case Infinity run used the repaired adapter before the shader fix. All five failures were
native/system-pass callback cases. The full Example baseline includes those five plus 200 external Pipeline
failures; the 53 skipped cases retain the original XML skip results, not exclusions added by this task.
No Player build was performed. No fixed-candidate full Example suite PASS exists. The fixed Infinity package suite passed 329/329.

All five original snippet-name variants still addressed the same SRPDefaultUnlit pass at 0/0. Corrected tests
load actual single-SubShader/single-pass fixtures and assert path, parse status and real tags before invoking the
production callback. Native ShaderTagId identity handles Unity's canonicalized tag spelling. This is callback
policy coverage, not proof of Player build scheduling or rendered shader output.

All four candidate targeted runs remain failed. Transient fixture teardown raised readonly-database errors;
persistent fixtures raised the same error at ShaderHasError. The assertions/log checking remain intact.
The real shipped Unlit, exclusively foreign shader and compute/preflight cases passed. The independent verifier
accepted only the eight adapter controls (including zero match, failed leaf, XML/progress write faults, sequential
artifact immutability and an external assembly case); the later 12/12 shader callback and 329/329 Infinity runs also received bounded independent acceptance. S1 as a whole remains blocked.

## Environment repair boundary

Task SQLite probes on ExFAT independently returned SQLITE_READONLY_DBMOVED (1032) while a new file's inode changed;
closing/reopening allowed writes. An APFS probe passed initial DELETE-mode writes with a stable inode and reopened
cleanly. These facts do not identify Unity's missing extended error code or prove Search's root cause.

The shader-error database is 12 KiB. A backed-up WAL experiment preserved logical content, but Unity reset DELETE.
A single-file APFS link was created successfully, then replaced by Unity during execution; therefore that round
was not a persistent Unity-on-APFS control. Its failed 8,392,704-byte sparse image was detached/deleted. A single
12 KiB original and small receipts remain. The later approved layout changed the actual DataStore parent path but kept its device/inode and contents on ExFAT. Only
validated AppleDouble sidecars were isolated under the approved exact rule, with real-file hash checks.

Later hangs sample SearchIndexArtifactGeneration / ImportScheduler and are distinct from the earlier GPU-readback
wait. Normal exit timed out; this task's Editor was stopped under the user's authorization. No native engine
patching, cache-tree deletion, ignored test logs or disabled tests were used.

The user requested fewer backups and disk operations. No further full backup, mirror, image or whole-tree hashing
is planned. The user approved a lightweight APFS Library root. The original Library was renamed in place to Library.exfat-original; large entries remain there and are linked. Only 5,382,396 bytes of small ShaderCache/Search files were copied. At that point DataStore was still linked. The later bounded metadata control is recorded below; no original real database was deleted.

Shader database errors disappeared during the accepted test runs. Later no-test cold starts still hung in Search artifact production / native manual-job-fence allocation: default 15 workers, one worker, temporary Builtin RP, and session-only OutOfProcessPerQueue. The Builtin control had no active RP at 15/30 seconds, but had started with Infinity; earlier startup effects remain unexcluded. GraphicsSettings/QualitySettings stayed byte-identical. These controls do not prove an engine bug or job leak. A fresh small Search-cache rebuild also failed; the original small cache was restored and the failed generated cache removed. The startup-index preference control is not a pure idle test: the user opened Spazon during it; the process responded afterward before finally timing out. The preference was restored byte-identically. A stricter guarded startup control observed no current pipeline before selecting Builtin, kept Search indexing enabled, and still hung in the same Search/manual-fence stack after three successful heartbeats. The temporary source/meta was removed. Original GraphicsSettings/QualitySettings SHA values remained unchanged.

The user authorized autonomous bounded environment repair. The 98.22 MiB paired SourceAssetDB/ArtifactDB control was applied once, reproduced the Search hang, and was rolled back: only task-created APFS copies/locks were removed and original ExFAT links restored. A subsequent DataStore control copied only uds.db (9,797,632 bytes) and created APFS locks/root; 262 original entries remain linked on ExFAT. It produced real search results, accepted 12/329 tests and normal exit; the next cold start is responsive. New normal cache bins are monitored without following links or repeatedly hashing big data. This is a useful bounded recovery result, not a proven Unity native root cause.

## Asset preservation and outstanding acceptance

The latest 427-input audit found 425 byte-identical inputs. EditorSettings and TimeManager underwent native
serialization-format changes during the external suite. Its known reload-options change 3→1 was restored only
at that field; byte delta versus the fresh post-test preimage was one byte. An independent cold reopen/no-op
was byte-identical. TimeManager was never assigned; its rational-time conversion has separate live evidence.
Old binary settings were not restored wholesale. The original user asset edits remain outside package commits.

Natural GameObject/Camera creation, auto-add, one-step Undo, Redo and restoration received independent acceptance with the original scene clean and selection preserved. Film→ACES normal-frame parity and Debugger UI still need evidence.
S2-S17, S12 three-way shadow evidence, S16 negative regression control, S17 baseline reuse, final HEAD acceptance
and valid GPU timing are not executed/accepted. No image baselines have been recorded to conceal these gaps.

## Evidence and retained state

Exact XML counts, hashes and paths are in S1-results.json. Raw failure/test results, candidate patches and current
asset preimages remain under the owned run02 directory for environment repair, S1 independent revalidation and
the final protected-input audit. The last full storage accounting was 177,899,305 logical / 674,234,368 allocated
bytes before later small controls; the discarded image adds no retained capacity. Confirmed run02 preimages remain for the final audit; run01 retention is not assumed. Current state: one Editor runs the historical S1 candidate; full Example tests are in progress, with no production frame capture active. The failed small Search rebuild and redundant healthy raw sample were reduced to receipts/excerpts (about 0.5 MB removed). This is retained in-progress state, not a claim
of final cleanup. No Fix S1 commit or PR-ready verdict will be issued until its required gates close.

## Latest S1 test-adapter continuation

The prior full Example run was interrupted at 1063/1534 by the old adapter cancelling a legitimate script reload; no complete XML or PASS is claimed. The new adapter checkpoints the original UniqueName set and per-leaf state only at discovery/reload, reattaches without Execute, and validates callbacks/final XML. Source review and real reload control are independently accepted: 1/1, same GUID, one reload/resume. Targeted smoke 12/12, corrected SDK ViaClient 61/61, Infinity 329/329 passed. Eight synthetic callback/start controls are accepted; outcome XML representation strengthening is pending. The first reload retry failed on the known generated script left by the interrupted run; its exact task-owned folder was cleaned and failure retained. Full Example rerun is active.

## Latest complete callback result and remaining fixture acceptance

`ledger-example` completed all 1534 started/finished callbacks: 1480 passed, 1 failed, 53 skipped. Raw framework XML remains 1501 (1480/1/20); installed UTF1.8.0 loses32 Explicit/Parent and1 Skipped/Parent leaf when rebuilding the tree across reload. The distinct callback-derived report restores only those33 pre-reload, fully persisted skipped XML nodes. Independent verification matched all identities, reasons and outcomes. `frameworkComplete=false` and `callbacksComplete=true` remain explicit; this is not an all-tests PASS. The prior1534-callback run with2failures and incomplete evidence remains unchanged.

The SDK config Save cache invalidation passed8/8 including the original15→40 failure; actual equal file timestamps were not measured, so no filesystem-specific cause is claimed. Current native/SDK screen pixels agree exactly. OriginalGUI fixture188/137 also appeared under temporaryBuiltin; Graphics/Quality settings stayed byte-identical and assignments restored. A texture-only authoring candidate failed native acceptance. An explicitUI material produced native+SDK128/64. First automated target skipped because overlay was not observed; bounded normal repaint then executed all unchanged assertions and passed1/1. A later resource-initialization cleanup revision must still be retested. No production capture/rendering change was made for this defect.

The script fixture now records exact owned EntityIds across reload for teardown; a same-name unregistered sentinel control is prepared but not yet run. Earlier unsavedSDK objects were not removed by prefix. The originalEditor normal exit is confirmed with PID11420 absent and shutdown log; only then was a new originalExample launch requested. No cold-reopen acceptance is inferred solely from an Exit scheduling response.

## Final SDK fixture continuation

Original Example PID20842 cold-started only after PID11420 normal exit. The final Capture initialization/cleanup revision passed `capture-final-initialization` 1/1, zero skipped, with original gray, direction and screen/camera assertions intact. Script ownership `ownership-negative-registered-generations` passed 1/1 through one actual reload; original two registered IDs and UTF repeated-SetUp IDs were removed while an unregistered same-name sentinel survived. TearDown left no registered keys or objects, and original Spazon restored clean. `ownership-class-final` passed31/31 through two reloads. Independent source/result review accepted the ownership scope.

The earlier pre-Execute unsaved additive sentinel control is InvalidControl: UTF closes such scenes before RunStarted and recreates source objects after RunFinished. It is not evidence of user-object loss. Two development failures (captured iterator closure reset; an incorrect assumption that UTF does not repeat SetUp after reload) remain in their original XML. The final negative test stores the original pair explicitly, verifies it survives reload, and checks cleanup against the same helper used by TearDown. Full Example `final-sdk-example` is running1535 tests, including this one added ownership contract.

## Current acceptance and integration checkpoint

Historical S1 repaired candidate f9b80a2 runner identity and all other4 source hashes matched on return to2276094.
Original Example PID33053 compiled/reloaded normally; ACES effective/profileclean; static0errors/0warnings.
The original Spazon SceneSetup, null selection and runInBackgroundfalse were restored; parked fixture sources
were returned before launch. Both reference Editors exited normally before the next checkout.

Final full Example run `final-sdk-undo-example`:1535discovered/started/finished,1482Passed,0Failed,53Skipped,
root Skipped:Ignored. Raw framework XML has1502leaves(1482/0/20); separate callback-results.xml restores33actual
checkpointed skipped nodes lost by UTF1.8.0 across reload. Both raw and derived evidence retain skip reasons;
this is not1535/1535PASS. The final ownership31/31 and capture initialization1/1 runs passed. A delayed Undo
control reproduced SDK-owned object resurrection with the old helper; FlushUndoRecordObjects before clearing
exact owned records fixed it while unrelated Undo/same-name sentinel remained valid. Final scene had9original
clean roots,0registered residue and no generated CLI195 asset folder. SDK patches remain local external changes.

Film evidence: `film-S0-S1-comparison.json`, the3accepted capture directories and native images. Entire640×360
RGBA16F AntiAliasing and DisplayColor buffers match byte-for-byte between S0Film and S1ACES (maxerror0).
ModeNone retains identical input and changes output. Effective mode1/curve/exposure/grading match; normal Game
frames advance, Present executes and all readbacks retire. Authored .18/.5/2/8 material values yield measured
pre-post inputs .0272064/.213989/4.58984/97; they are not asserted as scene-linear .18/.5/2/8. The native Game view
uses a deliberate640×360viewport in its lower-left; surrounding black is outside the fixture viewport.
The S1native screenshot contains an earlier camera-selection command error and is only label/image evidence.
The initial frame1capture is not accepted as advancing-Play evidence. S0reference staticFAIL24 remains separate.

Debugger public schema and all native panel screenshots confirm Rendering/Lighting/Mesh unchanged and Temporal
removed. No DebugView or Temporal control changed. Independent verifier accepted Camera/Undo, Film and Debugger
alongside the prior shader/runner/SDK scope. This does not imply other scenes or later stage features passed.

Main PID33534 compiled all scripts but first rendering error was incomplete defaultProfile (missing APV options).
The first-available AppendMissing menu initially added onlyProbeVolumesOptions in memory: all14old component snapshots,
IDs, values and source/meta bytes stayed unchanged. Normal exit later auto-saved the task APV addition; with Editor absent, fresh pre-menu bytes were restored under the source/value/meta guards. This was a rolled-back menu control, not S12 migration acceptance. Main targetedShaderStripping
run13 =4Passed/9Failed: one missing utilityShader resource, seven empty compiler-data/APVkeyword ownership
assertions, one invalid Unverified-0.5 category. ResourcePath targets exist in the main tree (static0/0); GlobalSettings resource loading and atmosphere
kernel readiness fail, with root cause still unconfirmed. The initial S17-path diagnosis was withdrawn. These are preserved main integration failures; they do not overwrite historical S1
results or authorize skipping tests. Stage ownership and commit boundary are under independent review.

Main static0/0 and offline checker controls10/10 passed. Main full-runtime acceptance and S2–S17 remain pending.
Only generated empty ActorAction/Tool directory metadata from historical import was removed after Editor exit,
with exact contents/hash retained; no user asset was deleted. Task Film assets are retained only until their
final consumers finish. The active APFS Library/DataStore metadata layout is live cache state, not disposable
scratch. Final cleanup is not yet complete; avoid additional broad backups or scans.

Fix S1 commit: `88933dcfd068fa739737b350da4c18572a1f31f6`. The staged diff check reported only conventional empty-field trailing spaces
in generated Unity `.meta` files; static remained0/0. No history rewrite was performed.

Remote receipt: PRbranch88933dcfd068fa739737b350da4c18572a1f31f6; remote mainc92be7707f45a1789688e5cc16e0845f183b2910. Package worktree was clean before detached S2 replay.

## Completed-consumer evidence compaction

After independent review of the precise S1 evidence index, 31 deliberate payloads (9,330,153 bytes), 299 receipts, the 33 original skip leaves, bounded log excerpts and hashes remain under S1/. Consumed raw paths in older sections are historical source identities, not retained openable files. The 15 key images, original failed/full final XML, external SDK patches and two small native configuration snapshots remain available there. Cleanup retired 2037 files and reduced S1 allocation 1189888→1024 KiB, releasing 1188864 KiB. Only the cold-start helper, its empty output parent and one original 4096-byte identical sidecar specimen remain intermediate: their exact future consumers and the conditional large-file-hash limitation are in S1/cleanup.json. Active Library/UDS/ExFAT caches were untouched. Later current progress is recorded in the S2/S3 reports and main PLAN; the table above describes the S1 checkpoint.
