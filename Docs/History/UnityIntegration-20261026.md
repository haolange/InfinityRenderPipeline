# Unity integration boundary acceptance — 2026-10-26

Base: main `4156fcad28ad13a7e41fda2ac37f6b81ce11c733`, preserving the preceding local cache repair and 13 unrelated resource edits. No branch, commit, push, Player build or shutdown. The main agent owned design, implementation, review and Unity operations; Luna agents searched source only.

## Changes

Native rendering retains Unity culling, RendererList/ShadowRendererList, per-object Lightmap/Directional/Shadowmask, legacy Probe/LPPV and motion inputs. Infinity retains its own RenderGraph, pool, Mesh scene, CPU/GPU backends, visibility and transform histories. Custom Mesh no longer discovers or disables a Renderer and has no Lightmap or CPU per-object Probe data. Its shader retains direct world-position APV sampling, atmosphere/IBL and realtime lighting. Removed data, command splits, bindings, keywords, interpolation, notifications and cache interfaces have no compatibility replacement.

Visible native `Light.bakingOutput` determines Shadowmask requirements. URT's public explicit-instance adapter distinguishes native renderer identity/custom draw generation from Mesh asset identity, independently detects native membership/transforms and accepts builds after successful frame submission. It remains separate from raster collection. This adds no native Skinned/Terrain RT feature; native raster support is retained.

Entity registration pairs disable/destroy once. APV cleanup belongs to the integration that initialized CoreRP. Custom terrain sector authoring is explicit and uses Unity's Inspector/Undo/Prefab APIs and native TerrainData; scene saves do not rebuild it. Disable/re-enable retains authored sectors while releasing/recreating runtime storage. Refresh tools use explicit-project Unity CLI/public AssetDatabase rather than synthetic global keyboard input. Capture tools require the selected process and do not fall back to another session. macOS Swift module caches are removed on exit.

The [audit](../UnityIntegrationAudit.md) and [file inventory](../UnityBoundaryInventory.csv) cover source, shaders, Editor, tools, tests, assets/import chains, assemblies, hidden configuration and current/historical documentation. Inventory hashes identify the reviewed candidate; generating the CSV alone is not an inspection or runtime gate.

## Evidence

Unity 6000.6.0f1 / CoreRP and Shader Graph 17.6 / Apple M3 Max / Metal. One task-owned full Example mirror and isolated Library were used; no source Editor was controlled. [Compact numeric receipt](UnityIntegration-20261026.json) and [matched-camera preview](UnityIntegration-20261026.jpg) retain the useful results.

| Gate | Actual result |
|---|---|
| Static | 0 errors / 0 warnings; 14 negative checks pass; diff whitespace and shell syntax pass. |
| Full EditMode discovery | 822 started/finished: initially 816 passed, 5 failed, 1 skipped. Four failures expected the removed Renderer takeover; one exposed inactive native roots in URT collection, which was fixed. |
| Final affected tests | Registration 6/6, RT input/submission 3/3, pipeline-state lifecycle 11/11, Terrain authoring 2/2, baked interfaces 8/8. The five original failures are closed; the new Terrain tests pass. Across these traceable runs: 823 unique passing cases, one real cross-queue GPU skip. This is not a claim of a second full 824-case execution. Empty-filter/old-DLL attempts are excluded. |
| Compilation | Final scripts compile; 25 loaded production ComputeShaders and 25 loaded surface Shaders return no ShaderUtil messages. Instanced surface no-APV/L1/L2 variants were warmed. Three actual generated/imported Graphs have no diagnostics. |
| Full Scene_Spazon | MainCamera 1666×937, quality/Volume snapshots, pose, matrices and jitter matched. Normal present, postprocessing and accepted unsharpened TAA history confirmed. Lighting, DisplayColor, BakedDiffuse, BakedOcclusion and four-cascade depth readbacks are finite; CSM depth is byte-equivalent. SSR, SSGI, two-stage visibility, temporal and post consumers remain live in the compiled graph. |
| Color comparison | RGBA linear Display MAE 0.00096055; Lighting MAE 0.00096934. Baked buffers intentionally change on custom content after removing its SH/Shadowmask input. This is a matched configuration/visual regression check, not a pixel-equivalence or quality-improvement claim. |
| Native bake/reopen | Tiny owned scene actually baked with public Lightmapping.BakeAsync. Reopen retains one color/direction/Shadowmask set, native Renderer index 0, Mixed/Shadowmask bake output with channel 0. Source Scene_Spazon was not rebaked. |
| APV | Actual CoreRP bake completes and loads; runtime `_EnableProbeVolumes=1`. Independent Mesh has no Renderer; its projected 5×5 BakedDiffuse ROI averages (0.0931128, 0.0931128, 0.0931128). L1 is runtime-tested; L2 receives shader warmup only. |
| Graph draws | Complete 32-event supplement tree confirms generated Lit SRPBatcher depth/GBuffer/motion/shadow/Forward, Unlit T2 and Decal DBuffer draws; custom depth/GBuffer/motion are separate direct Mesh draws. |
| Native raster supplement | SkinnedMeshRenderer and native Terrain, with no custom components, render through normal SRP cameras into 128×128 targets. On/off center RGB means: skinned 0.0488299 / 0; Terrain 0.493327 / 0. All sampled values finite. |
| RT | Actual URT Compute RTAO dispatch and finite AO output [0,1]. Hardware DispatchRays was not executed. Native/custom sharing one Mesh, transform changes without a MeshScene revision, disabled/inactive inputs and failed build acceptance have passing contract tests. |
| GC | Each path: 32 warmup + 120 samples. Frozen full scene: frame/proxy/native Submit allocation counts all 0. Original dynamic scene: proxy 0; frame 4 and native Submit 4 per sample, leaving 0 in the measured controllable library portion. These are allocation counts, not byte estimates or GPU timings. |
| Reload and restoration | Play transitions with Domain Reload disabled and enabled execute normal frames. Original Scene_Spazon reopens clean, with 11 map sets and 279 native probes. Play, pause and Frame Debugger return off. |
| Tools | Explicit-project macOS refresh executed successfully. Shell syntax passes. Windows scripts and OS window-capture wrappers were not runtime-tested this round. |

The supplemental full-scene Frame Debugger export timed out on a native event payload. It restored state and is not accepted as a complete tree; the separate complete Graph/APV tree and native on/off GPU probes provide the actual draw evidence. Low-sample RT noise and the already documented fixture sky-band limitation are not new quality claims. Compiler warmup/CLI main-thread timeouts were followed through to completion and current diagnostics; a timeout itself was never treated as acceptance. No isolated GPU speedup is claimed.

## Authorized source migration

Only `Assets/Scene/Spazon/Scene_Spazon.unity` was saved outside the package, after explicit approval. Eight null `m_BakedLightingSource` fields were removed. Native GlobalObjectIds/references and 18,716 retained serialized/public-setting properties across 554 inspected objects match before/after, including native bake settings and map references. Save, reopen, second-save byte identity and no-op checks pass.

Original SHA256: `09027b2d1ad93cae8d97a6ce3bc8f907b2214d24da6210bb08fe908033cd9a1e`.
Migrated SHA256: `c66f1916ba538afdd2047b9fdc389271048c11116908961d2ec077f811229e57`.
The scene meta is unchanged. The 13 pre-existing package resource edits remain byte-identical to their task-start hashes. The earlier destroyed-wrapper cache fix is superseded by removal of that owner; its historical receipt is retained.

## Platform and cleanup boundaries

Metal Editor acceptance above is bounded to the exercised content. D3D12/Vulkan, actual independent cross-queue execution and native Submit failure injection remain unverified. Existing Player RTAO loading remains open; no Player was built. This round does not re-certify HDR swapchains or every APV streaming/scenario configuration. It retains the preceding HDR receipt rather than inferring a new one.

Cleanup completed: owned Editor PID 9896 and CLI port 7800 are closed; tests/captures/readbacks drained. Clean Scene_Spazon, 11 map sets, 279 probes and non-Play/non-paused state were restored first. Exact task root `intermediate/unity-boundary-20261026` and cache `/Users/gushihao/Library/Caches/InfinityRP-Validation/unity-boundary-20261026` are absent. Mirror, independent Library, generated bake/graphs, raw captures, temporary code/logs and rollback bytes were removed. Only the audit, inventory and compact receipt/preview remain. Source Library, source resources and other sessions were preserved.
