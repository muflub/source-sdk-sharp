# HTTP API

Everything the web interface uses, and everything the `debug-api` script wraps. The
UI is a client of this and nothing more — there is no endpoint it needs that is
not listed here.

`GET /` returns this list from the running game.

Behind the proxy every request also carries `_pid`, naming which game it means.
One that does not comes back `409` with the live instances attached — the proxy
never picks a game for you, not even when only one is running.

## Reading

| Endpoint | |
| --- | --- |
| `GET /status` | Map, time, tick count, pid, and which build the game is running |
| `GET /log?since=N&max=M` | Managed log lines at or after sequence `N` |
| `GET /console?since=N&max=M` | The game's own console, as `-condebug` writes it: the mod's `console.log`, or the file `+con_logfile` moved it to. Followed from when the dev API started, 65,536 lines deep. `epoch` names the server that answered: a managed reload replaces it, and its sequences start again |
| `GET /convars?source=X` | Convars: `managed` (default) for this mod's, `native` for the engine's *minus* this mod's, `all` for both with each name once |
| `GET /commands?source=X` | Console commands, with the same three sources |
| `GET /entityclasses` | Declared C# entity classes, with inputs, outputs and keys |
| `GET /instances` | Live scripted entities, with their current values |
| `GET /entities?class=X` | Live entities of a class. `*` matches every entity |
| `GET /entity?index=I` (or `handle=H`, or `name=N`) | One entity: handle, index, classname, name, health, origin, angles, `velocity`, `speed`, `asleep` (the last wake or sleep its physics object reported; `null` when it never reported one) and `ground` (the index a player stands on, `-1` for nothing, `null` for a non-player), with the server `tick` it was read at. `found:false` when nothing live matches |
| `GET /map?map=X` | The map file: entity count, connection count, and what else is on disk |
| `GET /map/entities?map=X` | Its entities, keyvalues and I/O connections |
| `GET /map/geometry?map=X` | World surfaces and brush model bounds, for drawing |
| `GET /events?since=N&max=M` | Entity events: spawns, removals, inputs, outputs. Filtered by the game with `kind` (comma-separated: `spawned`, `removed`, `input`, `output`, `level`), `name` and `classname` (case-insensitive globs), `handle` and `index`. An unknown kind is an error, not an empty answer |
| `GET /counters?group=G` | Counter groups as numbers by name, read from what the console readouts print: `physics` (`cs_sv_physics_events`: events, `seen.<Kind>` for every kind, responded, updatesDelivered, scrapes, stress, shadowMoves...) `playershadow` (`cs_sv_playershadow`), and `think`: the think heap's bookings by `booked.<classname>` (or `<classname>:<context>`), their `total`, and `dueWithin.N` for N = 1, 2, 5, 10, 30 seconds of game. Every group when none is named; `available:false` when the source is absent |
| `GET /events/head` | The next sequence of the event ring, the managed log and the console, with no game thread |
| `GET /ui` | The web interface |
| `GET /ui/stamp` | What build of the interface is being served, and whether it can change under the page |

## Waiting

`GET /wait` holds the request until a condition is true, checked by the game
once per **server tick**, and answers when it is met, when its tick budget runs
out, or at a wall-clock cap. The game thread never blocks: the check is queued
and run once a tick inside its think, and the request waits on its own thread
(a waiting request does not hold any other request up).

Exactly one condition per request:

| Condition | Met when |
| --- | --- |
| `ticks=N` | N server ticks have passed. Its budget is N unless `timeout` is given |
| `field=F` with `index`, `handle` or `name`, `op` and `value` | The entity's reading compares true. `F` is `origin.x/y/z`, `angles.x/y/z`, `velocity.x/y/z`, `speed`, `health`, `asleep`, `ground`, `classname`, `name`, or a networked payload field by name. `op` is `eq` (default; numbers within `eps`, default 0.001), `ne`, `lt`, `le`, `gt`, `ge` or `changed` (against the first reading) |
| `exists=1` with `index`, `handle`, `name` or `class` | Something live matches |
| `gone=1` with `index`, `handle` or `name` | The entity it named at the start is no longer live (met at once, saying `absent at the start`, when nothing matched) |
| `event=KIND` (or `*`), with `/events`' `name`, `classname`, `handle`, `index` filters and `since` | An event matches at or after `since` (default: the head when the wait was made) |
| `settled=1` with `index`, `handle` or `name` | Its speed stayed at or under `eps` (default 1 u/s) for `for` consecutive ticks (default 5), or its physics object reports asleep; with `asleep=1`, only the sleep counts |
| `census=N` with `class` (default every entity) and `op` (default `ge`) | The count of live entities compares true |
| `io=empty` | The entity I/O queue has nothing pending |

`timeout` is the budget in server ticks (default 330, about 5 s at the 15 ms
tick). `wall` is the wall-clock cap in seconds (default ten times the budget at
full speed, at least 30, at most 600): a safety net for a game that stopped
ticking, not a timer. A condition already true is met with `waitedTicks` 0.

The reply: `ok`, `status` (`met`; `timeout` and `wallclock` answer 408;
`failed`, for an entity removed before its condition held, and `stuck`, for a
settle-teleported player that neither falls nor stands for 20 ticks (put in
solid, or no usercmds moving it), answer 409;
`cancelled`, for a game shutting down or reloading, answers 503), `condition`,
`startTick`, `tick` (when it ended), `waitedTicks`, `timeoutTicks`, `wallMs`,
`seq` (the matching event's sequence for an event wait, else the event head
when it ended) and `value` (what was read last). A malformed wait answers 400.

## Writing

| Endpoint | |
| --- | --- |
| `POST /command` | Run a console command. The body is the command |
| `POST /command?capture=1` | The same, answering `captured`: `console`, the console lines the command printed (engine and managed; with the in-process feed attached, every line whose console sequence falls between the command's start and end, from any thread, since console-batch (2026-10-11); without it, as `console.log` has them, scoped by two markers echoed around the command), `log`, the managed log lines it printed, `complete` (the closing marker was read) and `source`. Without `-condebug` it says it saw the managed log only |
| `GET /convar?name=X&value=Y` | Read a convar, or set it when `value` is present. Any convar the engine knows, not only this mod's |
| `GET /entity/input?target&input&param&delay` | Fire an input at an entity |
| `GET /entity/output?entity&output&map` | Fire everything an output connects to |
| `GET /entity/connect?source&output&target&input&param&delay&times` | Wire two live entities together |
| `POST /map/save?map=X&as=Y` | Write an edited entity list back, to the same map or a new one |
| `POST /entities/spawn` | Create a saved group, at its own origins or where a player looks. Each entity answers its `index` and `handle`, and the `origin` and `angles` read back after placement |
| `GET /entities/remove?name=G` (or `class=G`, `index=I`, `handle=H`) | Remove what matches (names and classes are globs; never the world or a player) and answer once every one is gone, counted in ticks (`timeout`, default 30). The reply is a wait's (see Waiting) plus `removeTick`, `removed` and the `entities` removed; `nothing matched` answers at once |
| `GET /player/teleport?player=N&pos=x,y,z&ang=p,y,r` | `setpos_exact`'s teleport (the physics shadow moves with the player) and `setang`'s view snap, without cheats. With `settle=1` it answers once the player stands on something at or under `eps` (default 1 u/s) for `for` ticks (default 3), as a wait (see Waiting) whose `value` carries the final `origin`, `speed` and `ground` |
| `GET /player/aim?player=N` | Where a player is looking, and what they are looking at |

An absent `value` on `/convar` means "just read it"; a present but empty one
means "set it to nothing".

## Two things worth knowing

**Which build answered.** `/status` carries a `build` block: the ABI version,
and a stamp for each half of the mod. `hash` is over the bytes that were
*loaded* — not a commit, which would be wrong about every build made from a tree
with uncommitted work in it, and this tree's normal state is uncommitted work.
`at` is when those bytes were written, `branch`, `commit` and `dirty` say which
source they came from, and `freshness` is `stale` when something has been built
since. `dirty` is three-valued: `null` means git could not be asked, and does
not mean clean.

There are two halves because `cs_sv_reload` replaces the managed one without
restarting the process. `native` and `managed` disagreeing is the normal reading
after a reload, not a fault — and is exactly what the whole block exists to make
visible.

**Sequence numbers.** `/log`, `/console` and `/events` are rings with a sequence
per entry. Ask for what is newer than the last number seen and the response
carries only that, plus a `next` to use on the following poll. A reader further
behind than the ring is deep gets what is left — losing the oldest entries is
what a ring is for.

**The game thread.** The engine is single-threaded and these requests arrive on
another thread, so anything touching the game is queued and run on the game's own
think, with the request waiting. Everything else — the log, the map file, the
convar and command listings — answers immediately and cannot stall a frame. That
is deliberate: a debugger you cannot open while the game is wedged is a debugger
missing its best moment.

Every write — commands, inputs, outputs, connections, damage, spawns — answers
with `logFrom`, `eventsFrom` and `consoleFrom`: where in the managed log, the
event ring and the console that call's own effects begin, read before it ran.
`/events?since=<eventsFrom>` is exactly what the call caused and what followed.

`/events` answers `next` (where this reader continues: the first match it did
not answer when `max` cut it short, else the head) and `head` (the ring's end).

## How a game ended

`/status` carries `quit`: `null` until something asks the process to end, then
`{reason, detail}` as the exit record would give it at that moment.

On any shutdown the game writes `<DEBUG_API_DIR>/<pid>.exit.json` (default
`/tmp/sourcesharp-debug-api/<pid>.exit.json`): `reason`, its `detail`, `map`,
`tick`, the native recorders' `facts`, and the last 20 `recentCommands` sent
through `/command`. `tools/debug-api exit <pid>` prints it. A managed reload
writes one too, and the next module removes it at start: the process did not
end, so the record left behind is always the process's last.

| `reason` | Meaning | `detail` |
| --- | --- | --- |
| `api-command` | `quit` or `exit` sent through `/command` | `command`, `requestSeq` |
| `signal` | SIGTERM, SIGINT or SIGHUP, from another process or a terminal | `signo`, `signal`, `senderPid`, `senderUid`, `sender` (its command line, when it is still alive) |
| `window-close` | The window was closed | `atMs` |
| `console` | `quit` typed at the game's console (its `] quit` echo) | `line` |
| `sdl-quit` | SDL delivered a quit with none of the above seen | `atMs` |
| `fatal` | A failed assert or `Error()`: written by native code, `writtenBy: "native fatal report"`, with `kind` and `message`; the process exits 253 | |
| `shutdown` | Nothing recorded asking for it | |

How it is decided: native code only records (a SIGTERM/SIGINT/SIGHUP handler
notes the sender from `SA_SIGINFO` and then runs the handler that was there; an
SDL event watch counts quit and window-close events; the fatal path writes its
own record). The earliest of a dev API quit, a signal and a window close is the
reason, and the later ones are its consequences (SDL turns SIGTERM into a quit
event). Without any of those, a typed quit, then an SDL quit, then `shutdown`.

## Where it listens

With `-debugapisocket`, on a Unix socket named for the process:
`/tmp/sourcesharp-debug-api/<pid>.sock`. That is what lets several games be driven
at once — a fixed TCP port cannot, because the second game to start silently
loses the bind and every request goes to the first, which looks exactly like a
change that did not take effect.

Without it, on `127.0.0.1:27100`, or the port given by `-debugapiport`.

There is no authentication, because there is nothing remote to authenticate.

A request may be up to 16MB. That is sized for the largest thing anyone sends,
which is a whole map's entity list on its way to being written back.
