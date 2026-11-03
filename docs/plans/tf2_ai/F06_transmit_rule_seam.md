# F06: the per-client transmit rule (core seam; not for a lane)

Base: `dae055998`. Size M. Fit: **stronger model**. Depends on nothing; F07
depends on it.

## Why not a lane

Stock's `CFuncRespawnRoomVisualizer::ShouldTransmit`
(`func_respawnroom.cpp:474-489`, read this session) sends the brush only to
a recipient on another team (`GetTeamNumber() > LAST_SHARED_TEAM &&
!InSameTeam(recipient)`), only while the room is active and outside
`GR_STATE_TEAM_WIN`, and its `UpdateTransmitState` is `FL_EDICT_ALWAYS`
(`:464-468`). The managed side has no per-client transmit rule: `grep -rn
'ShouldTransmit\|ITransmitRule' src/shared/interface src/server/managed`
is empty. Per-client transmit is an engine-facing path (the check-transmit
callback), so it needs a generic forwarder/managed-table row or a seam over
an existing one, which is the ABI tail (`docs/engineapi-areas.tsv`,
`docs/managed-interfaces.tsv`, regenerated both sides) plus `seams.tsv`.
That is serialised, reviewed work with a live check of what a client
receives: a stronger model.

## What it must deliver

- `src/shared/interface/TransmitRule.cs` (new): `public interface
  ITransmitRule { TransmitAnswer ShouldTransmit(EntityHandle entity, int
  recipientSlot); }` with `TransmitAnswer { Default, Always, Never }`, a
  chain asked per entity that opted in (an entity flag or a registration,
  so the per-client cost stays on the entities that need it).
- The native/engine side: the check-transmit hook as a generic row, named
  in the commit as the one ABI change, `tools/engine-forwarders --write`
  and the managed-areas generator run from the farm, append-only.
- Facts: a rule answering `Never` for one slot keeps that slot's entity
  list without the entity (the dev API `/entity` list per client, or the
  net-table fact shape the repo has); no rule registered leaves every
  existing transmit fact green; the `seams.tsv` row with its no-owner and
  owner facts.
- Live: `tools/tf2-gate --steps join,teams` passes unchanged.
