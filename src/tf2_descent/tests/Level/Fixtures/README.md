# Level fixtures (WO1)

Generated data, not Valve content: the files `make descent-level DEPTH=1 SEED=1`
writes for depth 1, seed 1 (map tools at `maptools.lock` 3551fa8cf557, made at
28e09a10d, 2026-11-11), after `make descent-rooms DEPTH=1`:

- `tf2_descent-1-s1.nav3d`, `tf2_descent-1-s1.map2d`: `bin/<config>/descent-rooms/d01/maps/`, as the level ships them.
- `tf2_descent-1-s1.yaml`: the level file the generator wrote (its `# rooms` comment is the layout's room count).
- `descent_d01.library.json`: the library's manifest (each room's marker kinds).

Rebuild them with those two commands and copy the four files here when the
level format or the depth 1 library changes.
