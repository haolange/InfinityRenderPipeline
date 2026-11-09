# S16 actual Unity verification and fix-forward

Original endpoint: `38b4620be0643bc38e595144454a5b7f872e3656`. Predecessor repairs are explicit, not rewritten history. Main candidate starts from `1977445eb495fa08091f1b5602a53fcaf672980c`. Unity 6000.6.0f1 / CoreRP 17.6 / Shader Graph 17.6 / macOS Metal / Apple M3 Max; original InfinityExample throughout.

## Original failures

Original compile: zero errors/warnings. Original ShadowBudget discovery produces three Invalid failures because NUnit reserves `-` in `Unverified-0.5`; canonical metadata-only spelling adaptation leaves test method/assertion bodies unchanged. Actual stage classes then pass 13/13 (Budget3/CubeFilter4/TileMask1/ImageComparison5). These are numeric tests, not normal image acceptance. Extra actual Unity probes show one NaN in640×400, trailing bytes and zero dimensions can all incorrectly Pass. In real Play, the original Runner fails Capture.Validate because width/height/ROI are absent. No baseline was recorded from that failing runner; no automatic default fixture replacement was executed.

## Minimal repair and contracts

Comparator rejects nonfinite RGBA, invalid/overflowing dimensions, empty payloads and incorrect exact byte length; RGB diagnostic errors remain visible and thresholds are unchanged. Known supported formats decode stored values; DisplayColor is display-linear/HDR, not a second output-transfer owner.

The same menu/internal Runner supplies explicit640×400ROI,90warm-upframes and one normal submitted Game frame. Capture owns a session reference and reload lock; independent capture is never cancelled. Cancellation waits for actual callbacks and physical retirement before leaving Play and restoring SceneSetup/Selection. File persistence failure cannot prevent in-memory safety progression; SessionState preserves recovery across reload. Unsaved edits latch NeedsAttention and stay dirty until explicitly resolved. Four early fixture creators accept scoped paths and use the shared explicit-asset/native-dirty save boundary. No Camera.Render, Player build, cache mirror or other-session Editor action.

Baselines are immutable SHA-named payload plus atomic metadata commit; an old valid pair survives failed replacement metadata. Comparison checks dimensions, format, color semantics and SHA. NoBaseline/Error/Cancelled/NeedsAttention/missing cases cannot be Passed.

## Actual main controls

- Unity compile/staticcheck0errors0warnings. Comparison16/16, request1/1, fixture-save6/6 including4foreign-same-directory controls, category71/71.
- Final full-v2:1824 discovered/finished=1770Passed/1Failed/53Skipped. The sole failure is the later S17 oldDepthPass source path. Native1791 plus33actualcheckpointSkipped exactly reconstruct callback1824; callbacksComplete=true/frameworkComplete=false,2reload/resume. No ignored leaf is relabelled Passed.
- Eight normal captures: finite, Completed, one frame each, both retirement channels complete, zero outstanding; first NoBaseline8 correctly yields Failed. Record8 then Compare8 Passed. Final repeat after all fault controls:6rawexact, AreaRMSE2.536311293e-6/Screen2.143424848e-6, zerooutliers.
- Actual light1.2→4.8:Local Fail RMSE.3051714456/outliers81.35703125%; restore1.2→exactPass without baseline replacement.
- Different candidate payload + blocked metadata.tmp→Error; prior metadata/payloadSHA unchanged. Unreferenced failed payload retired after independent review; raw candidate retained only for that review archive consumer. Corrupt baseline SHA→Error; original metadata restored exactly.
- Early state.tmp write failure:Failed/results0/no case capture; no new Play/capture producer. Pending cancel+writefailure:observed2→1→0, real captureCancelled/retired1+nativeRetired1, runFailed with persistenceError and restored original scene.
- Independent capture:after ownFinished, publicslot replaced; foreign181successful/1captured/Completed/0outstanding. Runner remainsDrain andPlaytrue until foreign completion, thenCancelled and restores.
- Explicit restore fault:task Local scene deliberate edit remainsdirty/NeedsAttention. Only that owned scene is reopened to discard deliberate control, realCancel resumes and restores Graphclean plus exact nonemptyGlobalObjectId941327271e45845eca178d729054adbe/174254443. No dirty flag clearing.
- Protected18project/source/bakefilesSHA unchanged.

## Repaired historical replay

Detached originalS16 plus209SHA-verifiedfiles:201predecessor sources with11currentS16overrides (3overlaps/8newpaths). No S17folder movement. Compile/static0/0; category155/155. Full1822=1769Passed/0Failed/53Skipped,2reload/resume. Same accepted16baselinefiles are byte-identical. Eight comparisons Passed:6rawexact,AreaRMSE1.168405107e-6/Screen1.388249516e-6,0outliers,finite/0pending/retired1+1. Independent verifier approved the historical subset.

## Limits

Regression acceptance covers fixed-input consistency and controlled objects in eight fixtures. Known sky black bands, dark-region/probe/Display residuals remain documented image defects/Open; baseline green does not fix them. BakedGI/APV/LPPV/ShaderGraph rely on separate stage evidence. No new valid GPU performance timing, Player, D3D12, Vulkan or HDR claim. PR11 remains Draft until all required overall gates close.

## Delivery boundary

Returnedmain/restored14frozenfilesexact. Verified-only metadata removes13Categoryattributes from26executedleaves without changing test bodies; remainingcategory45/45. Finalmain full1824=1770P/1F/53Skip and eight same-baseline comparisons Passed after cold restart. Compact SHA-verified evidence, independent final signature, exact-scope FixS16commit/explicitPRbranchpush and raw cleanup complete this batch. The final stage summary records the resulting commit hash. S17 remains locked until S16 delivery.

Evidence: [archive manifest](S16-closure/manifest.json), with original failures, actual test XML, source SHA, baseline/capture identities and negative/recovery controls. Full checkpoints and duplicate raw buffers are omitted;20consumed raw controls are retired with original SHA in cleanup.json; 33 original skip objects retain their source SHA, and the indexed35raw frames reconstruct losslessly from baseline identity or byte replacements plus SHA verification.

Cleanup: own27633exited normally; S16 temporary root absent. Removed 1700files/187271537payload bytes/608436224allocated bytes after archive/member/SHA and independent verification. Only three small CLI/case files carry to S17; baseline/fixture/bootstrap assets have that explicit next consumer.
