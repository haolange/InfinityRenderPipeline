# InfinityRP Full Rendering Closure — Task Source

This file is the only on-disk task source. Cursor Todo tracks execution. Do not inherit completion from older plans.

| Stage | Status | Owner | Test | Evidence |
|-------|--------|-------|------|----------|
| S0 Tooling | completed | Orchestrator | Refresh.sh / Capture.sh / Validation menus | `Logs/baseline/s0-*` |
| S1 R0 Camera/Volume/History | completed | Grok + Luna + Orchestrator gate | EditMode tests + Validation_Volume dump/play | `Logs/baseline/s1-*`, `Logs/volume-stack-dump.txt` |
| S2 R1 Material/GBuffer/Decal | completed | Grok + Luna + Orchestrator gate | Validation_Decal + Spazon | `Logs/baseline/s2-decal-play.png`, `Logs/baseline/s2-spazon-play.png`. Frame tree TODO(UNVERIFIED). MeshDraw+Instanced+Plane stays a known fixture gap; Decal gate used RendererList + InfinityLit. |
| S3 R2 Lights/Shadows | completed | Grok + Orchestrator gate | Validation_LocalLights | `Logs/baseline/s3-local-play.png` (Point/Spot/Rect + ground shadow), `s3-spazon-play.png` (brighter after intensity-once). Dump `s3-local-lights-state-dump.txt`. Frame tree TODO(UNVERIFIED). Luna slug unavailable this session. |
| S4 R3 Pyramids/GTAO | pending | Grok / Luna | framedump mip batches + GTAO still | framedump, screenshots |
| S5 R4 Atmosphere/IBL | pending | Grok / Luna | cache-hit zero dispatch | framedump, screenshots |
| S6 R5 SSR/SSGI | pending | Grok / Luna | Validation_Temporal | framedump modes, screenshots |
| S7 R6 Volume/Translucent | pending | Grok / Luna | Validation_Translucent | framedump order, screenshots |
| S8 R7 Exposure/Output | pending | Grok / Luna | Validation_Output gray card + Metal HDR | screenshots, framedump |
| S9 R8 Inspector/Cleanup/Docs | pending | Grok / Luna | Luna full review + all scenes | Delivery report |

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
