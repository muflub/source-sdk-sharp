# `sphere-stock.tsv.gz`: provenance

Stock `CParticleSphereRenderer`'s lighting (particlesphererenderer.h:
`RenderParticle`'s base colour, `AddLightColor` for the ambient light and,
unless the material sets `$USINGPIXELSHADER`, the directional one, then
`ClampColor`), as shell_managed_particles.cpp drew the smoke stack's quads
through it before the lighting moved to C#. `SphereLightingOracleTests`
replays it against `SourceSharp.Client.Effects.SphereLighting`, bit for bit.

## How it was recorded

1. Base `a9d8f9fd` (client-novalve), in a scratch worktree, with
   `tools/probes/client/shell_managed_particles.cpp.probe.diff` applied to a
   probe copy of `shell_managed_particles.cpp`
   (`ss_client_probe_copy_ours` in probe_copies.cmake) and the client built
   with `-DSS_CLIENT_PROBES=ON`. Before each lit quad the probe reads the
   renderer's own state (through explicit-instantiation access to its
   private members) and runs stock's own `AddLightColor` and `ClampColor` on
   it, in `RenderParticle`'s order.
2. `tools/probes/c0-session OUT tools/probes/sessions/c5p-sphere.steps
   --arg=+map --arg=dm_lockdown --port 27646`: two env_smokestacks, each lit
   by an ambient and a directional env_particlelight naming it; the first's
   material (particle/SmokeStack.vmt) sets `$USINGPIXELSHADER`, the second's
   does not. 2,500 quads per arm.

## Rows (`sphere`, tab-separated, floats %.9g)

base colour rgb (0..1), ambient position xyz, colour rgb, intensity,
directional position xyz, colour rgb, intensity, using pixel shaders, the
particle's world position xyz, alpha (0..255), `|`, the lit colour rgb
(0..255), the bytes `RenderParticle_Color255SizeNormalAngle` rounds them to,
and the alpha byte (-1: under 0.5, not drawn).
