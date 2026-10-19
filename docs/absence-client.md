# Absence ledger — CLIENT

`plan_entity.md:738`: *"A step that needs a stub writes it into the side's
absence file with the caller it waits for; the file must be empty at the phase
exit."*

**This file did not exist until step 7h, and that is the first thing to know
about it.** Sixty status files were searched and not one mentions an absence
file; `git ls-files | grep -i absen` finds only source files with `absent` in
their names (`shell_ai_absent.cpp` and five siblings), which are refusal
translation units, not this ledger. So every step from Phase 5 to Phase 7 has
passed the clause *"the absence file is empty"* against a file that was never
created — a check that could not fail.

7h introduces no stub of its own. The rows below are the client-side absences
that exist RIGHT NOW and that this step measured while trying to remove them.
Each names what is absent, the caller it waits for, and who owns closing it.
**The file is not empty, so the Phase 7 exit clause it belongs to is NOT met**,
and it is better for that to be visible than for a missing file to keep reading
as a pass.

Retire a row only by closing it. A row deleted without its caller going away is
the same defeat as the empty file.

---

## 1. `#ifdef CLIENT_DLL` preludes in six shared TUs, waiting on step 4h

These six are compiled into BOTH DLLs, and the two sides have different
`cbase.h` files. Their client arm now names the prelude headers it uses; the
`#else` arm still carries `#include "cbase.h"` for the server, whose own
entity-free step has not run.

| file | client prelude | waits for |
|---|---|---|
| `game/shared/entitylist_base.cpp` | `client_class.h` | step 4h ("the entity-free fifteen") |
| `game/shared/gamestringpool.cpp` | `string_t.h`, `client_class.h`, `icliententityinternal.h` | step 4h |
| `game/shared/movevars_shared.cpp` | `client_class.h` | step 4h |
| `game/shared/predictableid.cpp` | `tier0/platform.h`, `predictableid.h`, `client_class.h` | step 4h |
| `game/shared/takedamageinfo.cpp` | `client_class.h` | step 4h |
| `game/shared/usercmd.cpp` | `predictable_entity.h`, `cdll_util.h`, `client_class.h` | step 4h |

Two of those headers (`cdll_util.h`, `icliententityinternal.h`) are under
`game/client`, which is not on the server include path at all, so the guard is
not tidiness: without it the SERVER build stops with
`fatal error: cdll_util.h: No such file or directory` — measured, on the first
attempt at this step, in `baseentity_sound_members.cpp` and `physics_shared.cpp`.

Owner: **4h**. Closing it deletes the `#ifdef` and both arms become one.

## 2. `COM_GetModDirectory()` in `src/shared/native/shell_shared.cpp`

Step 7g copied it verbatim out of `achievementmgr.cpp:698` when it stopped
shipping achievements, and named its own removal condition: *"It is temporary:
7c owns `viewrender.cpp` and 7h the rest; when the second caller leaves the vpc
this definition goes with it."*

One of its two callers has left: `client_virtualreality.cpp` went with SteamVR
in plan_client_novalve C1 (lane c1, 2026-10-05). The other is still compiled
(`grep` over the compiled stock client TUs, comment lines excluded):

    game/client/viewrender.cpp:2192

Owner: **whoever evicts `viewrender.cpp`** (the renderer phases of
plan_client_novalve). Waits for: that caller.

## 3. `c_sun.h` holds the entity inside two otherwise entity-free TUs

`game/client/glow_overlay.cpp` and `game/client/warp_overlay.cpp` lost `cbase.h`
and still reach `game/client/c_baseentity.h`, through exactly one edge:

    glow_overlay.cpp -> c_sun.h -> c_baseentity.h
    warp_overlay.cpp -> view.h, c_sun.h -> c_baseentity.h

`c_sun.h:60` is `class C_Sun : public C_BaseEntity`, a real entity class, so
this is not a stale include like `c_recipientfilter.h`'s was — it is the sun
entity, and it has no owner in any Phase 7 step.

Owner: **unowned.** Waits for: `C_Sun` to become a managed entity.

## 4. `c_sprite.h` holds it inside `spritemodel.cpp`

    spritemodel.cpp -> c_sprite.h -> game/shared/Sprite.h -> baseentity_shared.h
                                                          -> c_baseentity.h

Step 7a evicted `c_sprite.cpp` from `client_csharp.vpc` and left the header,
which `spritemodel.cpp` still includes. Same shape as row 3.

Owner: **unowned** (7a's family). Waits for: the sprite entity.

## 5. `c_te_effect_dispatch.cpp` — recorded blocked by 7b, re-measured here

7b's status file §7.2 rules it *"A GUARD AND NOT AN EVICTION, which is the one
file in lane CE where that is the right answer"*, with 18 consumer objects
needing four symbols from it. 7h re-measured it from the include side: with
`cbase.h` replaced by the full non-entity prelude it compiles, and then reaches
`c_baseentity.h` through `c_recipientfilter.h`, which it needs for
`C_RecipientFilter` itself — not through a stale include.

Owner: **7b's successor / Phase 8.** Waits for: the TE dispatch port.

## 6. FOV-scaled render fade distance — no zoom state reaches this client

Recorded by lane `4c-render`, which deleted the mechanism that used to record
it at runtime.

Stock scales three distances by `CBasePlayer::GetFOVDistanceAdjustFactor()` —
`localFOV / defaultFOV`, `baseplayer_shared.cpp:1786` — so that a scope, which
makes things bigger on screen, does not fade them out from under itself:

    detailobjectsystem.cpp  SortSpritesBackToFront          (reciprocal)
    detailobjectsystem.cpp  BuildDetailObjectRenderLists    (direct)
    c_pixel_visibility.cpp  GlowSightDistance               (direct)

`PlayerNetState` carries no `fov` and no `defaultFov`, so there is nothing for
this client to divide: the factor is **1 by construction**, not by accident.
It was carried as `SourceSharp_ViewerFovDistanceAdjustFactor()`, a named
function that returned exactly `1.0f` and pushed a `SourceSharp_NotShipped`
row the first time a viewer existed. Lane `4c-render` deleted the function and
all three call sites: a per-frame call that always answers 1 is a cost, and the
ledger row it produced said nothing a reader could act on that this row does
not.

**What is actually absent:** detail sprites, pixel-visibility glows and static
props fade at their unzoomed distances however far a scope is zoomed in.

**Note for whoever closes it:** stock inverts the factor at
`SortSpritesBackToFront` and not at the other two. A restoration that spells
all three the same way doubles the error instead of cancelling it; both
detailobjectsystem sites carry that warning at the line.

Owner: **unowned.** Waits for: a `fov` field on `PlayerNetState`.

## 7. The in-game video panel — deleted by ruling

User ruling 2026-10-04 (plan_client_novalve §8), carried out by lane c1 in C1.
`vgui_video.cpp` and `vgui_video_player.cpp` left client.so: `VideoPanel`,
`VideoPlayerPanel` and the two commands that opened them.

**What is absent:** `playvideo <file> [w h]` and `playvideo_exitcommand <file>
<cmd>` are unknown commands; no Bink/WebM clip plays in game. No caller is
waiting: no other TU named either class or header (`ninja -t deps` over the
client objects), and no engine binary names a video panel.

Owner: **none — a ruled deletion, kept here as K7 of plan_client_novalve §4.2
requires.** It does not close.

## 8. No-op `fps` and `netgraphpanel` for `vgui_int.cpp`

The FPS and net-graph panels are deleted by ruling (plan_client_novalve C1);
what they drew is the dev API's `frame` and `netgraph` routes. `vgui_int.cpp` (fork) still calls
`fps->Create`/`fps->Destroy` from `VGui_CreateGlobalPanels`, `VGui_Shutdown`
and the video-mode callback, and `netgraphpanel->Create`/`Destroy` from the
same three places, so `shell_client.cpp` defines both pointers as objects whose
two slots do nothing. The blocking-I/O panel needs nothing: every
`iopanel` use in `vgui_int.cpp` sits under `TRACK_BLOCKING_IO`, defined
nowhere.

**What is absent:** nothing a player sees beyond the ruling; this row records
the stub, not a behaviour.

Owner: **C3** (the UI). Waits for: `vgui_int.cpp` leaving client.so.

## 9. SteamVR — deleted by ruling; no-op `g_ClientVirtualReality` for the view TUs

User ruling 2026-10-04 (plan_client_novalve §8), carried out by lane c1 in C1.
`client_virtualreality.cpp` left client.so with its `ClientVirtualReality001`
export (the C0 probe: engine.so asked for it 0 times, and the game ran
without it) and its 36 `vr_*` console entries (32 convars, 4 commands). `-vr` no longer asks for
sourcevr's interface, so `g_pSourceVR` stays NULL and `UseVR()` is false.

**What is absent:** VR mode, the HMD view and HUD quad, and every `vr_*`
console variable and command.

**The stub.** Four fork TUs still import `g_ClientVirtualReality` and ten of
its members (`nm -u` over the client objects):

    view.cpp        OverrideView, OverrideStereoView, ProcessCurrentTrackingState,
                    PostProcessFrame, OverlayHUDQuadWithUndistort, ShouldRenderHUDInWorld
    viewrender.cpp  RenderHUDQuad, ShouldRenderHUDInWorld
    in_main.cpp     OverridePlayerMotion, GetTorsoRelativeAim
    view_scene.cpp  GetHudProjectionFromWorld

Every call is behind `UseVR()` except two `ShouldRenderHUDInWorld` tests in
`view.cpp`, where stock answers `UseVR() && vr_render_hud_in_world`, i.e.
false. `shell_clientdll.cpp` defines the object and those members as no-ops,
plus the constructor, destructor and six virtuals the object needs to exist.
Nothing calls through its vtable: no interface is exposed.

Owner: **the phases that retire those four TUs** (the renderer and C6, input).
Waits for: each caller; the stub goes with the last.

## 10. No-op studio-stats names for `viewrender.cpp`

`studio_stats.cpp` is deleted (plan_client_novalve §2.1,
docs/client-novalve-rulings.tsv: a modelling tool, `r_studio_stats`).
`viewrender.cpp` (fork) still calls `StudioStats_FindClosestEntity` once per
main view and compares every opaque renderable with `g_pStudioStatsEntity`, so
`shell_client.cpp` defines the first as a no-op and the second as NULL. No
entity is ever chosen, so nothing is drawn with `STUDIO_GENERATE_STATS`,
which is what `r_studio_stats 0` did.

**What is absent:** the `r_studio_stats` overlay (three cheat convars).

Owner: **C7** (the renderer). Waits for: `viewrender.cpp` leaving client.so.

## 11. A managed trail's velocity is zero — no client entity velocity reaches the trail sims

`C_SmokeTrail` and `C_DustTrail` read `GetAbsVelocity()` in `Update`: the
puff origin is wound back along it, and every puff inherits it. Their sims
are C# now (`SmokeTrailSim`, `DustTrailSim`, lane c2-sims), and the client
trail entities (`ClientTrails.cs`) pass `default` for it, because nothing
replicates or estimates a managed entity's velocity on the client. The
native shells they replaced had the same zero (a client-built `C_SmokeTrail`
was never given a velocity), so this is no behaviour change; it is the
stock behaviour that a moving parented trail had and does not have here.

**What is absent:** puffs that trail a MOVING env_smoketrail/env_dusttrail
(a parented one) — they spawn at the current origin with no inherited speed.

Owner: **C8**. Waits for: a client-side absolute velocity for standalone
entities (from interpolation, or the server's), handed to `IManagedParticleSource`'s
emit — `ParticleEmit` would grow a velocity field.

## 12. `playsoundscape` completes no names — managed commands have no completion

Stock's `playsoundscape` registered `SoundscapeCompletion`, which listed the
parsed soundscapes beginning with what had been typed. The client soundscape
system is C# now (`ClientSoundscapes`, sound cluster 17, lane c2-sims) and
its commands are `[ConCommand]` classes, which register through
`IEngineConsole.RegisterConCommand` with no completion callback. The command
itself, `stopsoundscape`, `cl_soundscape_flush`,
`cl_soundscape_printdebuginfo` and `cs_cl_soundscape_report` behave as
before.

**What is absent:** tab completion of soundscape names after `playsoundscape `.

Owner: **C8**. Waits for: a completion hook on managed commands (a generic
`IManagedConsole` completion slot), which every managed command could use.

## 13. Ash falls where stock's Linux build drops it — a ruled divergence, not an absence

This is recorded here because it is the opposite of an absence. Stock on Linux
shows no ash; the port shows it.

`CClient_Precipitation::CreateAshParticle` places each candidate at
`m_vAshSpawnOrigin + vSpawnOrigin + RandomVector( -256, 256 )`, then drops any
candidate outside the volume.
- mathlib's RandomVector is `Vector::Random`, which computes
  `min + ( (float)rand() / VALVE_RAND_MAX ) * ( max - min )`.
- VALVE_RAND_MAX is 0x7fff, MSVC's range.
- glibc's `rand()` reaches 2^31-1, so on Linux each component lands up to about
  65,000 times outside ±256.

Measured by the precipitation oracle (src/test/oracle/native/precipitation_oracle_test.cpp,
Fixtures/precipitation-stock.tsv), stock run offline with glibc's own `rand()`
(srand 1):
- phase `glibc` adds **0** ash particles in 240 frames;
- so the ember arm, which needs 250 in-bounds candidates, never runs.

The same code with the MSVC CRT's generator (phase `msvc`, 300 frames) adds
them, ember arm included.

**Ruling (integrator, 2026-10-05):** the port uses MSVC semantics, so ash falls
as the content intended. It draws `rand()` as 0..0x7fff from a per-system seeded
stream (`IPrecipitationRandom.CRand`), never the process-global C library
state. Both oracle phases stay, so the Linux-faithful arm remains measurable.
The user may overrule.

## 14. Detail shapes are not compiled — `USE_DETAIL_SHAPES` is off, and its sway would be dead

DETAIL SHAPES ARE NOT COMPILED IN THIS BUILD. `detailobjectsystem.cpp`
defines `USE_DETAIL_SHAPES` only under `#if defined(DOD_DLL) ||
defined(CSTRIKE_DLL)`, and step 7d measured that neither is defined rather
than assuming it, because everything under the macro reads as live code:

    grep -c 'DOD_DLL\|CSTRIKE_DLL' <the client's build.ninja>        -> 0
    nm -C --defined-only <objs>/detailobjectsystem.o
      | grep -cE 'UpdatePlayerAvoid|CPlayerEnumerator'              -> 0

So the file keeps one `GetLocalPlayer` and one `CPlayerEnumerator` inside the
guard, and that is allowed only because this row says why.

**WHOEVER TURNS THIS ON INHERITS A DEAD SWAY.** `CDetailModel::UpdatePlayerAvoid`
finds nobody to avoid on this client, for two independent reasons:

- `playerenumerator.h`'s `CPlayerEnumerator::EnumElement` resolves each
  partition hit through `ClientEntityList().GetBaseEntityFromHandle`, which
  walks the partition before it asks `GetBaseEntity()`: NULL for every managed
  shell, so it collects nothing;
- the local player it adds at the end is `C_BasePlayer::GetLocalPlayer()`,
  whose one definition on this link line is `return NULL`.

The fix is a named fact over the slot table, the shape `renderable_facts.h`
uses for everything else in step 7d. It was written and withdrawn, because a
seam function with no compiled caller is native code nothing can gate. When
restoring it, note that the loop's two distance tests are NOT the same test.
Collection is 3-D (`LengthSqr` against the radius), and the force ramp is 2-D
(z zeroed, then `Length2D`). So a player on a roof above a clump is collected
only by the 3-D test, and then pushes the clump at nearly full force.

This row was a comment block at the top of `detailobjectsystem.cpp` in the
fork. It moved here so the patched-upstream ROOT (`make sdk-upstream`, whose
series carries no comments) keeps the finding. `renderable_subject_test.cpp`
checks this row and the guard it describes.

Owner: whoever enables `USE_DETAIL_SHAPES`. Waits for: a reason to.

## 15. The flashlight-target tail in `clientshadowmgr.cpp`: deleted, not ported

Stock lets a projected texture light one specific entity: a flashlight's shadow
walks the target's move children (`FirstMoveChild` / `NextMovePeer`) and adds
each one to the receiver. On this client that tail has never run.
- The only setter of the target is `c_env_projectedtexture.cpp:215`.
- `client_csharp.vpc` `-$File`s that file out of the project.
- So the handle stays the invalid one it was default-constructed with,
  `bLightSpecificEntity` is false, and the tail is never reached.

`FlashlightTargetVacuity` in `cpp_alias_t5_test.cpp` is the guard: it fails if
that exclusion goes, which would make this row a lie.

The walk was NOT RETYPED, and there are two separate walls rather than one:
- `FirstMoveChild` / `NextMovePeer` / `GetModel` read the SDK move hierarchy
  off `C_BaseEntity`. That hierarchy has no raw parent pointer to hand out at
  all: igameentity.h says so for GetMoveParent by name.
- `AddShadowToReceiver` takes an `IClientRenderable *`, and an
  `IGameEntity *` does not convert to one. `IGameEntity` has no base, so the
  conversion is the same no-route this port records for `IHandleEntity`.

A flashlight aimed at one managed entity would have to go through the
renderable that draws it, not through the entity.

This was a comment in the fork's copy of `clientshadowmgr.cpp`. It moved here
so the comment-free patched upstream (`make sdk-upstream`) keeps it.
`leaf_cycle_test.cpp`'s `ShadowMgrDeEntitied` fact reads this row.

Owner: whoever restores `env_projectedtexture`. Waits for: a managed light
target.
