# Rendering Debugger

Infinity registers panels:

- **Rendering** — Debug View enum (this is the only DebugView owner)
- **Lighting** — light counts, UnifiedRayTracing backend, Volume selection rule
- **Mesh** — MeshScene matrix duplicate ratio
- **Temporal** — native motion A/B flag, TAA sharpen fusion flag

DebugView is never serialized on the RP Asset.
