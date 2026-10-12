# Physics stock parity: where it stands, how to resume

Paused 2026-10-03 at the user's request. Everything below is on `server-novalve`
(worktree `../ss-novalve`), merged into main. Nothing is pushed.

Read with: `plan_physics_stock_parity.md` (the steps and their notes) and
`physics_stock_audit.md` (case ids A1..O against stock blob 22288b9).

## State

Steps 1-10 of the plan are done. The last three landed this session:

| What | Commit | Evidence |
|---|---|---|
| B5: the player's stress crush | 3389be1 | object_stress.h, 14 gtests red on a stub; live step `stress` |
| Forward aim: stock's `!pPhysGround` | 6789842 | 3 PlayerShadowTests facts red first |
| Teleport carries the shadow; `setpos_exact` is stock's Teleport | 0b76b768 | trigger step's snap arms, 14/14 rounds |
| B7: VPhysicsShadowUpdate (PlayerShadowRecovery) | 2182466e | 8 of 12 facts red first; gate 29x2 |
| Advance retired (B7's replacement) | the commit after this doc | push x10: 0/10 jammed without it, 2/10 with, 4/10 before B7 |

Gate-side fixes from the same session: `Api.events_all` paging (c4023038),
the player-health trail after every step and knock-loose before impact-damage
(04301a60), `tools/live-gate --self-test` in the runner leg (03033da).

## Open, in order of value

1. **E1b: contact touch with the world.** Stock marks a prop resting on the
   floor as touching worldspawn; ours dispatches entity-entity pairs only.
   Needs a world toucher. Test first: a crate on the floor lists index 0.
2. **B4c: `m_bAwake` networking and the thrown-by-player flag.** Check first
   whether the client reads either.
3. **H follow-up, needs the user's ruling:** the penetration give-up is
   decided natively (penetration_ledger.h). If "native never decides" must
   hold, it becomes a managed answer asked mid-step.
4. **Live checks with no stage yet:** G (freeze crush) needs a pile that makes
   vphysics freeze an object; the clip-brush check needs the brush footprint
   from the dev API; start-asleep needs a map-placed prop (sandbox map pass).
5. **Deferred, with reasons in the plan:** B3 (no InteractiveDebris group),
   TRYENTITYSOLVER, C8's damage-force arm, F2 (N/A).

Not a defect: a resting crush cannot be staged. Anything over the player's
350 kg push-mass limit passes through an idle player, in stock too
(bisected: 330 kg rests, 390 kg falls through).

## Watching

- **knock-loose** fails about 3/20 isolated on this tree under either teleport;
  42 measured 0/10 on theirs (p ~ 0.2). Re-measure before acting.
- **contact-touch** occasionally fails: crate B slides off A when A rests
  tilted against map prop 61. Seen at a07c216 and in one interrupted gate.
- **c7's dead player in a full gate** (85 -> -5 between pickup and
  prop-hurts-player at d673dea): not reproduced; the health trail will name
  the step next time.

## How to resume

1. Check the base: `git -C ../ss-novalve log --oneline -3`.
   `make setup ROOT=../source-sdk-2013`, then `make debug`.
2. Each step is test-first: write stock's facts, show them red, port, green;
   one commit with the red quoted.
3. Per-commit chores when physics.cpp or the shim changes (they trip every time):
   - re-measure physics.cpp: `tools/physics-scope/fnmap` + `classify`, add a
     ruling for a new function, update `docs/physics-move-scope.md` (section 1,
     the totals table, the history row) and `physics_move_scope_test.cpp`;
   - shim letters and ceiling: re-sync `docs/shim-reasons.md` from
     `shim_reason_letters_test`'s walk, raise `shim_lines` in
     `docs/shim-ratchet.tsv` with a dated reason;
   - a new ABI slot: a row in `docs/engineapi-areas.tsv`, then
     `cd farm/debug && tools/shim-areas --write && tools/vtable-layout --write
     && tools/entrypoint-decls --write`; bump `EngineApiCensusTests`; run
     `tools/abi-residency`;
   - a new file with the Valve header: a row in `docs/valve-header-new.tsv`;
     a deleted OURS file: remove its rows from `docs/move/ledger.tsv` (dated
     header note, counts) and `docs/move/header-rulings.tsv`;
   - a new live step: a budget row from a busy-regime pass via `budget_row()`.
4. `make check`, then the full `make live-gate` for any runtime change.
   Rates, not single runs: `tools/3f-gatebisect --steps join,<step> --rounds 10`.

## Pitfalls met this session

- Managed `Log.Msg` does not reach a bisect instance's console log. Native
  `cs_sv_*` output is read with `g.count_console` / `g.last_console_line`.
  For a temporary managed probe, append to a scratch file.
- `/events` returns 500 per call; use `Api.events_all`.
- Plain `setpos` leaves the player's shadow behind for a tick; the gate moves
  the player with `setpos_exact`.
- A teleport must also re-aim the player controller, or vphysics snaps a hull
  more than 24 units from its target back to the target.
