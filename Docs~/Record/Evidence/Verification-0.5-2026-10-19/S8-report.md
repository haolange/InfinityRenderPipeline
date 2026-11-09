> Delivery correction (2026-10-23): this report and its evidence were previously present locally but ignored by Git. This S17 delivery adds the report and the explicitly indexed [EarlyStages evidence archive](EarlyStages-closure/manifest.json). Earlier failures and acceptance boundaries below remain historical facts.

# S8 verification — 2026-10-22

Unity6000.6.0f1 / CoreRP17.6 / macOS Metal / Apple M3 Max. Original InfinityExample and Scene_Spazon were used; no fixture scene replaced the source scene, no source scene was saved, and no other Editor was operated.

## Source and repair

Original stage:7bbf86db97b5e92f026aa19894829342a990657a, with accepted S0-S7 repairs. The shape formulas introduced no additional observed failure in this stage. The final main reload exposed a distinct dependency-order defect: Debugger registration invoked the CoreRP Volume panel (introduced by S15/96fa4be) before VolumeManager.Initialize. The constructor now initializes its owned volume state before registration. The original S8 has no S15 Volume panel; only the existing call-order edit was replayed there.

The production Deferred GPU regression now includes six fixed-input, nonzero diffuse/specular weight controls in RGBA32F and clears the output before every dispatch because the kernel reads it as emissive. All original eight mask-count cases and their analytic assertions remain.

## Compilation and tests

- Unity compilation:0errors/0warnings on main and historical replay. StaticCheck:0errors/0warnings; diff whitespace check clean. Shader files unchanged from accepted main S7.
- Historical shapeGPU3/3; DeferredGPU9/9; lifecycle11/11. Full1621=1568Passed/0Failed/53Skipped, two reload/resumes; final repair replay has identical stableUIDs/results. Native XML1588 plus33 real checkpoint skip leaves reconciles to callback1621.
- Main initial focused21/21; after startup repair lifecycle11/11 and Deferred9/9. Final full1690=1624Passed/13Failed/53Skipped, two reload/resumes. Native XML1657 plus33 real checkpoint skips reconciles to callback1690. FrameworkComplete=false, callbacksComplete=true; root remains Failed(Child).
- The13Infinity failures are unchanged. The external Unity.Pipeline CleanupOwnedObjects across-domain case changed Failed→Passed on the legitimate repair rerun; no causal improvement claim is made. Full main is not green. XML results and exact stableUID changes are retained in S8-verification-receipt.json and accompanying XML.

## Actual frames

32 same-camera-phase normal controls per historical/main run cover point, sphere, line, tube, rect, disk and0°/2°sun, glossy/rough receivers, radius, term weights, rect backface and barn doors. Each control used32 accepted warmup frames and one real normal-frame capture, with exact current/previous jitter and matrices checked. Depth/GBufferB/GBufferC are identical within each run; actual GBufferA roughness bytes are31/255 and166/255, specular level128/255. No Camera.Render screenshot substitutes were used.

Sphere radius0→0.5 changes rough integrated direct light4.3499percent historical/4.3501percent main, below the predeclared5percent. Glossy peak drops while support grows. Line/tube streaks, rect shape, disk/sphere roundness and sun-angle widening were inspected at preserved image aspect. Main/historical peaks match; maximum integrated specular difference0.06752percent is below the predeclared0.5percent investigation alarm.

Rect backface historical versus off is exactly zero. Main versus a disabled-light baseline has a separately retained1.43051e-5 residual. A stronger main comparison with the same enabled back-facing rect, weights1/1 versus0/0, has byte-identical Lighting. Barn doors narrow faint support: at1e-5 threshold main25722→12601pixels; the .001/.01 bright core stays unchanged. Do not describe the result as large bright-core narrowing.

Normal RG16F component equations retain max residuals .00587654/.00293922/.00149822. Even pre-atmosphere normal controls leave1/1/2channels beyond a final-half-ULP-only model; the cause remains unproved. The fixed-input actual production RGBA32F full-pixel oracle independently passes its predeclared1e-5*max(1,abs(expected))criterion. That contract proof does not relabel the extra RG16F experiment as passing.

A native Game window shows a round Disk highlight in original Spazon; it uses an auxiliary actual1638x1754viewport, separate from320x180 numeric controls. After the constructor repair, a fresh domain has a live Infinity pipeline, initialized VolumeManager, a four-widget Volume panel and seven total panels; two registrations do not add widgets. A new real normal Game capture after repair passes33successful/1captured/3retired/0outstanding; its Point highlight was inspected. Historical repair replay also has a new accepted frame.

## Restoration and limitations

Each owned Editor exited normally only after task owners restored cameras/lights and destroyed runtime resources; Spazon remained a single clean scene with the original nine roots. Original source/meta hashes are preserved. The separate approved later-version APV Profile candidate is excluded from this commit; outer GlobalSettings remains outside package commits. No DataStore copy/migration or other-session Editor operation occurred.

No valid S8 GPU performance set exists; Perf stays Open. Other graphics APIs, Player and HDR remain unverified. S9-S17 and final HEAD are pending.

Fix commit/push:f033d5a6b1d0b6bb870cc52a88ab6927c7c98e57; remote claude/infinityrp-0.5 verified equal. Remote main remains c92be7707f45a1789688e5cc16e0845f183b2910. Independent verifier accepted the seven-path source batch, manifest e78b84bbd4003f92484ee1fa8d58be8787efbf12d5737b72c55878aba263fce8.

The permanent `S8-recovered-results-and-restoration.json` preserves all33 missing framework Skipped checkpoint objects per run with original XML and started/finished/checkpointed flags, source hashes, clean-scene restoration receipts and the original startup failure excerpt. Stable UIDs come from actual callback/checkpoint data; the remaining native/callback verdict tuples match as multisets. Native XML has no stable UID.

Numeric fixture:320x180, FOV45, camera(0,0,-8), XY receiver z3/normal-Z; local intensity2/range20, sun intensity0.6. Base color0.18, metallic0, specular0.5, roughness0.12/0.65. Sphere radii0.3/0.5, line length1.5, tube length2/radius0.08, rect1.6x0.6, disk radius0.5, sun0/2degrees. Contact sheets show matched-ambient-subtracted Lighting with common x/(1+x) then gamma2.2 and original pixel aspect.
