# Decal context recovery — 2026-10-23

Scope: focused Fix S15 follow-up on local main1307adc, Unity6000.6.0f1/CoreRP17.6/ShaderGraph17.6, macOS Metal/AppleM3Max. This is not final remaining-acceptance closure.

Enabled DecalComponents register once through pending FGraphics tasks. Replacing the RP instance creates a fresh RenderContext with count zero, while the components stay enabled/registered. DBuffer feature gating then drops all three visible projectors. The original normal-frame graph contains no RenderDBuffer.

The repair reconciles enabled decals in valid, loaded scenes once after the new context's first task drain. Later enable/disable events retain their existing counter ownership. Public APIs and serialized assets are unchanged. The runtime never discovers components per frame.

## Verification

- Unity compilation: zero errors/warnings. StaticCheck: zero errors/warnings.
- Final lifecycle XML v4:5 discovered/executed,5Passed,0Failed,0Skipped. Related Decal suite:8discovered/executed,8Passed,0Failed,0Skipped.
- Fixed normal-frame graph restores RenderDBuffer and all three colored overlays without reopening the scene or toggling the components after RP replacement.
- Disable removes overlays; re-enable with a second real camera rendering an owned128x80target restores them. Fixed ROI has0/27000 colored pixels disabled and17363/27000 enabled. Both captures are finite,Completed,1captured,0outstanding; six buffer readbacks retire.
- Five key JPGs and lossless compressed raw/graph/JSON evidence are retained. Raw images use Metal texture row order; JPGs apply the recorded Present Y flip and Rec709 linear-to-sRGB transfer for inspection.
- Independent verifier approved source, executed tests, actual frames, resource retirement and restoration. Before delivery,427protected source paths match their current input SHA. Original clean nine-root Spazon, empty selection,Playoff,bakeoff and existing EditorBuildSettings dirty state are restored. Owned secondary camera/RT and memory-only RP/Profile are removed.

## Separate configuration finding

The finite plane ends below the physical horizon. A downward ray beyond it has far depth0; with planetary ground disabled it gathers very little atmospheric radiance, which grading maps to black. Ground-on task-clone controls fill this band and retain decals. At rawpixel(320,350), ground-off reconstructed directionY=-0.080725, LightingRGB approximately(.000191,.000397,.000866),Display0; ground-on LightingRGB approximately(.202515,.182251,.154053),Display approximately(.198120,.177979,.148193). This explains the Decal band; it is not an atmosphere algorithm repair and user Profile defaults are unchanged. Fixed camera view/projection match, but Halton phases and DepthSHA differ; these are not byte-identical paired-depth frames.

## Preserved unsuccessful attempts

The first regression run could not open fixture scenes before explicit import; the subsequent eight-case run is7Pass/1Fail(Shadows outliers0.703906% vs0.5%limit). That failure remains open, old baselines unchanged.

Diagnostic captures cancelled by lazy pipeline disposal have0accepted captures and do not count. The corrected method drives an ordinary Game repaint and verifies actual loaded clone identity before beginning readbacks. A proposed direct original-RP binding experiment was rejected by automatic review and was not executed; all actual A/B controls use task-owned memory clones.

Initial lifecycle tests with HideAndDontSave objects outside the loaded-scene contract yielded1Pass/3Fail; ordinary loaded objects correct the test setup without relaxing assertions. The first unload test yielded4Pass/1Fail because UTF's untitled bootstrap forbids additive scene creation. A public owned PreviewScene supplies a valid loaded scene and real unload without saving the bootstrap. Test disposal also preserves preexisting Editor tasks and dirty-mesh references. Original failed XML remains archived; no tests were skipped or deleted to obtain success.

## Remaining work

Persistent picture review outside this bounded Decal control, Shadows regression, six representative valid performance comparisons, three cold starts, current package full suite/eight final regressions, revised S1 final evidence, final source restoration and complete task cleanup remain under PLAN.md. PR11 readiness is not established by this focused repair.
