# client.so T6: injection, not lookup

**Status:** in progress (user 2026-11-01: "do the same for client.so"), lane
`../ss-t6` branch `t6-client`. C0 and the first C1 steps landed on main
`ad7e68a59` (2026-11-01): client locator_calls 264 -> 183, engine_global_reads
860 -> 845, link_level 566 -> 550, unruled 512 -> 499, MEMBER 12 -> 9.
Reviewed and approved by the user 2026-11-01.

The server's T6 part 3 (plan_server_native_v2 §3, landed by 2026-10-27) took
server.so to classes whose dependencies are handed to them by constructor,
owned by the game DLL object, with no free function finding state through a
global (`globals_locator_calls` 101 -> 0, no MEMBER row left). This plan does
the same for client.so.

## Where the client starts (measured at 6ba2676f4, debug)

| Instrument | Count |
|---|---|
| `tools/globals-census --side client summary`: link_level | 566 |
| client ledger UNRULED / MEMBER / FLOOR / DEAD / CONST | 512 / 12 / 21 / 19 / 2 |
| objects with a static initialiser | 64 |
| free accessor calls (grep: `Managed*Client()`, `ShimConsole()`, `ShellClientDll_*()`, `ShimClientAreasOfRoot()`, `SourceSharp_ClientSlotTable()`, `SourceSharpClientEntityList()`, `BoundaryProbes()`) | ~400 |
| reads of stock engine globals (`render`, `engine`, `gpGlobals`, `modelinfo`, `materials`, `g_pClientMode`, `shadowmgr`, `input`, `gHUD`, ...) | ~700 |

The client links only tier1, mathlib and libss_particles beside its own
objects, and vendor headers use these globals in a handful of inlines (and
vendor headers are editable inside the engine barrier). So almost all of it can
be handed to its users; FLOOR is what an archive or the engine reaches by
symbol, or static-init registration.

## Phases (one commit per step, as on the server)

- **C0. The instrument.** `tools/globals-census` counts client locator calls
  (the accessor names above, matched whole in code relocations), with a
  `client_globals_locator_calls` ratchet. It only falls.
- **C1. The managed areas.** `tools/managed-areas` stops emitting the client's
  `Managed<Area>Client()` accessors; what called one is handed the holders
  (the client DLL object owns them) or the area it calls. As server G4.
- **C2. The engine interfaces.** A `CClientEngineInterfaces` on the client DLL
  object's DLL tier, acquired in Init (as the server's), replaces the stock
  interface globals that no archive needs; subsystems take the interfaces they
  call by constructor. Globals an archive reaches by symbol stay, ruled FLOOR
  with that reason.
- **C3. The client's subsystems.** Each file-scope singleton (the effect,
  beam, sprite, rope, temp-entity, particle, shadow, leaf, view-render,
  HUD/mode and prediction objects) becomes a member of the client DLL tier (or
  the level tier where it is per map), built with what it calls.
- **C4. ConVars and commands.** Native client convars and commands follow the
  server's split: those static-init registration needs stay FLOOR with the
  chain named; those owned by a subsystem become its members.
- **C5. Function-local caches and warn-once latches** become members of the
  object whose method they live in.
- **C6. Rule the rest.** Every row left is DEAD, CONST or FLOOR with a reason;
  `client_globals_unruled` reaches 0.

Each phase lands after `make check` and `tools/windows-cross`; touched live
steps run before landing (join, move, shoot, map-reload at least).

## Decisions

- 2026-11-01 (user): "do the same for client.so": the server's T6 part 3
  shape and rulings apply to the client (injection by constructor, owned by
  the DLL object; static-library and engine by-symbol reach is FLOOR).
- 2026-11-01 (user): "approved, go": the plan (phases C0-C6, the multi-file
  area owner, per-object context from creators, the census widened where a
  reach was hidden) is approved; land and continue.

## Progress notes

- The client DLL object owns the shim root, the convar area, the input, the
  physics, the decal emitter, the managed frame's system row and the client
  systems table; a system that is only a forward to managed is the table's own
  member. An area its class implements names `CClass::Slot` bodies.
- An area whose bodies span files (particles, fx, effects render, view,
  standalone, UI, temp-entity backends) gets one owner class that holds the
  other files' objects by constructor and forwards to them.
- Per-object reach (temp entities, renderables' SetupBones, particle and world
  text DrawModel) is handed by the creating module, as the server's entities
  hold their context.
- A function absent from client.so is not dead until tools/probes and
  tools/client-probe-link agree: the probe build calls the FX_* forwards.
