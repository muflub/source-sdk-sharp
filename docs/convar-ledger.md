# The ConVar ledger

`docs/convar-ledger.tsv` is the ruling on every `ConVar`, `ConCommand` and
`CON_COMMAND` declared natively under `src/sourcesharp/`. One row per name.
`src/sourcesharp/test/convar_ledger_test.cpp` pins it against the tree in both
directions, so it is a gate rather than a document.

The rule it exists to enforce is `plan_entity.md` §6's:

> No ConVar's default or semantics is declared natively unless the engine
> itself transmits or resolves the name.

## Columns

| column | what it is |
|---|---|
| `name` | the console name a player, a config or a tool types |
| `kind` | `ConVar`, `ConCommand`, `CON_COMMAND` or `CON_COMMAND_F` |
| `side` | `server`, `client` or `shared`, from the declaring file's directory |
| `file`, `line` | where the declaration is, repo-relative |
| `flags` | the `FCVAR_*` words in the declaration, `-` for none |
| `verdict` | the ruling — see below |
| `native_now` | **the fact**: is a native declaration of this name expected in the tree at this commit |
| `live_cpp_readers` | how many translation units **on a link line** name it, excluding the declaring file |
| `header_externs` | how many headers under `src/game` or `src/sourcesharp` name it |
| `reader_files` | which live translation units those are |
| `evidence` | why the verdict is what it is |

**`verdict` and `native_now` are deliberately separate.** The verdict is the
ruling — "this ought to be managed". Several rows carry a ruling whose move has
not landed, because it is blocked on something this phase may not do. A gate
that pinned the ruling would be red for work that is correctly not done yet, so
the gtest pins `native_now`, which is a fact about the tree. Neither column can
quietly become the other.

## Verdicts

| verdict | meaning |
|---|---|
| `protocol-keep` | the shipped engine itself declares, resolves or queries the name. The one legitimate reason to keep a declaration in C++ |
| `managed` | moved; the declaration is a `[ConVar]` / `[ConCommand]` class |
| `managed-queued` | ruled managed, move not landed in this commit |
| `native-required` | a ConCommand whose body reads native-only state; moving it needs a managed→native surface |
| `reader-owed` | ruled managed, blocked on converting named `extern ConVar` readers to `CManagedConVarRef` |
| `deferred-abi` | ruled managed, blocked on an ABI addition |
| `deferred-lane` | declared in a file another lane holds open |
| `delete`, `dead-already` | as measured |

## How a keep is justified

A `protocol-keep` row has to name what resolves the name engine-side, and the
gtest requires the evidence to do so. The instrument is the shipped engine
binary's own string table:

```
strings -a "$HOME/.steam/steam/steamapps/common/Source SDK Base 2013 Multiplayer/bin/linux64/engine.so"
```

**Bare presence is not the test** — `skill`, `developer` and `use` are ordinary
English words and appear for unrelated reasons. What each keep rests on is the
*neighbourhood*: `skill` sits immediately after `Game skill level (1-3).` and
beside `deathmatch` and `coop`, which is the engine declaring it; `sv_skyname`
sits beside `ProcessServerInfo`; `closecaption` and `cl_team` sit inside the
engine's client-convar query block beside `cl_predict` and `cl_lagcompensation`.

Measured on this branch, only **six** of the 113 names in Phase 5's scope appear
in `engine.so` at all. The other protocol route is that the engine *transmits*
the name — `FCVAR_USERINFO` and `FCVAR_REPLICATED` — and the engine does that
**by flag, not by name**, so a managed declaration carrying the flag is
transmitted exactly as a native one is. That is why the transmitted-but-not-
resolved names are rulings to move rather than keeps.

## The ordering experiment

The one thing about a move that cannot be settled by reading the tree is whether
`ConVarRegistry.RegisterAll` runs before anything resolves the name. It was run
for real for the fourteen names Phase 5 moved, with both controls
discriminating, and `docs/convar-ordering-experiment.md` records what a wrong
ordering would have looked like and what was actually seen.

## What a move must not break

A ConVar or ConCommand is reached **by name**, which no link-line census can
see. Before a name moves, sweep `tools/`, `docs/`, `game/mod_sharp/cfg` (in
particular `settings.scr` and `config.cfg`), the managed tree and the native
gtests. The name itself survives a move — `Engine.RegisterConVar` and
`Engine.RegisterConCommand` construct a real native object in the engine's
registry — so the risk is **ordering**, not existence: a `ConVarRef` built
before `ConVarRegistry.RegisterAll` runs caches the miss forever and then reads
as zero. `src/sourcesharp/game/shared/managed_convar_ref.h`'s
`CManagedConVarRef` is the native-side answer; it retries until it resolves.
