> Delivery correction (2026-10-23): this report and its evidence were previously present locally but ignored by Git. This S17 delivery adds the report and the explicitly indexed [EarlyStages evidence archive](EarlyStages-closure/manifest.json). Earlier failures and acceptance boundaries below remain historical facts.

# S7 local validation receipt — 2026-10-22

Original stage: e6155f32fbb67fac2df7532108550ada67be5ee6. Accepted predecessors: local main5b34e14.
Unity6000.6.0f1 / CoreRP17.6 / macOS Metal / AppleM3Max. Original InfinityExample and Scene_Spazon only;
verification objects were runtime DontSave objects, never saved over source scenes.

## Result boundaries

The historical S7 repaired snapshot passes its compilation, focused/full tests and scoped image/behavior checks.
The final main candidate passes S7 focused and normal-frame checks; main full EditMode still has14 pre-existing
failures. S8–S17 and final acceptance remain required. Performance is Open. PR11 must remain Draft.
Fix commit: `f4bbc3a5eaec253f79369e4ef96dfc2ef3421b0d` — independently accepted, committed on local main and pushed to origin/claude/infinityrp-0.5. Remote main remains c92be7707f45a1789688e5cc16e0845f183b2910.

## Original failures and fix

- Four historical LightBinningTests were invalid under NUnit because of the hyphenated category. Replay the already
  accepted S2 category normalization; no test was skipped, disabled or removed.
- Actual Metal Deferred invocation returned corrupted integer FLightLoop state: tile base110 became0x36dc0000,
  mask1 became0x33800000; it visited out-of-range local indices and produced no direct light. Scalar arithmetic and
  uploaded inputs were correct. Caller-owned out initialization restores base110/mask1/index0 and contribution.
  This is an observed lowering-sensitive behavior; the precise compiler/driver stage is not identified.
- CPU light-tile projection used renderIntoTexture=true while depth consumers use the canonical false projection.
  After the out fix alone, asymmetric upper/lower lighting still failed. Calling existing
  GraphicsUtility.GetComputeGpuProjection restores both. No shadow-matrix change is included.
- All six current consumers use the out API. Temporary invocation keywords/UAVs/counters were removed.
  Requested-only production-mask byte capture and immediate DeferredLighting transfer remain for diagnosis.

## Tests and compile

Historical final full:1617 discovered/started/finished =1564Passed/0Failed/53Skipped. Two reload/resumes.
Callbacks/checkpoint complete; native XML1584=1564/0/20 plus33 genuine earlier Skipped callback leaves reconciled.
frameworkComplete=false; rootSkipped:Ignored. This is not1617Passed.

Final main focused: actual Deferred GPU8, LightBinning6, resource lifetime2, light record9 — all25Passed.
Final main full:1689=1622Passed/14Failed/53Skipped; callbacks/checkpoint complete, frameworkComplete=false,
raw1656 plus33 genuine skipped checkpoint leaves; both roots remainFailed(Child). Existing14failure and53skip
identities unchanged from S6 main-carrier-full1676=1609/14/53. Thirteen new cases all pass (8DeferredGPU,
2projection,3previously added angularGPU). Known failures remain: external Pipeline reload cleanup; default APV
Profile factory; material upgrader; geometry seam path; probe record layout; graph viewer; eight shader-stripper
cross-shader keyword/identity cases. They must be resolved in their stages, not hidden by this repair.

Actual Unity main compile:0errors/0warnings. Static checker:0errors/0warnings; diff whitespace clean.
Offline DXC:312 attempted entries/variants,310successes,2 unchanged GTAO spatial dynamic-unroll failures.
The unchanged SpatialFilter sourceSHA is518d4233e07980eb48af5c642558586966e555e04713dc0a1ff6b690ac654df0.
No all-shader-compiled claim.

The permanent GPU test exercises actual production DeferredShadingCS at0/1/31/32/33/63/64/65 locals with
nonzero directional offset. It proves aggregate contribution across mask words. The independent exact-index
probe checks4480queries/455maskwords across multiple tiles, empty/range controls and tail guards. Neither is
substituted for actual normal-frame consumer checks. Return-by-value negative control completed0/1/31/32:
0passed;1/31/32failed. Case33hung readback and the owned Editor was terminated/recovered. Later cases were
unrun; that run is partial, not an8-case completed negative result.

## Ordinary rendering and volumetrics

Historical and final main ten-case normal-frame sets capture only Lighting/Depth/DisplayColor, each33successful
frames before one capture,35normal callbacks,finite/submitted/terminal/retired and0outstanding. No diagnostic
reader extends mask lifetime. Main upper/lower/Forward have4570/4577/4570lit pixels at correct positions;
inside48600; behind/offscreen have no increment>.001. Far controls retain local light at100m+ distances.
Actual contact sheet was viewed and independently accepted.

Multi-camera actual same-frame local counts0->1 and1->0 verified. Explicit full-image positive red>.001,
float64 energy difference1.7451630645e-6; known-jitter-corrected centroid residual[-2.3974e-5,.0001688154]pixel.
Empty-target receiver ROI x30:290,y5:175 maximumRGB difference3.814697e-6; full image maximum.003479004.
The earlier definition-incomplete6.68e-6 summary is superseded, not used as acceptance evidence.

Matched S6/S7 fog8phases: Lighting max3.0517578e-5/relativeL1 max2.43198e-5; SceneColor max1.2207031e-4/
relativeL1 max5.20941e-5. Cloud16 actual global frame&15 phases, exact current jitter/jittered matrices/reset,
Volume settings and CPU light records: fullRGBA Cloud and SceneColor byte-identical for all32paired buffers.
Two sides each48successful/16captures/64retired/0outstanding. Alpha/transmittance included. Saturated and
unmatched-phase controls remain unaccepted. Normal owned camera enable pauses adjusted phase; no history or
shader substitution. A camera cut did not restart committed jitter count.

Native Frame Debugger through public menu/CUA: original Spazon MainCamera153events, one ComputeLightTileMask
actual Compute Compute_LightTileMask leaf. ScreenshotSHA8ad2c3aa91f8515956cd6ba8fe5db69f4f6c36bbb4ef04d37e0684f19a50fe6b.
Right payload panel blank; no kernel/group counts claimed. Earlier internal-reflection export timed out; retained
as failed historical diagnostic and not retried as the acceptance path.

## Performance, restoration and evidence

Fixed320x180 far125 control,60warmup/120attempts:0validGPU samples, GPUtime/count0; CPUcount2 cannot support
a comparable single-camera timing set. No3x300validframes, median/P95 or before/after claim. Initial unpumped
instrumentation hit CoreRP GetSample(0) with an empty recorder; stopped/restored and retained as failed task
instrumentation. It did not alter product code or the already completed ordinary-frame evidence.

Final original Spazon:9original roots, clean, no task objects; inactive Play/compile/tests/timingrecording;
captureCompleted/outstanding0. SceneSHA0b987d060327f5baf5e9d2faefdd9ddafadeddabe962f90016698fbe408de341,
metaSHA36fb9d0329fc735eca7d4c5548b21000d75bc11c5d24744a92f7a6822f8154c1 unchanged.
OuterGSf22a4f11 unchanged. Later APV Profile candidatefe022b4c remains separate and excluded from S7 commit.

Evidence root: original Example/intermediate/verification-0.5/20261020-run02/S7. Closed raw buffers/XMLs are
losslessly compressed with per-fileSHA manifests before redundant originals are removed; no Library/DataStore
removed or migrated. This stage retains raw controls for final verification; final task cleanup is still required.
Key receipts: main-final-review-manifest.json, main-restoration.json, main-normal-frame-gate.json,
synced-cloud-comparison.json, multicamera-explicit-analysis.json, main-final-full/callback-results.xml,
closed-canonical-test-and-control-evidence.tar.gz, closed-final-normal-and-multicamera.tar.gz,
closed-matched-volumetric-evidence.tar.gz, closed-s6-synced-cloud.tar.gz, closed-s7-accepted-cloud.tar.gz.
