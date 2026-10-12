# The ConVar ordering experiment

`plan_shim.md` Phase 5 names one thing that has to be settled by running the
game rather than by reading it:

> managed `ConVarRegistry.RegisterAll` must run **before** the engine's first
> `ConVarRef` for any engine-resolved name. If it does not, the name resolves
> empty and the failure is silent.

Run in lane 5d-convarledger for the fourteen declarations Phase 5 moved out of
C++. The harness is `.5dlane/ordering-experiment.sh` in that lane's worktree;
what it does and what it found are below, because the script is scratch and this
is not.

## Why it is a real risk

A native `ConVar` is constructed at DLL load. A managed `[ConVar]` is registered
by `ConVarRegistry.RegisterAll` during managed startup, inside `DLLInit`, which
is later. A moved name therefore appears in the engine's registry **later than
it used to**, and `ConVarRef` resolves once, in its constructor, and caches the
answer — **including a miss**. Anything that resolves the name in that window
answers 0 / 0.0 / "" for the rest of the process, with no further complaint.

A knob that reads as zero looks exactly like a knob somebody set to zero.

## What a wrong ordering would look like — written down before the run

1. **Unknown.** `./debug-api convar sv_backspeed` reports no such convar: the
   registration never happened, or not into the registry the engine is asking.
2. **Present but wrong.** It resolves and reads something other than its
   declared default with nothing having set it — a half-constructed or refused
   registration.
3. **Refused link.** `game/mod_sharp/console.log` carries *"For
   FCVAR_REPLICATED, ConVar must be defined in client and game .dlls"* naming
   one of the fourteen. That is the engine refusing to link the
   client half to the server half; the client's copy then sits at its own
   default forever and never tracks the server. This is the defect
   `docs/defect-convar-order.md` diagnosed, and five of these were printed on
   every run before the DLL-identity flag landed at
   `sourcesharp_gamesystem.cpp:699,701`.

## The two controls

A reading of "it resolved" means nothing unless the instrument is capable of
saying "it did not".

| | name | must |
|---|---|---|
| positive | `sv_gravity` — still declared natively | resolve |
| negative | `ss_5d_no_such_convar` — declared nowhere at all | **not** resolve |

## What happened

Launched `dm_lockdown` headless, with this lane's own socket directory rather
than the machine-wide default (`DEBUG_API_DIR=/tmp/ss-5d`), and read
each name back out of the running game's own registry with `./debug-api convar`.

```
--- POSITIVE (sv_gravity, still native)
    {"name":"sv_gravity","value":"600.000000"}
--- NEGATIVE (ss_5d_no_such_convar, declared nowhere)
    {"name":"ss_5d_no_such_convar","value":"(no convar of that name; a command has no value to read)"}

sv_backspeed               want=0.6    value "0.6"
sv_footsteps               want=1      value "1"
sv_wateraccelerate         want=10     value "10"
sv_waterdist               want=12     value "12"
r_VehicleViewDampen        want=1      value "1"
r_JeepViewDampenFreq       want=7.0    value "7.0"
r_JeepViewDampenDamp       want=1.0    value "1.0"
r_JeepViewZHeight          want=10.0   value "10.0"
r_AirboatViewDampenFreq    want=7.0    value "7.0"
r_AirboatViewDampenDamp    want=1.0    value "1.0"
r_AirboatViewZHeight       want=0.0    value "0.0"
phys_pushscale             want=1      value "1"
sv_soundemitter_trace      want=0      value "0"
english                    want=1      value "1"

failure mode 3, "For FCVAR_REPLICATED" in console.log:  none
"engine refused ConVar" in console.log:                 none
```

Fourteen of fourteen resolve, each carrying exactly its declared default,
including the spelling: `"7.0"` and `"1.0"` come back as written rather than
normalised, which is a second, incidental confirmation that the value the engine
holds is the one the managed attribute declared and not something reconstructed.

Both controls discriminated: the positive resolved, the negative did not. Had
the negative answered, every green reading above would have been the instrument
being agreeable rather than the convar being there.

## What this settles, and what it does not

**Settled.** For a ConVar with **no native reader**, `RegisterAll` runs early
enough. The name is in the engine's registry by the time a map is up, it carries
its declared default, and the client/server link is made — the replicated pair
warning is absent for all thirteen replicated ones, where five such warnings were
printed on every run before the DLL-flag fix.

**Not settled, and it is the harder half.** This says nothing about a name whose
**native reader** resolves it *earlier than managed startup*. That is the case
for the eighteen movevars and every other `reader-owed` row in
`docs/convar-ledger.tsv`, and it is why those rows are blocked on converting
their readers to `CManagedConVarRef` (`managed_convar_ref.h`) rather than on
anything about the declaration. `CManagedConVarRef` exists precisely because
`ConVarRef` caches its miss: it retries while unresolved and becomes a plain
cached pointer once it succeeds. The experiment for those is the same one with
the reader's own value read back, and it belongs with the commit that moves them.

**Also not settled.** Whether `english`, now `FCVAR_USERINFO` from managed code,
actually reaches a *server* in a client's userinfo string. That needs a two-
process join, not a headless single instance. What is shown here is that the
declaration exists with the flag; `movevars_shared.cpp`'s own banner records an
earlier two-process measurement establishing that a managed replicated
declaration does reach a remote client, which is the same mechanism in the other
direction.

## Re-running it

```bash
MAP=dm_lockdown bash .5dlane/ordering-experiment.sh
```

Native output goes to `game/mod_sharp/console.log`, never to `debug-api log`, which
is managed-only — failure mode 3 is only visible in the former. And keep the
socket path short: a unix domain socket path is capped at 108 bytes, and a
directory inside a deep worktree overflows it, which the first run of this
script died of.
