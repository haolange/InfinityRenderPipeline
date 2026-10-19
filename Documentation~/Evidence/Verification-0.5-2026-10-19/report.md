# Local 0.5 verification — 2026-10-19

The original39-commit0.5 range is frozen in stage-map.json. Local main was fast-forwarded from c92be77 to eab18e5. Remote main was not changed.

## S0

- Original endpoint: d2ca231b17263779314df955362b95650cc8dc99. Original full static result: FAILED,51 errors (original output retained).
- Corrected historical replay:10/10 offline regression tests,0 errors and8 individually reported historical-link warnings. Independent bounded offline-tool PASS.
- Main integration:10/10 offline regression tests,0 errors and0 warnings without a historical receipt. Independent bounded integration PASS.
- Negative controls retain failures for real orphan metadata, duplicate GUIDs, unknown live LightModes, unlisted broken links, changed source hashes and out-of-repository sources.
- Historical missing links remain unresolved and are not image evidence. Exact source hashes and targets are in S0-historical-links.json.
- Unity compile / EditMode / image / performance: not accepted by this tool-only gate; the playbook requires no Editor check for S0.

## Editor availability and remaining stages

Unity6000.6.0f1 on the original ExFAT Example aborts at native startup with `std::invalid_argument: stoull: no conversion`. The actual held launch reproduced the signature. Exact AppleDouble UDS sidecar isolation and provenance removal did not give durable recovery. No real database contents were deleted; the438 original source inputs still match the prelaunch hashes.

S1–S17: not accepted. Unity compile, targeted/full EditMode, frame inspection, image baselines/comparison and GPU performance remain pending. No Unverified-0.5 category or FeatureStatus image/performance state was cleared. PR11 remains Draft and is not ready for review.

The run retains targeted preimages and source/evidence identities under InfinityExample/intermediate/verification-0.5/20261019-run01 for the next Editor recovery and staged verification consumer. This is a checkpoint, not completion of the approved full plan.
