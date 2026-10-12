# `particlequery-stock.tsv.gz`: provenance

Stock `CParticleSystemQuery` (particlesystemquery.cpp, the fork's game/shared
file at its SOURCESHARP-edited state), as particles.a called it, recorded live
before the answers moved to C#. `ParticleSystemQueryOracleTests` replays it
against `SourceSharp.Client.Effects.ParticleSystemQuery`, bit for bit.

## How it was recorded

1. Base `a9d8f9fd` (client-novalve), in a scratch worktree, with
   `tools/probes/client/c5particles_probes_debug.cpp` (the recorder, from lane
   c5-particles) added to the probe sources and the client built with
   `-DSS_CLIENT_PROBES=ON`. The recorder wraps `g_pParticleSystemQuery` at
   static init, before `CParticleMgr::Init` hands it to the library, and
   forwards every call to the stock object.
2. `tools/probes/c0-session OUT tools/probes/sessions/c5p-particlequery.steps
   --env SOURCESHARP_C5P_FIXTURE_DIR=OUT --port 27646`: dm_lockdown, then the
   C0 sweep (`cs_cl_probe_particle_sweep`: each of the 245 loaded definitions
   started in turn in front of the view for 0.8 s) while the player is moved
   through twenty `setpos` points, so the lighting and trace answers are taken
   at many places rather than one.
3. The recorder takes a mutex around its rows and its own second
   `GetLightForPoint`: the library simulates on several threads
   (`r_threaded_particles`), and without it two of three sweeps crashed or
   hung at the first lit system. Stock serialises its own lighting call the
   same way (`s_LightMutex`). The sweep's own `particles_new.cpp (174)
   m_RefCount > 0` asserts (two, on definitions that fail to create) are the
   C0 sweep's and appear without the recorder too.

## Rows

| tag | columns | what |
|---|---|---|
| `cg` | name, result, monotonic ns | `GetCollisionGroupFromName`; 28 rows, 14 before `client-dll Init exit` (the Init-time definition read, before the managed runtime exists) and 14 at the first level's full re-read |
| `light` | origin xyz, IsInGame, the engine's `GetLightForPoint( origin, true )` xyz, `\|`, r g b a | `GetLightingAtPoint`; 82 rows, 11 distinct tints |
| `trace` | start xyz, end xyz, mask, ignore != NULL, group, IsInGame, `\|`, startpos xyz, endpos xyz, normal xyz, dist, type, signbits, fraction, contents, dispFlags, allsolid, startsolid | `TraceLine`; 3,928 rows, all in game, mask 0x200400b, no ignore entity, group DEBRIS |
| `rp` | cp, n, scale, tries, bias xyz, object bound, cp position xyz, index wanted, relative wanted, `\|`, per point: xyz [index] [relative xyz] | `GetRandomPointsOnControllingObjectHitBox`; 420 rows |
| `hbi` | cp, buffer size, object bound, `\|`, result | `GetControllingObjectHitBoxInfo`; 698 rows |
| `never` | method | any of the six C0 measured as never called; 0 rows |

Not recorded, because the sweep runs in a level: the out-of-game arms
(white light, no trace). The facts for those read the stock source.
