# Command line

`tools/debug-api` talks to a running game, and launches one.

## Launching

```bash
tools/debug-api run               # the default mod (mods.yaml), no map, UI opened
tools/debug-api run dm_lockdown   # ...on a map
tools/debug-api run dm_lockdown +sv_cheats 1 -windowed
```

Starts the game with `-debugapi -debugapisocket -condebug -console -novid -insecure
+sv_lan 1`, waits for the API to come up, and opens the interface. Anything after
the map is passed straight through.

Each run gets its own `TMPDIR`, which is what lets a second game start at all —
the engine allows one instance per user and takes its lock there. The directory
is removed on a clean exit and kept, with its launch log, when something went
wrong.

The game is started through `bin/<config>/source#`, the launcher, which reads
`mods.yaml` beside it. `DEBUG_API_MOD` picks a different mod by its id (passed as
`-mod`), `SOURCESHARP_CONFIG=release` the release build, `DEBUG_API_UI_PORT` a
different port. `tools/debug-api launcher -help` runs the launcher with nothing but
the arguments given.

Quitting the game closes the UI, and closing the UI stops the game.

## The interface, against games already running

```bash
tools/debug-api ui        # 127.0.0.1:27110
tools/debug-api ui 8080
```

A browser cannot open a Unix domain socket, and the per-process socket is exactly
what lets several games run at once — so this listens on a normal TCP port and
forwards. Which instance a request means travels in the request itself rather
than being held by the proxy, so two browser tabs can watch two games without one
changing the other's mind.

`/_instances` is the only path it answers itself: the list of live games, which
no single instance could tell you.

A request that acts on a game and does not name one comes back `409` with that
same list attached, so a refusal is a listing with a reason on it rather than a
dead end that sends you to a second endpoint to find out what the choices were:

```json
{ "error": "name a game: this proxy does not choose one for you",
  "instances": [ { "pid": 12345, "ok": true, "map": "ss_sandbox", "address": "…" } ] }
```

The same answer comes back when the game a page names has gone, with the ones
still up in the list.

## Talking to a game

```bash
tools/debug-api list                    # which instances are up
tools/debug-api status
tools/debug-api log [since] [max]       # the C# log
tools/debug-api console [since] [max]   # the game's console
tools/debug-api cmd "sv_cheats 1"
tools/debug-api convar sv_gravity [value]  # any convar the engine knows, not only ours
tools/debug-api convars                 # what this mod declares
tools/debug-api convars native          # the engine's own, ours left out
tools/debug-api convars all             # both, each name once
tools/debug-api commands [managed|native|all]
tools/debug-api flags Cheat commands    # only what carries that flag
tools/debug-api classes
tools/debug-api instances
tools/debug-api ents item_healthkit     # "*" is every entity
```

## The live gate

```bash
make live-gate                      # build, then gate TWO loads of dm_lockdown
tools/live-gate --map dm_powerhouse --ents N --classes M
DEBUG_API_PID=12345 tools/live-gate --attach     # a game that is already up
```

`tools/debug-api ents "*"` on a freshly loaded map is the measurement every lane has
made by hand, and by itself it is not enough: it is ONE map load, and a thing
that outlives a level change is invisible until the next one. `tools/live-gate`
makes the reload part of the gate — it loads the map, measures, issues `map`
again, requires the process to survive, and requires the census to come back
*the same*. It also reads the log for a refused worldspawn and for the map's
decal pass, which are the two symptoms that appear before the crash does. Read
the head of the script; it says what each check is for.

`native` and `all` walk the engine's registry on the game thread, so they need a
game that is running frames; `managed` is a managed registry and answers from a
game sitting at a menu. `flags` filters what came back — the endpoints do not,
because the table is walked whole either way — and takes the listing and the
source after the flag: `tools/debug-api flags Archive convars all`.

## The entity debugger

The static half reads the compiled map rather than the game, so it answers with
no game running at all.

```bash
tools/debug-api map [name]              # what the map file declares
tools/debug-api mapents [name]          # its entities, with their connections
tools/debug-api events [since]          # spawns, removals, inputs, outputs
tools/debug-api fire plug_pull Trigger
tools/debug-api fire trigger_multiple Disable      # a classname works too
tools/debug-api fire "front door" Open "" 2
tools/debug-api connect fan_sensor OnGreaterThan fan_off1 PlaySound
```

`connect` is a cheat command underneath — `tools/debug-api cmd "sv_cheats 1"` first. So
is a DELAYED `fire`, which has to go through `ent_fire` because there is no way
to schedule an input from outside; an immediate one calls `AcceptInput` directly
and needs no cheats.

`fire` reads its target the way the console does: a TARGETNAME if anything
carries it, a CLASSNAME otherwise, and it addresses every entity that answers
rather than the first. The answer says which reading matched and how many were
reached:

```json
{ "ok": true, "via": "AcceptInput", "matchedBy": "classname",
  "targets": 47, "accepted": 47 }
```

`targets` is how many were sent it and `accepted` how many took it, because an
input an entity does not implement is the other thing that looks like nothing
happening. It used to resolve one entity by name and answer `no entity named`
for everything else, so a classname was refused against forty-seven live
triggers and six lights sharing a name had one of them lit.

Every field is quoted on the way to the console, so a targetname with a space in
it addresses the entity rather than the first word of its name.

## Choosing between instances

Always explicitly. `DEBUG_API_PID` names the game, even when only one is up:

```bash
tools/debug-api list                    # pid, map and address of each
DEBUG_API_PID=12345 tools/debug-api status
```

It used to fall through to the only live socket, and that read as a convenience
and behaved as a trap. A socket file outlives the process that made it, so the
one instance on the list is sometimes a game that exited half an hour ago —
still answering, with its last map and the build it was compiled from. Every
reading taken through it looks plausible and is about the wrong game. Naming one
costs a variable; not naming one has cost a wrong conclusion.

The web interface asks the same question for the same reason, with a picker that
appears whenever no game has been named — one click, remembered in the address.

With no sockets at all it falls back to `127.0.0.1:27100`, or `DEBUG_API_PORT`, and
says so on the way past. That is not a choice between games; it is the only
transport a game started without `-debugapisocket` has.
