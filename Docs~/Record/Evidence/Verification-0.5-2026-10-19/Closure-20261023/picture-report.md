# Current picture controls — 2026-10-23

These controls use normal submitted Game frames in the original InfinityExample, Unity 6000.6.0f1/macOS Metal. User scenes and profiles were not saved. The original eight-case failure is retained separately from later successful controls.

- Decal, LocalLights and Translucent horizon bands correspond to finite fixture floors and disabled planetary ground. Task-only atmosphere clones with ground enabled fill the bands while preserving geometry and lighting. No atmosphere algorithm repair is claimed. Unjittered camera view/projection match, but Halton phases/depth hashes differ; these are not pixel-identical depth pairs.
- LocalLights' small dark floor mark is the liveness cube's real shadow. Changing only that task marker's shadow casting removes 64 dark floor pixels (minimum luminance 0.0188900→0.1196900). Its original shadow state was restored. This is not a light-loop defect.
- The initial Shadows comparison failed with 0.703906% outliers against a 0.5% limit. It used accepted render submissions while Unity game time/frame count were frozen. Contact-shadow noise depends on that frame count. A genuine advancing-Play retry passes the unchanged baseline and tolerances: RMSE 0.005385155 (<0.01), outliers 0.1566406% (<0.5%), nonfinite 0; captured frame 92 with game time 9.004. No shadow shader or tolerance was changed.

Independent verifier reviewed the actual raw frames, source consumers and state restoration before their retirement. No arbitrary large triangles/blocks were visible in these reviewed controls. This bounded conclusion does not replace the remaining probe, cold-start, final eight-case or performance gates.

See `picture-controls-manifest.json` for the 116-member lossless archive and SHA identities. `picture-retirement.json` records the exact consumed paths and allocation lower bound before verified deletion. Protected 427 source hashes remain unchanged. Original Spazon (nine clean roots), empty selection and the preexisting EditorBuildSettings dirty state were restored.
