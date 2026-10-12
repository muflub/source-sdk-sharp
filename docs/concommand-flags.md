# The FCVAR word on a managed console command

`SourceSharpEngineApi::RegisterConCommand` took a name and a help string and
nothing else until **ABI 250** (lane `6b-abiappends`). A C#-declared console
command was therefore always an **unflagged** one, whatever it was replacing.

Two consequences, and only the first was written down anywhere.

## 1. Thirteen native commands could not be declared managed at all

`docs/convar-ledger.tsv` held thirteen rows at `deferred-abi` naming this gap.
The slot carries a word now, and **none of the thirteen moved** — each is
blocked on a further crossing its row never named. The ledger's evidence cells
were re-measured at ABI 250 and now say which one; `concommand_flags_ledger_test`
refuses a row that goes back to blaming the flag word.

## 2. Forty-four managed commands are registered unflagged, and their stock
##    twins are `FCVAR_CHEAT`

This is the one nobody had written down, and it is live rather than deferred.
Joining the 132 `[ConCommand]` declarations under `src/sourcesharp/managed`
against the stock declaration of the same name under `src/game` (outside
`sourcesharp`) gives 51 names in common, and **44 of them carry `FCVAR_CHEAT`
in stock**:

```
buddha              cast_hull           cast_ray            drawcross
drawline            ent_absbox          ent_attachments     ent_autoaim
ent_bbox            ent_call            ent_cancelpendingentfires
ent_create*         ent_dump            ent_info            ent_keyvalue
ent_messages        ent_name            ent_pivot           ent_rbox
ent_remove          ent_rotate          ent_script_dump     ent_setname
ent_show_response_criteria              ent_text            ent_viewoffset
explodevector       god                 hurtme              killvector
noclip              notarget            particle_test_start particle_test_stop
prop_dynamic_create prop_physics_create setang              setang_exact
setmodel            setpos              setpos_exact        test_dispatcheffect
test_entity_blocker bug_swap
```

`*` stock declares `FCVAR_GAMEDLL | FCVAR_CHEAT`. The DLL bit is **not** to be
declared in managed code — `SourceSharp_EngineRegisterConCommand` ORs the side's
own bit, because this assembly is loaded into both DLLs and a value chosen
managed-side would be one value for both.

`ent_fire` is a 45th and does not appear in the join only because its stock
block has already been deleted under `SOURCESHARP`; see
`game/server/baseentity.cpp:5822`, which records that it was
`FCVAR_GAMEDLL | FCVAR_CHEAT`.

Seven more overlap and are unflagged in stock too, so they are already right:
`explode`, `fov`, `give`, `kill`, `say`, `say_team`, `soundscape_flush`.

### Why this lane did not flip them

It is a **behaviour change to 44 commands at once** and the thing it changes is
whether they run at `sv_cheats 0`. Every live gate and probe in `tools/` drives
some of these by name; most set `sv_cheats 1` first and some do so at join
(`live-gate`'s `JOIN_CONVARS`), but "most" is not a measurement, and the only
instrument that settles it is a live-gate run, which this lane does not own.
Landing an unverifiable behaviour change into the thing the gate measures is
how a gate stops meaning anything.

So the capability is landed and the flip is owed, with this list as its
worklist. The lane that takes it should:

1. set `Flags = ConVarFlags.Cheat` on each declaration above,
2. run the live gate, and
3. for every tool in `tools/` that drives one of these by name, check that
   `sv_cheats 1` precedes it — `tools/movemath-gate:558` is the one that
   already writes down why ("a cheat command at sv_cheats 0 reports success").

The join is reproducible: match `[ConCommand("<name>"` in
`src/sourcesharp/managed/**/*.cs` against `CON_COMMAND_F(<name>,` and
`ConCommand <var>("<name>",` in `src/game/**` outside `sourcesharp`, and read
the argument after the help string.
