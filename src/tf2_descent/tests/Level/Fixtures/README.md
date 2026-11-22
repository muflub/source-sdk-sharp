# Level fixtures (WO1)

Generated data, not Valve content: the files `make descent-level DEPTH=1 SEED=1`
writes for depth 1, seed 1 (map tools at `maptools.lock` 3551fa8cf557, made at
28e09a10d, 2026-11-11), after `make descent-rooms DEPTH=1`:

- `tf2_descent-1-s1.nav3d`, `tf2_descent-1-s1.map2d`: `bin/<config>/descent-rooms/d01/maps/`, as the level ships them.
- `tf2_descent-1-s1.yaml`: the level file the generator wrote (its `# rooms` comment is the layout's room count).
- `descent_d01.library.json`: the library's manifest (each room's marker kinds).

Rebuild them with those two commands and copy the four files here when the
level format or the depth 1 library changes.

**These are depth 1 as generated before `50cefc6c3` (2026-11-14), kept on
purpose.** That commit made the wide drift rooms' floor rails non-solid, so in
a level built today a giant's hull reaches all 18 rooms. In these files the
rails are solid and the two largest hull classes are cut off from rooms 6, 16
and 17, which three sets of facts need: the room table (`HordeNavDataTests`),
the reach rule that keeps a giant off a floor it cannot get to
(`NavHullReachTests`) and the nearest-place path answer (`HordePathsTests`).
Do not refresh them for a room change alone: a level with no cut floor makes
those facts' stimulus vanish. What today's rooms admit is the map generator's
own fact (`DescentGiantNavTests`).
