# The client entity shell, and what is left native in it

`plan_shim.md` §6 (Phase 4), lane `4a-clientshell`. The companion of
`docs/entity-shell.md`, which is the server's half of the same question.

---

## 1. The facade audit: the server's twenty-slot defect does not occur here

Lane 3c re-measured `shell_entityapi.cpp` and found the previous lane's split
**inverted** — not 23 unreached slots and 6 reached, but 3 unreached, 5
decorated and **20 with live managed callers receiving nothing**. The earlier
lane had grepped the SLOT name; managed code names the **facade method** in
`EngineBinding.cs`, which is spelled differently.

The same audit was owed over every client slot this lane's files serve. It was
taken with a committed instrument rather than a throwaway grep, because the
reading has to be re-takeable. (That instrument, tools/shim-facade-audit, was
deleted on 2026-10-04: it still read the pre-move src/sourcesharp/ layout from
the repo root and could not run. The commands and numbers below are this
lane's record at its base.)

```
tools/shim-facade-audit --self-test
tools/shim-facade-audit served    src/sourcesharp/game/client
tools/shim-facade-audit unreached src/sourcesharp/game/client
tools/shim-facade-audit nulled
```

### The result

| question | client answer | server answer (3c) |
|---|---|---|
| slots this lane's files serve | **62** | 29 |
| of those, bodies that are STUBS | **0** | 28 |
| of those, with no managed caller by any route | **2** | 23 claimed, 3 real |
| managed callers receiving nothing | **0** | **20** |

**There is no client defect ledger of this shape.** Every slot lane 4a's files
serve has a live body — nothing in the client half is the `func_friction`
situation, where a game rule calls in and the native side answers with a stub.

The reason the two halves differ is structural rather than lucky. The server's
stubs are what remained when `CBasePlayer` left the build in 13c: the state
those slots read is simply gone. The client shell never had a corresponding
removal — `C_StandaloneEntity` still holds every member its slots touch.

### The second shape, checked separately: a caller on a slot the client never binds

Lane 2e-abi's `ScriptedEntity.PlaySequence` defect was not a stub at all. It was
a live managed caller routed at a slot Phase 2 had **nulled**, returning false
for every prop on every map, invisible to both suites and to the live gate. A
stub census cannot see that one, so it is a separate subcommand:

```
tools/shim-facade-audit nulled          # exit 0, no rows
```

**Zero rows over `SourceSharp.Client` and `SourceSharp.Shared`.** The control
that makes that zero mean something is in `--self-test`: it forces
`StandaloneRagdollApplyForceCenter` — a slot the client *does* bind — through
the same report and requires `ClientTeExplosion.cs` to come back as its caller.
Without that control, an empty report proves only that the report is empty.

### The two slots that are unreached, and why they are NOT retired

| slot | facade | what it is |
|---|---|---|
| `StandaloneSetShadowCast` | `IEngineEntityRender.SetShadowCast` | the write path for `EngineBlockClientTail_t::ShadowChoice` |
| `StandaloneShadowCast` | `IEngineEntityRender.ShadowCast` | the read-back: what is cast, what was asked, and whether the shadow manager holds a handle |

`ClientRenderPolicy.cs` *reads* `ShadowChoice` out of the engine block
(`:606`) and has a `Shadow(in ClientRenderFacts)` decision procedure — and
nothing calls either that decision or these two slots. So no managed rule asks
for a shadow choice today, and every entity stays on `AUTO`.

That is an **unfinished port, not a finished one**. Lane 3c's rule applies
directly: retiring a slot whose caller wants an answer records a live
requirement as done, and here the requirement is one step earlier — the answer
exists in managed and has no way out. Retiring the write path would make
`ClientRenderPolicy.Shadow` permanently unreachable. They stay, listed here so
the next lane finds them rather than re-deriving them.

**Re-checked by lane 5b after the property fix below, and the ruling stands —
for a reason rather than on the tool's silence.** Both are declared as METHODS
(`EngineAreas.Entities.cs:501,504`, bound at `EngineBinding.cs:548,549`), so the
property blind spot that produced four false zeroes *cannot* reach them: their
zero was never a property read the scan could not see. A receiver-style search
of the whole managed tree for `.SetShadowCast` / `.ShadowCast` finds only the
explicit interface implementations on `RecordingEngine.cs:663-668`, which are
the test double's *implementations* and not calls; every other `ShadowCast` hit
in `ClientRenderPolicy.cs` is the enum or the `ShadowChoice` field. Under the
fixed resolver both still resolve to the same two facades and both still have no
caller. Genuinely unreached, correctly pinned.

### Outside this lane's files, for whoever owns them

`unreached` over the whole client directory also finds seven slots with no
caller and no route, none of them in lane 4a's files:

- `shell_hud.cpp` (lane 4c): `GetLocalWeaponCount`, `GetLocalActiveWeapon`,
  `GetLocalWeaponClip`, `GetLocalWeaponReserve` — **all four are STUB bodies
  with no managed caller**, which is the only place in the client half where
  the server's shape appears at all. `GetLocalPlayerHealth`, `HudLookupBinding`
  and `HudLocalise` have live bodies and no caller.
- `shell_worldrender.cpp` (lane 4c): `ClientColorCorrection`,
  `GetLocalPlayerOrigin`.

### Four ways this audit's own reading was wrong before it was right

Recorded because every one of them produced a confident, plausible, wrong
number, and three of them produced it in the direction of "delete this".

1. **A two-line assignment read as "assigned nowhere".**
   `engineApi.StandaloneRagdollApplyForceCenter =` with the function on the
   next line. The first parse reported the slot as bound nowhere — which is
   exactly the defect `nulled` exists to find, so the tool reported its own
   parse bug as a finding. `--self-test` now pins that slot.
2. **A facade resolved as a bare method name.** `Count`, `Create`, `IsValid`
   and `Idle` are facade names. The first `nulled` run printed **28 rows**,
   every one a false positive, because most of the client mentions `Count`.
   A facade is an (interface, method) pair here.
3. **Receivers typed per file.** The receiver is usually
   `protected readonly IEngineEntityRender Render` on `ClientStandaloneEntity`
   and the callers are subclasses elsewhere saying `Render.SetFade(...)`.
   Per-file typing called **eight live slots dead**.
4. **A call is not always a call site.**
   `wireframe.Sweep = entities.UpdateVisibilityAll;` passes the facade as a
   method group, and `var soundscapeEngine = _services.Require<IEngineSound>()`
   declares a receiver with no type name in the text. Each cost one live slot a
   false zero (`EntitiesUpdateVisibilityAll`, and `ClientSetSoundscape` until
   sound cluster 17 retired it).

A zero from this instrument is a zero for *facade* calls. Slots reached through
a second `SourceSharp.Abi` layer — `StringTableRegistry`, `StandaloneNetStates`,
`ClientReplay`, `InterpolationBlocks`, `RenderPolicyBlocks` — answer zero and
are live; `unreached` prints that route in its last column and **a row with a
route is not a candidate for anything**.

### Two more ways, found by lane 5b: the audit cannot see a property

`tools/shim-facade-audit unreached src/sourcesharp/game/client` prints 22 rows,
six of them with no second-layer route. Two are lane 4a's pinned shadow-cast
pair. **The other four are all false positives**, and they are false for two
reasons neither of which is in the four above. Both live in `facade_map()`.

5. **Property members never update the current method name.** The scan is

   ```python
   m = re.match(r'\s*public\s[\w<>\[\],\?\s\.]*?\b(\w+)\s*\(', line)
   if m:
       method = m.group(1)
   ```

   which only matches a member with a `(`. `EngineBinding.cs` binds most of
   `IEngineHud` as **properties** — `public int LocalPlayerHealth => Engine.LocalPlayerHealth;`
   has no parentheses before the `=>`, so `method` is never reassigned and the
   property inherits **the previous method's name**. Probed directly rather
   than read off the regex:

   | slot / static | what `facade_map()` answers | what it is |
   |---|---|---|
   | `LocalPlayerHealth` | `(IEngineHud, AddLineOverlay)` | stale carry-over |
   | `LocalPlayerOrigin` | `(IEngineHud, AddLineOverlay)` | stale carry-over |
   | `GetLocalPlayerIndex` | `(IEngineHud, LocalWeaponName)` | stale carry-over |

   `AddLineOverlay` and `LocalWeaponName` are simply the last methods declared
   above those properties. This is what makes `GetLocalPlayerIndex` look as
   though it resolves to a facade about weapon names.

6. **The map is keyed on the `Engine.cs` static's name, not the slot's.**
   `facade_map()` keys on `Engine\.(\w+)`. Wherever the static was renamed from
   the slot — slot `GetLocalPlayerHealth` → `Engine.LocalPlayerHealth`, slot
   `GetLocalPlayerOrigin` → `Engine.LocalPlayerOrigin`, and the op-dispatch slot
   `ClientColorCorrection` → `Engine.ColorCorrectionAdd` / `…Remove` /
   `…SetWeight` — the slot gets **no entry at all** and prints `NO-FACADE`.
   A one-slot-to-many-facade fan-out is invisible to it for the same reason.

### The four unreached client slots: all four are LIVE

Neither dead nor an unfinished port — a **third** category the project has not
hit before: live slots with ordinary managed callers that the instrument cannot
see. Every chain below was walked end to end.

| slot | real facade | managed callers | verdict |
|---|---|---|---|
| `GetLocalPlayerHealth` | `IEngineHud.LocalPlayerHealth` (property) | `SourceSharp.Client/Hud.cs:125` | **LIVE — do not retire** |
| `GetLocalPlayerIndex` | `IEngineHud.LocalPlayerIndex` (property) | 6 sites (below) | **LIVE — do not retire** |
| `GetLocalPlayerOrigin` | `IEngineHud.LocalPlayerOrigin` (property) | `Standalone/ClientColorCorrection.cs:59`, `TempEnts/ClientTraceProbe.cs:80` | **LIVE — do not retire** |
| `ClientColorCorrection` | `IEngineVisibility.ColorCorrectionAdd` / `Remove` / `SetWeight` | 7 sites in two entity classes | **LIVE — do not retire** |

The chains, for whoever executes rather than re-derives:

```
GetLocalPlayerHealth   Abi.cs:340  -> Engine.cs:3155 LocalPlayerHealth
                       -> EngineBinding.cs:736 IEngineHud.LocalPlayerHealth
                       -> Hud.cs:125   int health = Screen.LocalPlayerHealth;

GetLocalPlayerIndex    Abi.cs:1180 -> Engine.cs:5347 GetLocalPlayerIndex()
                       -> EngineBinding.cs:742 IEngineHud.LocalPlayerIndex
                       -> ClientArsenal.cs:311,361; Standalone/ClientSsViewModel.cs:65;
                          Standalone/ClientWorldFog.cs:144; Standalone/ClientSkybox.cs:152;
                          ClientPlayerPrediction.cs:836

GetLocalPlayerOrigin   Abi.cs:684  -> Engine.cs:6985 LocalPlayerOrigin
                       -> EngineBinding.cs:737 IEngineHud.LocalPlayerOrigin
                       -> Standalone/ClientColorCorrection.cs:59;
                          TempEnts/ClientTraceProbe.cs:80

ClientColorCorrection  Abi.cs:675  -> Engine.cs:7004/7021/7032
                       -> EngineBinding.cs:925-927 IEngineVisibility.ColorCorrection*
                       -> Standalone/ClientColorCorrection.cs:125,148,171,189;
                          Standalone/ClientColorCorrectionVolume.cs:78,102,137
```

`ColorCorrectionVolumeTests.cs:130,159,251` already asserts on all three colour
correction ops, so that slot additionally has a managed test pinning it.

**What retiring any of them would have cost:** the HUD's health readout, every
consumer of the local player index (arsenal, view model, fog, skybox,
prediction), the temp-entity trace probe, and both `color_correction` and
`color_correction_volume`. This is lane 3c's shape again — a defect ledger, not
a retirement list — except that here the slots are not even defective. The ABI
is locked this pass, so **nothing was marked, retired or bumped**; the next lane
should treat all four as pinned and fix the tool before re-running it.

### The current `unreached` list for the client: 18 rows

Taken with the fixed resolver at this lane's commit, so the next lane inherits a
current list rather than re-running the audit to discover it moved. Before the
fix this was 22 rows; the four that left are the four above, and **none joined**.

Read the last column first. `unreached` prints a second-`SourceSharp.Abi`-layer
route when it finds one, and **a row with a route is not a candidate for
anything** — the slot is reached, just not by a name the facade scan sees. On
that rule exactly **two** of these eighteen are candidates for any future
ruling, and both are already pinned by lane 4a above.

| slot | body file | facade | `abi_route` |
|---|---|---|---|
| `NetHierarchyDeclare` | `shell_standalone_client.cpp` | `IEngineNetworking.NetHierarchyDeclare` | `NetSchema.cs`, `StandaloneNetStates.cs` |
| `NetPublishType` | `shell_recvtable.cpp` | `IEngineNetworking.NetPublishType` | `StandaloneNetStates.cs` |
| `NetTierInfo` | `shell_recvtable.cpp` | `IEngineNetworking.NetTierAt` | `INetTiers.cs`, `NetSchema.cs` |
| `StringTableFind` | `shell_stringtables_client.cpp` | `IEngineStringTables.Find` | `EngineAreas.StringTables.cs`, `NetStateSource.cs` |
| `StringTableFindStringIndex` | `shell_stringtables_client.cpp` | `IEngineStringTables.FindStringIndex` | `EngineAreas.StringTables.cs` |
| `StringTableGetString` | `shell_stringtables_client.cpp` | `IEngineStringTables.GetString` | `EngineAreas.StringTables.cs`, `ModelDescriptor.cs`, `NativeInterfaceFactory.cs`, `SaveCustom.cs`, `SaveTiers.cs`, `SavedEntityCodec.cs`, `ScriptedEntity.cs`, `UserMessage.cs`, `Utf8Names.cs` |
| `StringTableGetNumStrings` | `shell_stringtables_client.cpp` | `IEngineStringTables.GetNumStrings` | `EngineAreas.StringTables.cs` |
| `WorldNetDeclare` | `shell_world_nettable_client.cpp` | `IEngineNetworking.WorldNetDeclare` | `StandaloneNetStates.cs` |
| `StandaloneSetShadowCast` | `shell_standalone_client.cpp` | `IEngineEntityRender.SetShadowCast` | **none — candidate** |
| `StandaloneShadowCast` | `shell_standalone_client.cpp` | `IEngineEntityRender.ShadowCast` | **none — candidate** |
| `ClientInterpBlocks` | `shell_standalone_client.cpp` | **NO-FACADE** | `InterpolationBlocks.cs` |
| `ClientPolicyBlocks` | `shell_standalone_client.cpp` | **NO-FACADE** | `RenderPolicyBlocks.cs` |
| `ClientGetUserCmd` | `shell_prediction.cpp` | **NO-FACADE** | `ClientReplay.cs` |
| `MoveBeginClientCommand` | `shell_prediction.cpp` | **NO-FACADE** | `ClientReplay.cs` |
| `MoveEndClientCommand` | `shell_prediction.cpp` | **NO-FACADE** | `ClientReplay.cs` |
| `ClientPlayerPredictionDrive` | `shell_prediction.cpp` | **NO-FACADE** | `ClientReplay.cs` |
| `TempEntRegister` | `shell_tempents_client.cpp` | `IEngineEffects.TempEntRegister` | `EngineEffectsForwarder.cs`, `TempEntRegistry.cs` |
| `ClientDecalIndexFromName` | `shell_te_backends_client.cpp` | `IEngineEffects.ClientDecalIndexFromName` | `EngineEffectsForwarder.cs` |

`NetTierInfo` is on this list as `IEngineNetworking.NetTierAt` rather than
NO-FACADE because of the resolver fix — it is one of the twenty slots that
gained a facade — but it has a route and its verdict is unchanged.

---

## 2. §6's client-shell items, re-measured

The rulings in `plan_shim_rulings.tsv` were taken at `85a7449d1` by read-only
lanes, and Phase 1's instrument purge, Phase 1c's ConVar move and the whole of
Phase 3 have landed since. Every item in this lane's step was re-measured before
it was executed. **Five of seven did not survive.**

| item | plan says | measured |
|---|---|---|
| 1 facade audit | expect the server's inverted split | **0 stubs, 0 callers receiving nothing.** §1 above |
| 2 `shell_standalone_client.cpp` | 7,400 lines, 41% comment; `ComputeFxBlend` + ≈1,200 lines of anim state become block fields | **6,848 raw / 2,955 code / 57% prose.** The move does not survive — §2.1 |
| 3 `IGameEntity` | 7 stock callers; retype them and 6 slots go | **6 callers, retyped and the relays deleted.** The slots do NOT go — §2.2 |
| 4 `shell_clientdll_probe.h` | capture, then delete | **captured, then DELETED** — 459 raw / 312 code, with both console commands — §2.3 |
| 5 `cs_sv_boneprobe` / `BoneReplay` | managed recorder + gate re-point, same commit | **blocked on an ABI slot that does not exist** — §2.4 |
| 6 `shell_playerresource` | delete; panels move to the managed HUD | **3 importers confirmed; two managed inputs are missing** — §2.5 |
| 7 ConVars | move four, delete one | **already done in Phases 1c and 2e** — §2.6 |

### 2.1 `ComputeFxBlend` and the anim state do not become block fields

The plan's sentence is that `ComputeFxBlend`'s fade curve and "the anim-layer /
pose-parameter / sequence-cycle state (:2126-2324, :4733-5756, ≈1,200 lines)"
become "engine-block fields managed writes ... a layout change, not a call".

**The ≈1,200 is a raw line SPAN, and it is not a set of movable code.** Read at
`85a7449d1`, the two cited ranges are **1,223 raw but 625 code lines, 49%
prose** — the same raw-versus-code confusion `world_adapter` and `physics.cpp`
hit in Phase 3. Worse, the spans are not the subject: `:2126-2324` is
`EnsureAttachments` and the attachment lookup, not anim layers at all, and
`:4733-5756` starts at `SourceSharp_ClientStudioHdr` (the studio-header
resolver) and ends at `SourceSharp_ClientEmitSound`, taking in every ABI door
between them.

Measured function by function on today's file (`.lane4a/anim_region.py`), the
named candidates are **283 code lines**, and each is native for one of two
reasons:

| function | code | why it cannot leave |
|---|---|---|
| `SetPlayingSequence` | 23 | `BoneLibrary_SequenceFacts` — studio header |
| `SetAnimLayer` | 31 | studio header, per layer |
| `RefreshModelPoseState` | 47 | `Studio_CalcDefaultPoseParameters` |
| `PoseParameterCount` / `LookupPoseParameter` / `PoseParameterName` / `PoseParameterRange` | 51 | studio header |
| `SetPoseParameter` / `GetPoseParameter` | 27 | `Studio_SetPoseParameter` normalises, wraps and clamps against the model's declared range |
| `SequenceCycle` / `AnimLayerCycle` | 23 | read per draw from `EnsureBones` |
| `ComputeFxBlend` | 48 | called by the renderer per frame per renderable |
| `AnimLayerAt` / `ActiveAnimLayers` | 33 | pure storage readers; the `StandaloneAnimLayer` report path |

**Ten of the fifteen need the studio header**, which is native data managed has
no view of: a managed write of a pose parameter could not normalise it, and a
managed `SetAnimLayer` could not read the layer's duration. The remaining three
are read **per draw**, so moving them is a crossing per entity per frame — which
is not a perf prediction on this lane's part but the plan's own stated
criterion, "with no crossing", applied to the plan's own candidate.

`ComputeFxBlend`'s *policy* half already moved in step 6b: `SetFade` writes
`FadeStyle`, `FadeStartDistance`, `FadeEndDistance` and `FadeMinAlpha` into the
block, and `ComputeFxBlend` is the evaluator that reads them. What is left is
arithmetic over the block plus two values only available natively per frame —
the view origin and the world-space surrounding bounds.

**The one thing in this item that DOES measure as the plan says** is its
precondition. `m_flPoseParameter` and `m_Layers` have exactly one native reader
outside `EnsureBones`, `RefreshModelPoseState` and the entry points, and it is
the `ClientBoneProbe` instrument (`:6055`, `:6064-6074`) — §4a item 5's subject.
So if the state ever does move, the probe is the only thing in the way, which is
what the plan predicted.

**Recommendation:** re-scope the item function by function before anyone
executes it, exactly as lane 3a did with `physics.cpp`'s 1,011 lines, and do not
carry the ≈1,200 figure into another plan.

### 2.2 The six `IGameEntity` slots cannot go yet, and the blocker is a shared header

**Closed 2026-10-19.** The relays left first, then the client's live
`GetGameEntity()` readers moved to the slot table and `C_StandaloneEntity`
dropped `public IGameEntity` and the six slots (`GetCollisionGroup` stays,
`ICollideable`'s); see docs/todo.md's section of the same name for the
measurements and the fork-declared signatures kept. What follows is the record.

`C_StandaloneEntity` implements six `IGameEntity` methods — `ShouldCollide`,
`Touch`, `StartTouch`, `EndTouch`, `VPhysicsUpdate`, and `GetCollisionGroup`
(which `ICollideable` also declares, so one override answers both).

The plan says seven stock callers reach them through
`standalone_relays_client.h`. There are **six**, and that header's three relays
are now deleted. But the shell cannot drop `public IGameEntity` yet, because
**`game/shared/game_entity_relays.h` has nine more client call sites** and the
server compiles the same header:

| site | fact wanted | engine interface that answers it |
|---|---|---|
| `c_te_legacytempents.cpp:226` | `GameEntity_RefHandle` | `IHandleEntity::GetRefEHandle` |
| `c_te_legacytempents.cpp:1428` | `GameEntity_EntIndex` | `IClientEntity::entindex` |
| `c_rope.cpp:1632` ×2 | `GameEntity_ModelIndex` | none directly |
| `ragdoll.cpp:141,154,168` | `GameEntity_ModelIndex` | none directly |
| `view_beams.cpp:691` | `GameEntity_RefHandle` | `IHandleEntity::GetRefEHandle` |
| `shell_clientdll.cpp:2561` | `GameEntity_RefHandle` | `IHandleEntity::GetRefEHandle` |

Five of the nine want a **model index**, and no engine interface answers one:
the route is `modelinfo->GetModelIndex( modelinfo->GetModelName( r->GetModel() ) )`
off an `IClientRenderable`, which is a real change to two stock TUs rather than a
re-spelling. And because the header is shared, retyping it is the server lane's
work as much as this one's. **It is one lane's job, not this one's, and it should
be scheduled as such.**

**The cast those relays make is a property of the LINK LINE, not of the code.**
`standalone_relays_client.h` and `shell_standalone_client.h:132` both assert that
`C_StandaloneEntity` is the only implementor of `IGameEntity` in this DLL. That
was checked rather than taken on trust: **twenty classes in this tree declare
`: public IGameEntity`** — `c_entitydissolve.h`, `c_basedoor.h`, `c_sun.h`,
`c_vguiscreen.h`, `C_EnvProjectedTexture` and more — and `nm -C` over all 253
client objects on the graph finds a vtable for exactly one of them. The claim
holds **today**, and it holds because those TUs are not compiled, which is not a
fact any of those headers states.

### 2.3 §4a item 4 — the `IBaseClientDLL` probe is DELETED

`shell_clientdll_probe.h` was **459 raw / 312 code**. Its capture reached
`docs/engine-boundary.md` §6a (29 of 79 called, 0 refused, commit `24badb01a`),
which was the precondition, so lane `5b-probedelete` removed it: the header, the
`#include`, the seventy-nine `SS_CLIENTDLL_PROBE` arming lines, and both
`CON_COMMAND`s (`ss_cl_clientdll_probe_report`, `ss_cl_clientdll_probe_reset`).

Neither command was reachable by name from anywhere that drives the build:
`grep -rn clientdll_probe . --exclude-dir=.git` found **zero** hits under
`tools/`, `cfg/`, `game/mod_sharp/cfg/` and the managed tree — unlike
`cs_cl_dust_report`, `cs_cl_rope_spans` and `cs_cl_effect_kinds`, each of which
turned out to be gate-driven by name.

**Two things did NOT go with it**, and both were one edit away from doing so:

* `cs_cl_refusals` shares the probe's machinery. `SS_CLIENTDLL_UNREACHABLE` was
  deliberately the single writer of *two* records — the probe's row and the
  refusal tally — so that they could not disagree. It now writes the tally
  alone and names the method with a string literal instead of the probe's enum.
* `ss_cl_stage_crossings` read the engine's `FrameStageNotify` call count off
  the probe's row, and compares it against the crossings into managed code. A
  field on `CShellClientDLL` (`m_nFrameStageNotifyCalls`) replaces that number,
  so the command still compares two numbers rather than reporting one.

`clientdll_probe_test.cpp` was 691 lines and only partly about the probe; it is
now `shell_clientdll_test.cpp`. Its doc facts used to enumerate the probe's
hand-written enum and now parse `public/cdll_int.h` itself, which re-derives the
seventy-nine-not-eighty fact from the interface rather than restating it.

### 2.4 §4a item 5 — the bone probe needs an ABI slot that does not exist

The client half (`cs_cl_boneprobe`, `cs_cl_bonereplay`, `BoneReplay`) is
`shell_standalone_client.cpp:5610-6848`, **1,239 raw / 766 code**; the server
half is `cs_sv_boneprobe` in `shell_standalone_hierarchy.cpp:2017`.
`tools/live-gate` drives them at three sites (`cs_sv_boneprobe seq 3`,
`cs_cl_boneprobe mergeauto`, `cs_cl_boneprobe report`) and reads their console
output at two more (`boneprobe_rows`, `:4069`).

A managed recorder has to emit the same six facts:
`model`, `bones`, `written`, `posed`, `attachments`, `movedattachments`.
Managed can answer three of them today — `StandaloneBoneCount`,
`StandaloneLookupAttachment` and `StandaloneGetAttachment` are all bound on the
client. **It cannot answer `posed` or `written`.** Both are read off
`C_StandaloneEntity::m_CachedBones` — the bone matrix cache — at
`shell_standalone_client.cpp:5860`, and `posed` is specifically a `memcmp` of
each cached matrix against the entity-to-world transform. No ABI slot exposes a
bone matrix.

So this item needs a `StandaloneBoneMatrix`-shaped slot appended first, and then
the five `tools/live-gate` sites re-pointed — and the re-point cannot be verified
without running the gate, which is the integrator's. **"Or not at all" is
therefore the answer for a lane that cannot run it**, and this lane left the
probe alone rather than landing an unverifiable re-point into the Phase 4 gate.

### 2.5 §4a item 6 — `shell_playerresource` deletes, but two managed inputs are missing

`tools/shim-census --mtime-policy off who-imports
src/sourcesharp/game/client/shell_playerresource.cpp` reports **3 client
importers and 0 server**:

```
client  ClientScoreBoardDialog.o  _Z13GameResourcesv
client  SpectatorGUI.o            _Z13GameResourcesv
client  shell_clientsystems.o     _Z27ClientSystem_PlayerResourcev
```

The third is this shim's own game-system row and goes with the delete, so the
plan's reading is confirmed: **`IGameResources` has no other consumer on the
link line.**

**And one near-miss worth recording.** `teammenu.cpp` is on the client link line
and its source contains `GameResources()->GetTeamName( i )` at `:335` — a grep
makes it a fourth consumer. It is not one: the whole of
`CTeamMenu::MakeTeamButtons` is inside a `/* */` block in stock, and
`nm -C teammenu.o` emits no symbol for it and no undefined reference to
`GameResources`. This is exactly why the census is taken over the link line.

What the move needs that does not exist yet:

- **The observer mode is not readable from managed on the client.**
  `PlayerGetObserverMode` is bound under `#ifndef CLIENT_DLL`
  (`sourcesharp_gamesystem.cpp`), so `IEnginePlayers.GetObserverMode` answers
  nothing on this side. `SpectatorGUI` is driven by it. A client arm has to be
  bound before a managed spectator panel can exist.
- **No managed route to the scoreboard key.** The panel is shown while `+score`
  is held; nothing managed sees that.
- The drawing half is ready: `IEngineHud` has `DrawRect`, `DrawText` and
  `MeasureText`, and `Hud.Paint` already receives the screen size.

Sizes, for whoever schedules it: `ClientScoreBoardDialog.cpp` **585 raw / 375
code**, `SpectatorGUI.cpp` **857 raw / 564 code**, `shell_playerresource.cpp`
**609 raw / 307 code**, `shell_playerresource.h` **76 raw / 11 code**.

### 2.6 The ConVar row is already closed

All five names in §6's list for this lane's files are done, in earlier phases:

| name | state |
|---|---|
| `showparticlecounts` | managed, `SourceSharp.Client/ConVars.cs:110` |
| `r_lightmap_bicubic_set` | managed, `:131` |
| `cl_interp_npcs` | managed, `:148` |
| `vcollide_wireframe` | managed, `:198` — lane 2e-abi's visibility sweep did take it |
| `r_drawmodeldecals` | **no native declaration anywhere.** `shell_te_backends_client.cpp:1054` holds a `CManagedConVarRef` by name onto the engine's own, which is a reference and not a declaration |

```
grep -rnE "ConVar +(showparticlecounts|r_lightmap_bicubic_set|vcollide_wireframe|cl_interp_npcs|r_drawmodeldecals) *\(" \
    --include=*.cpp --include=*.h src/sourcesharp/game src/sourcesharp/host
# no output
```

The four protocol names stay native and were not touched: `cl_interpolate`
(`shell_entitystatics.cpp:273`) and `localplayer_visionflags`
(`client_util_library.cpp:91`) are the only native ConVars left in this lane's
files at all; `cl_team` and `cl_class` are members of `CShellClientDLL`.

### 2.7 Two more rulings this lane's measurement overturns

- **`shell_entitystatics.h` is ruled DELETE and has two live includers.**
  `tools/shim-header-census includers sourcesharp/game/client/shell_entitystatics.h`
  reports `shell_clientdll.cpp` and `shell_entitystatics.cpp`. Its declarations
  have to go somewhere before the file can.
- **`shell_standalone_client.cpp` is 6,848 raw, not 7,400**, and **57% prose,
  not 41%**. Phase 1's instrument purge took the difference. Any figure derived
  from the 7,400 is stale by roughly one part in twelve.

### 2.8 `shell_clientdll_refusals.h` has no twin to be merged with

§6 lists "`shell_clientdll_refusals.h` + the server twin → one shared header".

**There is no twin.** `sourcesharp/game/server/shell_gamedll_refusals.h` and
`CShellGameDllRefusals` were deleted: the server's counter had no reader —
nothing in `tools/`, `docs/`, the managed tree or `game/mod_sharp/cfg/` ever ran
`cs_sv_refusals`, and the table's own two readouts were the only things that
read the table. `sourcesharp/test/shell_gamedll_refusals_test.cpp` records that
removal and is the arrangement half only now.

```
find src/sourcesharp -name 'shell_gamedll_refusals.h'     # nothing
grep -rln CShellGameDllRefusals src/sourcesharp/game      # only the client header's prose
```

So the merge would produce a `game/shared` header with exactly one includer,
which is a worse end state than a one-includer header sitting beside its
includer. The file stays where it is, and **its prose is corrected** — it
asserted in the present tense that the server's header "is this file's
original", which stopped being true and would have sent the next reader looking
for a file that is not there.

**The client's counter is not in the server's position and does not follow it
out.** It has a live reader: `cs_cl_refusals` (`shell_clientdll.cpp:2872`) and
the DLL's own shutdown readout, and it is what plan_entity.md 5b's gate sentence
is about — "no row reads `refused=`N with N above zero".

### 2.9 `shell_prediction.cpp`'s empty bodies — and why no symbol census can settle them

§6: "`shell_prediction.cpp` :83-211 (130 lines of empty `CPrediction` bodies) →
implement `IPrediction`'s 13 slots directly".

`CPrediction` **already** derives from `IPrediction` directly, so the item is
really about the eight methods it adds on top of the thirteen. Five of those
take a `C_BasePlayer *` or are reached only from one — `RunCommand`,
`SetupMove`, `FinishMove`, `SetIdealPitch`, `_Update` — and every constructor
for that type left the build in 13c, so none can be called with a non-NULL
player. Each body was stock's own `return on NULL` early-out written down.

**They are `virtual`, which is why they had to be deleted rather than ignored:**
a declared virtual with no definition is an undefined reference at vtable
emission, so each cost a slot and a body for as long as it was declared. They
are gone from `game/client/prediction.h` and from the shim.
`shell_prediction.cpp` is **496 raw / 241 code → 485 raw / 221 code**;
`prediction.h` is **154 → 152 raw, 100 → 89 code**.

The other three — `InPrediction`, `IsFirstTimePredicted`,
`GetIncomingPacketNumber` — stay: compiled code calls them
(`shell_recipientfilter_client.cpp:221`, `gamemovement`'s replay bracket).

**THE PART WORTH CARRYING FORWARD.** `tools/shim-orphan-reach shell_prediction.o`
reports **23 of 29 definitions DEAD**, including `Init`, `Shutdown`, `Update`,
`PostEntityPacketReceived` and every view accessor — all of which the engine
calls every frame. They read dead because every call reaches them through
`prediction->`, which is a **virtual call and emits no symbol**. That is the
fourth way a symbol census has been wrong in this tree, and `shell_prediction.o`
is the clearest example of it in the client half.

The five removed here were settled a different way: by finding their only
caller (`prediction.cpp`) and confirming it is not on the client link line
(`grep -cE "objs/client_sourcesharp/prediction\.o:" <ninja>` → 0). **A DEAD row
from a symbol census is a question, not an answer, for any method on a class
whose vtable is emitted.**

---

## 3. The two linker-satisfaction files had drifted, and nothing said so

`shell_player_orphans.cpp` and `shell_client_orphans.cpp` exist only to satisfy
the linker: every definition in them is there because some still-compiled
translation unit references it. Both headers state the same rule —

> Found by removing the files and reading the linker, which is also how entries
> come off this list.

— and that rule has one failure mode, which is slow and silent: a definition's
caller is evicted by a later lane, the definition stays, and nothing anywhere
notices. **Both files had drifted, and between them they were holding
twenty-four definitions nothing imports.**

| file | defined | imported | deleted |
|---|---|---|---|
| `shell_player_orphans.cpp` | 26 | 5 | **21** |
| `shell_client_orphans.cpp` | 7 | 4 | **3** |

`shell_player_orphans.cpp` is **207 raw / 103 code → 133 raw / 20 code**.
§6 asked for "207 → ≈40".

### How it was measured, and the three ways it could have been wrong

```
tools/shim-orphan-reach --self-test
tools/shim-orphan-reach shell_player_orphans.o
tools/shim-orphan-reach shell_client_orphans.o
```

Over the 253 client objects the **ninja graph's build lines** name — never the
`objs/` directory, which keeps objects of translation units the build has
evicted and once inflated a consumer count by 13× — `nm -C -u` against
`nm -C --defined-only`, joined per symbol.

1. **A plain `nm --defined-only` would have been useless here.** These files are
   pages of one-line bodies and they pick up dozens of weak inline emissions
   from `cbase.h` — `Vector::Init`, `Vector2D::Vector2D`, `Vector4D::Vector4D`.
   Every one would read as an unimported definition of the file's own. Only
   `T`, `B` and `D` are counted.
2. **A sibling does not import its neighbour.** `EyePositionAndVectors` called
   `EyeVectors`, and a flat reading would have deleted the callee while keeping
   a caller that needed it — `util_shared_library`'s "130 dead lines" were 7 for
   exactly this reason. Here the two coincided, because the only internal edge
   ran between two symbols that were both dead, and that was checked rather
   than assumed.
3. **A virtual call emits no symbol, so the count is a FLOOR.** The only class
   whose members either file defines is `C_BasePlayer`, and `nm -C` over all
   253 objects emits **no `vtable for C_BasePlayer`**: every constructor left
   the build in 13c, so no instance exists and no virtual call can dispatch to
   one. That is what makes the floor exact, and it is pinned as its own fact —
   with a positive control on the same command shape (`vtable for
   C_StandaloneEntity` must be non-zero), so a reading of zero cannot come from
   an unbuilt tree or a glob that matched nothing.

### The rule now re-runs itself

`sourcesharp/test/orphan_reach_test.cpp` runs `tools/shim-orphan-reach` over
both files on every `buildcpptests`, and fails when either defines something
nothing imports. Three of its four facts exist because the first one can pass
for the wrong reason:

- **`TheToolActuallyFoundTheObjectsAndTheirSymbols`** — the main fact is
  satisfied by a tool that prints nothing and exits 0. This one requires a
  report of the graph it checked and at least one `LIVE` row.
- **`AnObjectTheGraphDoesNotNameIsRefusedRatherThanCalledDead`** — an unbuilt
  tree answers "nobody imports anything", which is indistinguishable from
  "delete this whole file". The tool exits **2**, not 0, and this proves that
  path is wired to the exit code.
- **`NoLinkedObjectEmitsAVtableForCBasePlayer`** — the floor-is-exact fact
  above, with its own positive control.

Mutation-verified (`.lane4a/mutate_orphan.sh`, `.lane4a/mutate_orphan2.sh`):
adding one unimported definition back reds the main fact; neutering the tool to
print nothing and exit 0 reds **both** controls and leaves the main fact green,
which is exactly the failure those controls exist to catch.
