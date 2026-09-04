# InfinityRP Full Rendering Closure — Task Source

This file is the only on-disk task source. Cursor Todo tracks execution. Do not inherit completion from older plans.

| Stage | Status | Owner | Test | Evidence |
|-------|--------|-------|------|----------|
| S0 Tooling | completed | Orchestrator | Refresh.sh / Capture.sh / Validation menus | `Logs/baseline/s0-*` |
| S1 R0 Camera/Volume/History | completed | Grok + Luna + Orchestrator gate | EditMode tests + Validation_Volume dump/play | `Logs/baseline/s1-*`, `Logs/volume-stack-dump.txt` |
| S2 R1 Material/GBuffer/Decal | completed | Grok + Luna + Orchestrator gate | Validation_Decal + Spazon | `Logs/baseline/s2-decal-play.png`, `Logs/baseline/s2-spazon-play.png`. Frame tree TODO(UNVERIFIED). MeshDraw+Instanced+Plane stays a known fixture gap; Decal gate used RendererList + InfinityLit. |
| S3 R2 Lights/Shadows | completed | Grok + Orchestrator gate | Validation_LocalLights | `Logs/baseline/s3-local-play.png` (Point/Spot/Rect + ground shadow), `s3-spazon-play.png` (brighter after intensity-once). Dump `s3-local-lights-state-dump.txt`. Frame tree TODO(UNVERIFIED). Luna slug unavailable this session. |
| S4 R3 Pyramids/GTAO | completed | Grok + Orchestrator gate | Spazon play | `Logs/baseline/s4-spazon-play.png`. HiZ 4-mip/batch, ColorPyramid 2-mip/batch, GTAO full chain. Frame tree TODO(UNVERIFIED). |
| S5 R4 Atmosphere/IBL | completed | Grok + Orchestrator gate | Spazon play + profile assign | `Logs/baseline/s5-spazon-play.png`. Shared/View/IBL caches; Profile-only atmosphere. Cache-hit zero dispatch TODO(UNVERIFIED) without framedump. |
| S6 R5 SSR/SSGI | completed | Grok + Orchestrator gate | Validation_Temporal | `Logs/baseline/s6-temporal-play.png` (liveness 17%), `s6-spazon-play.png`. SSR/SSGI denoise + four-mode framedump TODO(UNVERIFIED). Luna slug unavailable. |
| S7 R6 Volume/Translucent | completed | Grok + Orchestrator gate | Validation_Translucent | `Logs/baseline/s7-translucent-play.png` (T0 glass + T2 sphere visible, console 0). Fog/T1 refraction quality + framedump order TODO(UNVERIFIED). |
| S8 R7 Exposure/Output | completed | Grok + Orchestrator gate | Validation_Output | `Logs/baseline/s8-output-play.png`, `Logs/gray-card-mean.txt` mean=0.1608 (ARGBFloat after full RP). sRGB [0.44,0.48] Game-view gate and Metal HDR hardware TODO(UNVERIFIED). SDR skips HDROutputSettings probe. |
| S9 R8 Inspector/Cleanup/Docs | completed | Grok + Orchestrator gate | docs + compile | SessionState inspectors, leftover Generator/Dummy/Compress delete, DESIGN/Delivery-Report. Luna full review unavailable. |
| R0 DebugView infra | completed | Grok + Luna + Orchestrator | EditMode DebugViewStats + Capture Debug Views | `Logs/debug/Validation_Output-*.png` Albedo mean 0.180; `Logs/debug/Scene_Spazon-*.png` + stats; Play FramePairDiff diffRate 25%; Luna review+verifier PASS. Marker ROI oversized P1. Beauty still T1–T5. |
| R1 Default Volume / Output authority | completed | Grok + Luna + Orchestrator | DefaultVolumeOutputTests + Ensure profile + dumps | IdentityLut deleted. `volumeProfile` → SetCustomDefaultProfiles. Gizmo before OT on PostProcessBuffer. Format chain: target→active→editor present→lastKnown; None and HDR-format guess throw. Gray-card Game sRGB luma 0.31 (gate [0.44,0.48] FAIL; linear dump 0.078). TODO(UNVERIFIED) mid-gray. |
| R2 Atmosphere integrity | completed | Grok + Luna + Orchestrator | AtmosphereParameter/Cache tests + SkyView dump | ThrowIfInvalid physical ranges; UpgradeOutOfRangeToEarth; MultiScatter uses sun azimuth; cache Equals miss. Earth fixture: ΔuvDaylight 0.007, L0 r/b 0.55 PASS. zenith/horizon 0.15 (gate [0.2,0.8] FAIL); sun/sky 3.9 (gate >8 FAIL; Hillaire SkyView has no solar disc). Lighting |Co|/|Cg| drop 8%/2% — T3 albedo. Profile already Earth. TODO(UNVERIFIED) sun disc. |
| R3 GBuffer contract | completed | Grok + Luna + Orchestrator | GBufferContractTests menu 5/5 | Color.hlsl YCoCg (not Common). Checkerboard SV_POSITION. BestFit decode normalize. Metal raster→compute albedo <2/255, normal <1°. Spazon Albedo DebugView is beige stone + authored banners, not purple pack. Stone ROI |Co|~0.07 (gate 0.05 FAIL; warm albedo). Full-frame Co 0.38 is banners/sky. |
| R4 Screen-space denoise | completed | Grok + Luna + Orchestrator | ScreenSpaceDenoiseTests + Play dumps | SSR SpatialRadius used. SSGI Spatial fills misses. Temporal TAA-style lerp + 8-frame ramp, no *8. AO owner=Deferred IBL; Composite no *ao. GTAO VolumeHasOverrides. NumRays default 2. Luna P0 PASS. Play dual DebugView inter-frame SSR 0.09 (gate 0.02 FAIL; dump cycles TAA kernel). Lighting highpass not 30% down. TODO(UNVERIFIED) still-frame after T5. |
| R5 TAA + SceneView gating | completed | Grok + Luna + Orchestrator | TemporalAntiAliasingTests + TAAConfidence | 3x3 HistoryDepth reject + gradient pad. Reset 8-frame ramp. Frame gap / new state → historyReset + jitter=0. SceneView recycle 120, Game 8. Preview excluded from new gating. TAAConfidence mean 0.975 (gate >0.9). camera.Render() Game dumps reset history so FramePairDiff 62% is invalid. TODO(UNVERIFIED) Scene drag SSIM. |
| R6 Docs / delivery | completed | Orchestrator + Luna | Luna full review + docs | AGENTS/DESIGN/PLAN synced. Delivery report lists Metal numbers and D3D12/Vulkan recipes. TemporalAntiAliasingGenerator kept (live jitter+dispatch). Luna: zero dual-authority P0. |

## Workflow

1. Update this table and Cursor Todo.
2. Grok (`cursor-grok-4.6-high-fast`) implements.
3. Luna (`gpt-5.6-luna-medium`) reviews; fail loops back to step 2.
4. Record `Logs/Editor.log` byte mark, run `Tools/RefreshUnityEditor.sh`, diagnose only the new window.
5. Play liveness: two captures ≥1s apart with Game-region difference > 0.5%.
6. Dump Frame Debugger via `Infinity/Validation/Dump Frame Debugger`.
7. Luna checks evidence against the stage gate. Fail loops back to step 2 (max 4 cycles).
8. Stage pass: commit and push the Package repo on `main`. Do not commit the example project repo.

## Decisions (locked)

- Package repo only for git commit/push. Assets in InfinityExample may change; do not commit that repo.
- GBuffer A/B Crytek layout + GBufferC for flags/SSS/thickness. Emissive writes LightingBuffer.
- Rect lights: Karis representative + Frostbite shape factor. No LTC LUT.
- Unity Light + `visibleLights` are authoritative. LightComponent holds Infinity extensions only.
- Atmosphere lives only on `AtmosphericalProfile`. Volume does not override it.
- DeferredShading / SSS / TAA are not Volume components.
- Present stays Raster. OutputTransform is the single transfer-encoding owner.
- RTAO/RTGI files stay; they are out of scope.
- Metal is the verification platform. D3D12/Vulkan stay `TODO(UNVERIFIED)`.
