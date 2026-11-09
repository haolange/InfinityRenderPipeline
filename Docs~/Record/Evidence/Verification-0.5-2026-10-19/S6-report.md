> Delivery correction (2026-10-23): this report and its evidence were previously present locally but ignored by Git. This S17 delivery adds the report and the explicitly indexed [EarlyStages evidence archive](EarlyStages-closure/manifest.json). Earlier failures and acceptance boundaries below remain historical facts.

# S6 本机验证（进行中，未验收）

平台：Unity6000.6.0f1 / CoreRP17.6 / macOS Metal，原 InfinityExample。

| 门禁 | 当前事实 |
|---|---|
| 原始身份 | 834f41de69b2 + 已验收前序修复；S6 replay-identity.json 冻结66文件 |
| 编译 | 原始S6与修复回放均成功；main实际CLI编译0errors/0warnings |
| 原始阶段测试 | Shape14/14、Layout2/2、Record7/7 |
| 原始全量 | 1585=1531P/1F/53Skipped，回调完成，reload/resume2/2；失败为空utilityShader |
| 修复后main自测 | Shape15/15、Record9/9、Lifecycle11/11；独立审查已核对XML |
| 第一次修复后S6全量 | 1593=1539P/1F/53Skipped，回调完成，reload/resume2/2；失败为空blueNoiseRankingTile |
| 配置完整后的S6复跑 | 仅内存补齐三个蓝噪声槽；Shape15/15、Record9/9通过。Layout当前Starting、零发现，不计通过；后续Lifecycle和全量尚未启动 |
| 实际帧 / native culling / bake | 尚未运行，不能从数值测试推断通过 |
| 修复提交 | 尚未提交；main候选patch090577078a749a82c15324353b039eb618910df6eba0d6659685f243ffee9621，源码层独立审查通过 |

已复现并修复的候选：旧组件首次Inspector采用无Undo；管线退出丢失此前Lightmapper delegate；实时Light颜色未线性化。前两项任务自有preview对象/公开哨兵原始失败与main复验均保留。三组乱序释放、外部F1/F2与幂等控制通过。六组公开Unity提取对照，原4组不一致，修复后RGB精确一致；这并不证明实际bake的照度、锥角或半影。原有raw-color测试期望因违背Unity的线性颜色契约而修正，另加公开Extract oracle；没有禁用或删除测试。

main早期两次Lifecycle5P/6F是任务临时Profile被UnloadUnusedAssets清理的前置失败，原XML保留。用HideAndDontSave明确保留临时对象后11/11通过。资源补配按Unity Object有效性判定，不使用CLR object-null；无GS源资产保存。main82740完整原GSJSON已恢复且无脏场景后正常退出。

当前阻断：自有Editor84585在Layout运行启动前进入SaveCurrentModifiedScenesIfUserWantsTo原生NSAlert。线程采样已保存；不能把零发现算通过。官方主线程CLI超时，官方只读/api/dialog未覆盖该提示（active=false）。Computer Use只能绑定到其它session的窗口，未向其发送输入；已请求只在InfinityExample点击Cancel保留现场。没有强制退出、保存或丢弃未知场景。需在恢复后核对dirty来源并修正任务测试现场保护，不能归咎或干预其它Editor。

独立审查指出还需窄化playbook S6.2：载入/adoption不改native字段，同一修复后版本上adoption前后画面不变；S5到S6的灰/彩灯亮度变化单独说明，不能声称历史画面exact。

数据：S6所有临时数据在原Example intermediate/verification-0.5/20261020-run02/S6，当前约65MB，512MiB上限；生产者为目标Editor与测试框架。具体下一消费者为模态恢复、历史候选帧/烘焙和独立阶段验收。已删除消费完的source-fix.patch及source-pulse preimage，精确字节见consumed-input-cleanup.json；其它数据仍有消费者，未宣称整体清理完成。GS及meta源字节仍与原哈希一致。

S7–S17继续锁定；PR11维持Draft。

## Continuation: real frames and native Disk blocker (2026-10-21)

- Historical fixed full EditMode: **1593 discovered =1540 Passed /0 Failed /53 Skipped**; callbacksComplete,2 reloads/resumes. This predates the newly discovered Disk contract failures and is not full S6 acceptance.
- Fifteen 320x180 normal Game captures completed with Lighting/Depth/DisplayColor, one frame each, all buffers terminal/retired, no NaN/Inf. Records use the exact producer frame. Interior Depth reconstructs receiver z=3. Rect back/off agree; distance16/18/20m packs30/15/no record and culled counters0/0/1. Directional remains present beyond the local distance limit.
- **Disk fails:** both front/back have zero production records. Native Disc type assignment changes bake mode. Restoring Realtime on a bare Disc keeps that mode for6 actual frames, but public native Cull never includes it. forceVisible and bounding-sphere override also give zero production records.
- API bake-mode preservation alone does not close Undo/Redo: Realtime and Mixed revert to Baked on Redo under both RecordObjects and CompleteObjectUndo. Latest precise main candidate shape suite: **18=16 Passed /2 Failed /0 Skipped**. Keep both failing contracts; no test is disabled.
- Adoption test now uses DontSave objects moved into an owned PreviewScene; it passes and asserts original scene dirty state unchanged. Dirty-scene preflight is added before test Execute.
- Independent review accepts an explicitly documented authored Disk→native Rectangle mapping as a next candidate after the two Undo failures. Automatic approval review rejected this native/API/gizmo semantic adjustment; one explicit authorization question is pending. **Carrier code was not applied.**
- BakeHarness was dry-run compiled only. Reviewer requires transactional restoration, exception-safe readback/cancel, exact case guards, cumulative atlas budget and proper decoded/normal-frame comparisons before Setup. No bake or task asset folder was created.
- A transient edit omitted bakeType's local declaration during cold startup, causing3 CS0103; fixed. Owned failed startup94284 had no graceful CLI channel and was closed with official CLI force for the exact original project. Retry94686 compiled and ran the precise tests.
- Owned fixture from84585 actually unloaded and its resources disappeared. Both84585 and94686 ended normally after Play/tests were inactive, scenes clean and full temporary GS JSON restored; GS dirty=false. Fifteen protected source inputs remain byte-identical. Other sessions were not operated.
- Main remains98012a1 with uncommitted S6 candidate. No S6 commit/push; S7–S17 remain locked; PR11 stays Draft.

Retained S6 scratch: 116285676 logical bytes in 958 files; next consumer is approved Disk fix, historical replay and independent S6 acceptance. Eight consumed discovery scripts plus matching sidecars were deleted. No DataStore migration/copy or bake asset creation occurred.

## Current continuation: approved carrier and bake evidence (2026-10-21)

The user explicitly approved authored Disk -> native Rectangle, retaining Additional Disk and GI Disc baking. This supersedes the earlier pending-approval state. Candidate main patch716db7a5 was replayed on original S6: Shape23/23, Record9/9, Layout2/2, Lifecycle11/11; complete Example1601=1548Passed/0Failed/53Skipped, callbacks complete,2 reloads/resumes. No S6 commit or push yet.

Three Disk modes survive save/reopen with stable GUID/local IDs and byte-identical second/no-op saves. Four actual normal frames now show Point/off/Disk-front/Disk-back; front/back are Rectangle/Realtime with Disk radius0.85 and production visibleIndex0/flags516. Independent review accepts normal collection and one-sided lighting: common reconstructed receiver interior38532pixels, front increment0.03155720 over off, back mean difference-0.0000009904/max0.0000152588. Full-image edge difference0.00747681 remains reported because jitter differs. No bitwise-equivalence claim.

Actual GPU bakes and five valid Game captures cover Disk, sun0/2 and Spot Baked/Mixed. Each valid capture has >=33 distinct frames before its exact producer, finite retired raw buffers and settings/JSON restoration. The first frame1 capture is an invalid progression control. Initial lightmap readbacks failed due streamed mip2; task-owned generated textures only were imported non-streaming for native256 half-float analysis. This does not validate or fix production streaming. Independent review accepts Disk baked-data normal consumption and Sun source-angle widening: all9 measured edges widen,10-90percent width median0.0802809967m ->0.1483922484m.

S6 remains blocked on Cone attenuation. Original production GPU probe fails four transition points against CoreRP smooth attenuation:20deg actual0.6201871 vs0.384545326. The bake radial profile independently fits a squared window. Initial Spot total-Lighting ratio1.3925 also contains realtime GGX and is not a diffuse-only acceptance test. Main now has a narrow squared Cone candidate plus real packed-record GPU tests (translation/rotation and Point/Pyramid/Box controls); actual revalidation and materialSpecularLevel0 comparison are pending. Historical punctual LightSpecularWeight consumption is a separate observed limitation, not fixed by this candidate.

The complete-main suite has14 independently classified later-stage failures (S12/S13/S15/S17), not a green final suite. Those are preserved for their corresponding replay stages. Actual native Inspector interaction remains unverified: CUA still binds Foliage History after public own-Editor focus; no input was sent to that process. Other sessions were neither closed nor changed.

All task bake asset folders were restored and deleted, original FilmParity/meta hashes unchanged. Owned9252 exited normally after Play/capture/resources drained and original GS JSON restored (dirty=false). All70 historical source hashes matched before targeted replay removal and return to main98012a1. Four closed logs were losslessly compressed (~92MiB ->~1.2MiB); source hashes retained. Scratch is still consumed by Spot fix/history/verifier and is not globally cleaned yet. S7-S17 remain locked; PR11 Draft.

## Current squared-Cone revalidation (2026-10-21)

Candidate source is restored on local main98012a1, uncommitted. Historical identity is original834f41de69b2 plus75-file hashed replay; main patch SHA005764ac63d3018ae757443457d313e263453019b7d22457145e60f2d6897605. Angular actual Metal GPU3/3, Shape23/23, Record9/9, Layout2/2, Lifecycle11/11. Full1604 callbacks discovered/started/finished:1551Passed/0Failed/53Skipped,2 reload/resumes; native XML1571 leaves plus33 checkpointed skipped callbacks. Overall Skipped:Ignored, not1604Passed or frameworkComplete. Independent ID audit accepted. DXC67/67; static0/0.

Specular-isolated Baked/Mixed with-caster ROI ratio median1.002581, absolute relative errorP95=6.36075percent; caster shadows differ, cause unproved. NoCaster actual bakes and14 finite/retired buffers show33 advancing frames before producer34; material JSON equal and actual Specular0/roughness1/metallic0. Independent radial analysis accepts inner/outer shape: theoretical radius0.705308/2.309401m; inner r<.6 range0.999802..1.000709 Baked /0.999633..1.000643 Mixed; transition(.8,2.1)m max squared-window error.00115808/.00055021; full-radius including corner.01001784/.00481878. Near-axis normalization removes constant scale; it does not establish absolute intensity. Unnormalized36372 correlated spatial samples: ratio median1.00246870, P05/P95.93791721/1.07204548, absrelP95=8.201891percent. Residual still being investigated; no precise equivalence or post-hoc tolerance.

Evidence under S6/spot-window-fix; NoCaster subdirectory contains bake/frame manifests, raw SHA and radial-analysis.json. Both task bake asset folders removed after transactional restore, original FilmParity/meta unchanged. Owned20790 normal exit: GS JSON equal/dirtyfalse, no active tests/Play/bake/readback. Main restored by exact hash-checked replay removal. Native Inspector UI evidence still missing because supported CUA app bindings select another project's Editor; no input sent there. S6 GPU before/after timing remains Open. No S6 commit/push; S7-S17 locked; PR11 Draft.

## Absolute intensity closure and pending native UI

Independent verifier and main Agent rerun the same fixed-parameter Lambert analysis with identical JSON SHA036d458b068b2c9758651f0fb71aa45e9d64f4e5c106fa8c7e3a6011c7480058. Mixed actual/theory P05/median/P95=.99855353/.99930877/.99993139; native atlas integral/theory=.99807141; normal Mixed/Baked integral ratio1.00197687 at radius2.65m. No brightness fit. Native atlas centroid(71.509991,71.524850) differs from original ST world-origin index(72,72), around half texel. BakedDiffuse matches native map sampled with original ST (absrelP95~.052percent); diagnostic centroid shift explains residual without changing UV/intensity. Spot intensity scale subgate accepted; original8.201891percent spatial residual and caster-shadow difference retained for S12 investigation, upstream cause not established.

Evidence script/JSON/checkpoint are under spot-window-fix/no-caster, total28833B. UI retry own25925 on localmain creates only the unsaved task scene/light selected in Inspector. Computer Use exact path still binds another project; no input sent there. One consolidated direct user native-UI check is pending. Do not close the task scene until that response or explicit cancellation; then run NativeUiHarness.Cleanup and restore original scene/selection. S6 remains uncommitted; S7-S17 locked, PR11 Draft.

## User-scene restart and schema audit (2026-10-21)

User rejects further manual UI handoff and asks to restart into their own scene. Prior25925 had already exited normally. Official CLI-openfile starts32233 directly into original Scene_Spazon; CUA binds correctly. Startup actually fails on missing packaged ProbeVolumesOptions. Explicit Append adds it and targeted save preserves14 current component JSON/local IDs and second-save bytes. Native binary audit gives15->16objects, newAPV3810187332190063959; old IDs/order preserved.34 semantic deltas include19 current non-overriding parameters and14 retired non-overriding SSR/SSGI parameters plus component count. All other native fields/references unchanged; independently accepted as separate later main Profile candidate, excluded from FixS6.

Eight current ResourcePath fields are absent from original native GS; explicit assignment/save creates them and retires S7 zBinningShader. Read-only v23 mhtt adapter + full native type-tree audit confirms exactly9 semantic differences, all other fields/RIDs/PPtr identities unchanged. Old zBinning/newlightTileMask GUID0a57574d950fac64c9465adeb0908015/pathID7200000 are identical; source replacement e6155f32fbb6. Independent verifier accepts this bounded current-main schema upgrade; GS meta unchanged, preimagecabd1631 retained. CurrentGSf22a4f11 is an outer Example modification, excluded from package commits.

Actual Spazon Game renders. Real UI SourceRadius .025->.85, keyboardUndo .025 andRedo .85 verified by full native/Additional JSON; independent accepted. Type menu lists5 options, but AX/coordinate/key/continuous/drag input has not produced selection; no Inspector implementation defect or workaround asserted. No later stage unlocked.

Temporary disabled/DontSave task light in same Spazon removed; cleanup honestly records dirtytrue. Original9roots/scene+meta SHA checked, same source Spazon reopened without save; final state one scene/cleantrue/noPlay,compile,bake/25shader slots/0tasklights. Independent accepts source restoration. Fresh log mark after retained failures:562bytes/0Exceptions, shader or C#errors; actual restored-window PNG saved, forced /private/tmp copies immediately collected/deleted. Main source remains uncommitted; separate later asset candidate frozen in main-later-profile-candidate.patch. S6 Type/SpotShape/Mode and gizmo verification still Open; do not ask same manual question again.

Independent closure review after original Spazon reopen: S6 remains BLOCKED solely for the outstanding native Type/SpotShape/Mode selection and associated readback/Undo evidence. The verifier found no documented, untried input route that currently closes this gap. Do not repeat manual user requests, substitute setters, or mark S7 unlocked. Original Spazon remains clean and open. Ended PID 25925 log was losslessly compressed after confirming no writer; decompressed SHA-256 verified. S6 scratch is now 149.71 MiB logical / 457.25 MiB allocated; current log and evidence still have named consumers.

Native UI closure supersedes the preceding blocked status: original Example Inspector was moved to the screen center; AX menu actions then succeeded. Full JSON receipts cover all Type values, Cone/Pyramid/Box, Disk radius edits and Type/radius/mode Undo/Redo, plus all three baking modes. Screenshot spazon-disk-native-closure.png SHA256 0c333d086b222c303a623fddaf362e26aa09a9ff24d1039ec84ba6c1ce47fbf3 shows the actual circular Disk handle and Baked Inspector. Readback uses the exact current helper assembly; the first ambiguous helper failure is retained and not counted. An optional legacy Disc setup-only follow-up is not counted as native mode-menu evidence. This round temporary light and its Undo were removed; original source/meta hashes and nine roots checked, same Spazon restored clean without saving; final state PID32233 has no Play/test/bake/compile/task light. Independent verifier accepted the native UI gate and is reviewing the immutable 17-file S6 manifest. Production/test source matches previously verified candidate hashes exactly. Static check 0 errors/0 warnings and diff check clean. Profile asset and its later-stage doc hunks remain excluded from Fix S06.

Final S6 batch independently accepted and pushed: 5b34e14eecfdc1c250a553b6d9c315fe143f8a09. Remote PR branch matches; remote main stays c92be7707f45a1789688e5cc16e0845f183b2910. Latest cached diff check covered the previously untracked meta files and removed generator trailing spaces only (YAML tokens and GUID unchanged); static0/0. Owned Editor32233 normally exited after clean-scene/test guard and capture Idle/finishedtrue/outstanding0. S7 replay is next; preserve matched raw inputs for S12.
