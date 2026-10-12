# `proxies-stock.tsv.gz`: where it came from

Stock's material proxies (plan_client_novalve.md §2 cluster 1: the 24
`*proxy.cpp`/`mathproxy`/`cl_mat_stub` TUs, and `shell_materialproxies.cpp`'s
four), recorded off a live `dm_lockdown` before lane c5-proxies moved every
`OnBind` to C#. `MaterialProxyOracleTests` replays it.

Recorded at `client-c5-proxies` `a9d8f9fd` (+ the probe), debug, through
`tools/probes/client/c5proxies_probes_debug.cpp` (`-DSS_CLIENT_PROBES=ON`)
driven by `tools/probes/sessions/c5-proxies.steps`. No stock TU is patched:
the probe swaps each `*_IMaterialProxy003` registration's create function for
one that wraps the stock proxy, and logs

- at `Init`: the proxy's KeyValues block, flattened RAW (each key's type and
  the value the non-converting accessor reads: `KeyValues::GetString`
  rewrites a FLOAT or INT key as a STRING in place, so the probe must not ask
  it before stock's Init does; for a STRING key, also what `sscanf("%f")`,
  `atof` and `atoi` read from it),
  every `FindVar` stock's Init made (name, complain, found, the var's name),
  and Init's answer;
- at each `OnBind`: the inputs (curtime, frametime, the bind argument resolved
  as stock resolves it, the view origin, the water LOD params, the world's
  bounds), the state of every var Init found, each `GetTextureValue` stock
  made, each draw from the engine's uniform stream (`random` and
  `randomgaussian`'s), and every `IMaterialVar` setter stock called with its
  arguments (the vars' vtables swapped for logging copies for the call only).

Where dm_lockdown binds a proxy itself (Sine, the first 400 binds) those are
stock's own binds. Every other proxy name is bound by the probe scene
(`cs_cl_probe_pxscene 300`), once a frame from inside a bind the renderer made
(so `CurrentViewOrigin` answers): 22 real content materials (the shipped
VPKs' `.vmt` files that carry each proxy: `effects/combineshield/*`,
`dev/dev_tvmonitor1a`, `dev/ocean`, `sprites/lamphalo`, `effects/ar2_altfire1`,
`decals/playerlogo01`, ...) and 20 synthetic ones (`materials->CreateMaterial`
with a Proxies block) that carry every registered proxy name over float, int,
vec2/3/4, matrix and texture vars, their sources swept before each bind, with
NULL, the local player and one other entity (or player indices -1..maxclients+1
for `PlayerLogo`) as the bind argument. `Health` cannot be asked for by a
material in this process (the KeyValues symbol table spells the block
`health`, which the case-sensitive registry does not answer) so the scene makes
two directly through the wrapped registration.

## The rows

| tag | fields |
|---|---|
| `px-init` | id, proxy name, material |
| `px-key` | id, key, KeyValues type (1 string, 2 int, 3 float), the string (STRING only), the float (FLOAT only), the int (INT only), sscanf count and value, atof, atoi (STRING only) |
| `px-findvar` | id, name asked, complain, found, the var's name, whether stock passed a found pointer |
| `px-initok` | id, Init's answer |
| `px-bind` | id, name, n, curtime, frametime, arg kind (0 null, 1 index, 2 renderable), player index, maxclients, the player's customFiles[0], entindex (-1 not an entity, -2 none), abs origin, render alpha (-1 not an entity), is-NPC, view origin, water LOD start/end, world present, world mins, world maxs |
| `px-before` | id, var, type, vector size, GetIntValue, GetFloatValue, GetVecValue (4), texture frames, texture name, the matrix (16, matrix vars only) |
| `px-get` | id, var, `texture`, the texture's name, its frames |
| `px-rnd` | id, `f`, lo, hi, the draw |
| `px-set` | id, var, the setter (`float`, `int`, `vecn`, `vec3`, `veccomp`, `matrix`, `texture`, ...), its arguments |
| `px-iset` | a set the material system made inside a getter (GetTextureValue loading a string var's texture); the gets it makes inside one (`px-iget`) are left out |
| `px-end` | id |

43 proxy names × 400 binds = 17,200 binds; 12,532 setter calls; 898 draws.
`Camo` never binds: stock's Init answers false before it reads anything.

What the scene cannot reach on dm_lockdown, said here so the facts that cover
it offline are not mistaken for recorded ones: every entity's render alpha is
255 and none carries FL_NPC (HL2MP has no NPCs and no managed `Alpha` input),
and no player has a spray (`customFiles[0]` is 0), so PlayerLogo's texture arms
are not in the recording.

## How to record it again

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON && ninja -C farm/debug/build client_sourcesharp
    tools/probes/c0-session OUT tools/probes/sessions/c5-proxies.steps --config debug
    tools/probes/px-select OUT/probe.log src/test/client/Fixtures/proxies-stock.tsv.gz --title "..."
    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=OFF && make debug

The probe needs the stock proxy TUs in the build (and the old shell's
materialproxy_facts.h): the recipe works at d63a2672 .. 7a4c554e, before the
commit that evicted them, which also took the probe off the SS_CLIENT_PROBES
list.
