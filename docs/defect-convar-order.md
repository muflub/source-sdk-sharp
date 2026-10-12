# `sv_gravity` reads 600 on one build and 800 on another

`plan_shim.md` §4a item 16. Diagnosed by lane 3-gravity, based on the plan_shim integration branch at
`5984a8f0b`.

**Verdict in one line: the defect as reported — *between builds, from the
relocation* — does not reproduce, and the commit it was reported on reads 600
for me three times out of three. What is real, and is the thing that actually
blocks the Phase 5 move, is a mechanism in the tree that can lose the value and
say nothing. It is named below.**

---

## 1. The first correction: 600 is not a declaration

The brief for this lane says "800 is the engine's own stock default; 600 is this
mod's intended value", and `plan_shim.md` says "something about registration
order at startup decides which default wins". Neither is what the tree does.

**Every line number in this document is at `5984a8f0b`, the commit this was
diagnosed on — which is the PARENT of the commit carrying the file, because the
commit carrying it moves some of those lines itself.**

* There is exactly **one** `sv_gravity` declaration in the whole build, and its
  default is **800**:

  ```
  $ grep -rn '"sv_gravity"' --include=*.cpp --include=*.h --include=*.cs . | grep -v /.git/
  src/sourcesharp/game/shared/movevars_shared.cpp:113:ConVar sv_gravity ( "sv_gravity", DEFAULT_GRAVITY_STRING, ...)
  src/sourcesharp/game/shared/shell_movement.cpp:212: SetEngineConVar( "sv_gravity", s_Movement.flGravity );
  ```

  `DEFAULT_GRAVITY_STRING` is `"800"` (movevars_shared.cpp:101), and the engine
  declares no `sv_gravity` of its own: the one registered `sv_gravity` in the
  live table below carries `GameDll`.

* **600 is a runtime write.** `Movement.Current` (SourceSharp.Shared/Movement.cs:46)
  says `Gravity = 600f`; `SourceSharp_RegisterMovementParams`
  (shell_movement.cpp) writes it into the ConVar **once**, during managed
  startup, through a `ConVarRef`.

So there is no contest between two declarations and no "which default wins".
**800 is what the ConVar holds whenever the runtime write does not survive, and
nothing in the process says that it did not.** Every explanation below is a way
for one write to be lost.

The live table agrees. `flags` and `default` are the engine's own:

```
$ curl --unix-socket <debug-api>.sock http://localhost/convars?source=all
{'name': 'sv_gravity', 'value': '600.000000', 'default': '800',
 'flags': 'GameDll, Notify, Replicated', 'source': 'native', 'registered': True}
```

`value` carries `%f` formatting and `default` does not, which is itself the
proof that the value was **written at runtime** rather than parsed from the
declaration: tier1's `ConVar::SetValue(float)` formats with `"%f"`, and its
early-out means a convar whose tuning equals its declaration keeps the
declaration's own string. Of the eight convars the movement write targets, only
two have a tuning that differs from the declaration — `sv_gravity` (600 vs 800)
and `sv_stopspeed` (75 vs 100) — and those two are exactly the two that show
`%f` formatting. The other six are invisible by construction.

## 2. It is not between-build, and it is not the relocation

`plan_shim.md` 4a item 16 records three runs of `9321f9f66` reading 800 against
`c053cefe3` reading 600, and concludes "whatever decides it is link or
static-init order". **`9321f9f66` reads 600 here, three times out of three.**

Every row below is one launch of `dm_lockdown` through this lane's own debug-api
socket directory, value read from `GET /convar?name=sv_gravity`:

| build | commit | mode | runs | `sv_gravity` |
|---|---|---|---|---|
| integration tip | `5984a8f0b` | debug   | 4 | `600.000000` ×4 |
| tip, again after the instrument landed | `5984a8f0b` + this commit | debug | 6 | `600.000000` ×6 |
| the commit 4a item 16 names | `9321f9f66` | debug   | 3 | `600.000000` ×3 |
| the same commit, other mode | `9321f9f66` | release | 3 | `600.000000` ×3 |
| first load / `map` reload / `changelevel`, one process | `9321f9f66` | release | 1×3 reads | `600.000000` ×3 |
| two concurrent instances, each read on its own socket | `9321f9f66` | release | 2 | `600.000000` ×2 |

**21 readings, 5 builds, 2 commits, 2 build modes, 0 at 800.** (The
second-instance row's own `/status` reply names the build it answered for —
`"commit":"9321f9f6"`, `(detached)@9321f9f6` — because a reading whose instance
is not named is the trap `debug-api`'s own header warns about.) The relocation
commit moved the `$File` row from line 228 to line 187 of
`server_csharp.vpc` — a real change of link order — and that row is *still* in
the tip I measured, so the tip and the "800 build" are the same arrangement.

Two hypotheses this kills outright, and the second one with a number rather
than a shrug — a handful of green runs is not absence, so here is the trial
count and the regime. If a run of this build read 800 with probability `p`, then
21 consecutive 600s has probability `(1-p)^21`:

| `p` | P(21 runs all read 600) |
|---|---|
| 0.5 — the regime item 16 reports (3 of 3) | 0.0000005 |
| 0.2 | 0.009 |
| 0.1 | 0.11 |

So the rate **in the regime item 16 measured in** is excluded, and so is
`p = 0.2` at better than 99%; a rarer intermittent at `p ≤ 0.1` is not, and I am
not claiming it is.

* **not** link or static-init order — the ordering under test is present in both
  builds, and both read 600;
* **not** non-determinism at the rate originally seen, per the table above.

What it cannot kill is a confound in the original measurement. Item 16's runs
were taken in the same commit that ran a **two-process** replication experiment,
where `sv_gravity` was the positive control and was read *on the client* both
before and after a set. A reading taken from the wrong instance — or from a
tree whose managed half had not been deployed, which leaves the write never
executed at all — reads 800 stably and looks exactly like this. I have no
access to those logs, so I cannot convict a particular confound; I can only say
that the code at that commit does not produce 800 for me.

## 3. The mechanism that CAN lose the write, and is in the tree

This is the part that matters, because it is what blocks the 29 `sv_*`
declarations regardless of what lane 2 measured.

### 3.1 A replicated ConVar registered late slams the value back to *its* default

`CShellServerGameDLL::RegisterConCommandBase` (shell_gamedll.cpp:1437-1475) is
stock's `CGameDLL_ConVarAccessor` (game.cpp:60-98) transliterated, and it ends:

```cpp
    else if (bReplicated && pDefault && !pCommand->IsCommand())
    {
        ((ConVar *)pCommand)->SetValue(pDefault);   // pDefault = THIS declaration's default
    }
```

`ConVar::SetValue` writes through `m_pParent` (tier1/convar.cpp:1047-1050), and
registering a name that already exists (`ICvar::RegisterConCommand`) points
`m_pParent` at the *existing* object. So a **second** replicated declaration of
a name, registered through this accessor, **overwrites whatever the first one is
currently holding with its own declared default.**

Two further facts make that reachable after managed startup:

* `ConVar_Register` sets `ConCommandBase::s_pAccessor` **for the lifetime of the
  process** (tier1/convar.cpp:71). Every ConVar constructed later — every
  managed `[ConVar]`, built by `CSourceSharpConVar` — goes through
  `CreateBase` → `Init()` → **this accessor** (tier1/convar.cpp:184-188), not
  through the static list.
* Which accessor is live changed in Phase 1d. `shell_gamedll_convars.cpp`'s own
  banner records it: game.cpp's `ConVar_Register` used to run first and
  tier1/convar.cpp:63 made the shell's call a no-op; deleting game.cpp's row
  inverted that, and the shell's override became the live one.

**Why the tree is safe today, in one ordering:**

```
console.log, one launch of dm_lockdown at 5984a8f0b
   83  [SourceSharp] C# registered convar 'mp_forcerespawn' = '1' (id 33)   <- managed RegisterAll
  626  [SourceSharp] movement: jump 21u, gravity 600, ...                   <- the write
  729  For FCVAR_REPLICATED, ConVar must be defined in client and game .dlls (sv_hudhint_sound)
  ...  (five of these)                                                       <- the CLIENT dll registering
  940  [SourceSharp] movement(client): predicted params registered
```

The server DLL initialises **before** the client DLL, every managed declaration
is registered before the movement write, and the client's
`ConVar_Register( FCVAR_CLIENTDLL )` passes **no accessor** (shell_clientdll.cpp:390-393
states this divergence explicitly; stock's `cdll_client_int.cpp:987` does the
same), so the client's duplicate registration cannot slam anything.

**Three changes that would each break it, none of them obviously about gravity:**

1. giving the client's `ConVar_Register` an accessor "to match stock";
2. any managed `[ConVar]` that is `Replicated` and is declared or re-registered
   *after* `RegisterMovementParams` — the shell's accessor runs for it, and if
   the name already exists it slams the parent;
3. moving `movevars_shared.cpp`'s 29 to managed, which is §4a item 16's actual
   subject — see 3.2.

### 3.2 A managed replicated ConVar does not link to the client's native one

The engine refuses the link when the child is `FCVAR_CLIENTDLL` and the parent
is not `FCVAR_GAMEDLL`, and prints

```
For FCVAR_REPLICATED, ConVar must be defined in client and game .dlls (%s)
```

A managed declaration carries **neither** flag: `ConVar_Register` ORs
`s_nCVarFlag` only into convars that already exist when it runs
(tier1/convar.cpp:76-78), and a managed ConVar is built afterwards. So a managed
server-side replicated declaration becomes the **parent**, and the client's
native declaration of the same name is refused the link — `m_pParent` is never
assigned, and the child is not put in the engine's command list either. **It is
an orphan holding its own declared default forever.**

That is not theory; it happens five times on every run today:

```
$ grep -c "For FCVAR_REPLICATED" console.log
5
  sv_hudhint_sound  mp_fadetoblack  mp_friendlyfire  mp_allowspectators  mp_forcecamera
```

Those five are exactly what `GameVars.cs` declares. They are **harmless today**
only because nothing on this build's client link line reads them. That has two
halves, and both are checkable. `gamevars_shared.cpp`'s own only reader is
`MPForceCameraCallback`, which is `#ifdef GAME_DLL` (gamevars_shared.cpp:12-20)
while the file itself is on the **client** script and not the server one
(`client_csharp.vpc:243`, no matching row in `server_csharp.vpc`) -- so inside
the TU the reader is compiled out. And outside it, stock's readers are absent
from the file list:

```
$ grep -n "c_baseplayer.cpp\|hud_hintdisplay.cpp" src/game/client/client_csharp.vpc
(no output)
$ nm -C --defined-only game/mod_sharp/bin/linux64/client.so | grep -E ' [bB] (mp_forcecamera|sv_hudhint_sound)'
mp_forcecamera
sv_hudhint_sound          # the objects exist, from gamevars_shared.cpp; nothing reads them
```

**`movevars_shared.cpp` is not in that position.** Its client half is read by its
own `GetCurrentGravity()`, which is `sv_gravity.GetFloat()` on the file-scope
object. Move the server's 29 to managed and the client's 29 become orphans:
the server would read 600 and the client's predicted movement loop would read
800, on the same machine, in the same process, with no error — which is the
prediction-bug shape this project already has a name for. That, and not link
order, is the real content of "do not move a ConVar that depends on it until it
is understood".

This is also the explanation for the 5→29 warning count item 16 records: adding
`FCVAR_GAMEDLL` to managed registrations silences the five by making the managed
parent a legal partner for a `FCVAR_CLIENTDLL` child, and the measurement that
went to 29 was taken on the build where the 29 were already managed.

### 3.3 Nothing observed the window

`SourceSharp_OnConVarChanged` (sourcesharp_gamesystem.cpp) returned early on
`!g_bSourceSharpLoaded`, which is true for part of exactly the window in
question, and no other observer existed. So a reading of 800 carried **no
information about which write produced it** — which is why item 16 could only
say "something between the write and the first frame".

## 4. Which ConVars are affected

Every ConVar in the process, on a live `dm_lockdown` at `5984a8f0b`, compared
against its own declared default (`GET /convars?source=all`, 1,369 rows):

| convar | default | runtime | cause |
|---|---|---|---|
| `sv_gravity` | 800 | 600.000000 | the movement write — intended |
| `sv_stopspeed` | 100 | 75.000000 | the movement write — intended |
| `sv_skyname` | sky_urb01 | sky_day02_09 | the map — intended |
| `sv_lan` | 0 | 1 | `+sv_lan 1` on my launch |
| `sv_tags` | (empty) | `gravity` | the **engine**, tagging a non-default gameplay convar |
| `cs_sv_greeting` | hello | howdy | `game/mod_sharp/cfg/config.cfg:65` |
| `g_ragdoll_maxcount` | 8 | 4 | `GameRagdollManager.cs:88` executes `g_ragdoll_maxcount 4` |
| `mp_forcerespawn` | 1 | 0 | `game/mod_sharp/cfg/settings.scr:107-111` declares the dialog default as 0 |

38 rows differ in total; the remaining 30 are engine and material-system
convars (`r_decals`, `mat_software_aa_strength`, …) with no mod declaration.

**`mp_forcerespawn` is the one that looked like a second instance of this defect
and is not.** The engine's own echo confirms the value is really 0 and not a
managed-cache artefact:

```
$ ./debug-api cmd mp_forcerespawn    # reply lands in console.log
"mp_forcerespawn" = "0" ( def. "1" )
$ grep -n "convar '" console.log | grep changed    # sv_sourcesharp_verbose 2
[SourceSharp] convar 'cs_sv_greeting' changed -> 'howdy'
[SourceSharp] convar 'mp_forcerespawn' changed -> '0'
```

Both changes happen at the config-exec point, and both have a file on disk
behind them. `settings.scr` is a **name-reached** writer that no link-line census
can see, and stock has the same 1-vs-0 disagreement between `game.cpp:37` and
its own `settings.scr` — so this is inherited, not a port regression.

**Conclusion for §4a item 16's scope: gravity is not one of several.** No ConVar
in this build is taking a value from the mechanism in §3. The eight the movement
write targets are the only ones exposed to it, and six of those are invisible
because their tuning equals their declaration.

## 5. What this lane changed

The cause I can establish is "a runtime write with no observer, in a process
that has a documented way to undo it". The minimal change that implies is the
observer, which is what §4a item 16 itself asks for — *"the instrument has to be
native and placed before that guard"*.

* `src/sourcesharp/game/shared/shell_movement_audit.{h,cpp}` — the decision, in
  a translation unit with no engine in it so a gtest can compile it: given what
  the mod registered, what the declaration says, and what the convar reads now,
  is this **as registered**, **back at the declared default** (the defect), or
  **overridden** (a config or a console, which is what the write is a default
  *for*).
* `shell_movement.cpp` — remembers what each of the eight was set to, and on any
  later change asks the classifier and says so. The defect verdict is a
  `Warning` naming all three numbers; an override is one `Msg`; "nothing moved"
  is silent, because six of the eight would otherwise print every run.
* `sourcesharp_gamesystem.cpp` — the call, **above** the `!g_bSourceSharpLoaded`
  early-out.
* `src/sourcesharp/test/movement_audit_test.cpp` — 11 gtests, including the two
  orderings that make the decision non-obvious (a tuning that equals its
  declaration, and the `"%f"` round trip). Both were mutation-verified red.

**What it deliberately does not do.** It does not add `FCVAR_GAMEDLL` to managed
registrations (measured worse, twice now, and §3.2 says why the silence would be
bought with a broken link on the client's side). It does not change any
registration order. A run that reads 800 with **no line from this function** is
itself evidence: it means the value was decided before the change callback was
installed, which is a different fault.

## 6. What is not settled, and the experiment that would settle it

I could not reproduce 800. The instrument above would have named the writer if I
had, and it is now in the tree — but the honest state is:

* **Unsettled:** what produced item 16's three 800 readings. Ruled out: the
  commit, the link order it changed, the build mode, a map reload, a
  `changelevel`, a second concurrent instance, and non-determinism within a
  build (15 runs).
* **The experiment that would settle it:** re-run the two-process join recipe
  from `9321f9f66`'s commit message with **this** build, reading `sv_gravity`
  from each process's own debug-api socket with `DEBUG_API_PID` named explicitly on
  every read, and keeping both `console.log`s. If the 800 was the client
  instance answering, it appears immediately and this document's §2 becomes a
  measurement error rather than an open question. Memory says the Steam ticket
  fails with several instances and the client never joins — which is precisely
  the state in which an unjoined client reads its own declared 800 — so that
  experiment must count the join on the **server** before reading anything.

## 7. What Phase 5 should do with this

The block on moving the 29 is **§3.2, not §2**. Moving the server's
declarations to managed without moving the client's makes the client's copies
orphans at 800 while the server is at 600, silently. Either move both halves in
the same commit, or leave the declaration native and keep the managed value as
the runtime write it already is. The 5 warnings printed on every run today are
the same mechanism already running, and the only reason they are harmless is
that nothing on this build's client link line reads those five names — which is
a property of the current file list, not a guarantee.
