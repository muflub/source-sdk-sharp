# Level fixtures (WO1)

Generated data, not Valve content: the files `make descent-level DEPTH=1 SEED=1`
writes for depth 1, seed 1 (map tools at `maptools.lock` 3551fa8cf557, made at
28e09a10d, 2026-11-11), after `make descent-rooms DEPTH=1`:

- `tf2_descent-1-s1.nav3d`, `tf2_descent-1-s1.map2d`: `bin/<config>/descent-rooms/d01/maps/`, as the level ships them.
- `tf2_descent-1-s1.yaml`: the level file the generator wrote (its `# rooms` comment is the layout's room count).
- `descent_d01.library.json`: the library's manifest (each room's marker kinds).

Rebuild them with those two commands and copy the four files here when the
level format or the depth 1 library changes.

**These are depth 1 as generated before `50cefc6c3` (2026-11-14).** That commit
made the wide drift rooms' floor rails non-solid. In these files the rails are
solid, 8 units high, their tops inside a voxel. Until 2026-11-15 the nav
answered a hull standing on such a step as blocked, which cut the two largest
hull classes off from rooms 6, 16 and 17; since the fit is asked from the voxel
over the feet, the giants stand on the rails and every class reaches all 18
rooms, as in a level built today. The facts that need a floor a giant really
cannot cross (`NavHullReachTests`, `HordePathsTests`' nearest-place answer) use
a level of their own, `CutFloorLevel` in `Horde/NavHullReachTests.cs`: a
corridor narrower than a giant, every face on a voxel boundary. These files
need no refresh for that. What today's rooms admit is the map generator's own
fact (`DescentGiantNavTests`).
