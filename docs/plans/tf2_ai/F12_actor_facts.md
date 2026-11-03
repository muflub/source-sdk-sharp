# F12: `ITFActorFacts`, what a client renders of a player (read-only)

Base: `dae055998`. Worktree `../ss-tf2ai-F12`, branch
`tf2ai-F12`. Size S, one or two commits. Fit: GLM or Qwen. Depends on
nothing. **P5 (the AI lift) depends on this.**

## Goal

One read-only service, by player index, answering only what every client is
sent about a player and would draw: class, the conditions a client renders,
the disguise as rendered (class, team, the borrowed name's player index),
how invisible, the air-dash count. Nothing a client is not sent (no health,
no clip). No behaviour changes.

## What exists

- `src/tf2/shared/Player/TFPlayerNetState.cs` (`tf_player_data`, networked
  to every client): `PlayerClass`, `DisguiseClass`, `DisguiseTeam`,
  `DisguiseTargetIndex` (`:123`), `DisguiseHealth`, `CloakMeter`,
  `InvisChangeCompleteTime`, `AirDash`, `PlayerCond` + `PlayerCondEx..Ex4`.
- `src/tf2/shared/Player/TFPlayerData.cs:56` `Class(this state)`, `:62`
  `DisguiseAs(this state)`, `:90` `IsStealthed(this state)`; `ITFPlayerData.
  Networked(int playerIndex)` (read-only, any player) per the carrier notes;
  `src/tf2/server/Player/TFPlayerCarriers.cs` (`Get(int)`, `ClassOf`).
- Stock's disguise target: `tf_player.cpp` `FindDisguiseTarget` is ported at
  `src/tf2/server/Player/TFSpy.cs:353`; the target ID shows the borrowed
  name (`tf_hud_target_id.cpp:248`, read: it prints the disguise target's
  name for an enemy viewer).
- Invisibility as a client draws it: stock's `GetPercentInvisible`
  (`tf_player_shared.cpp`, grep it and cite) from the cloak state and
  `InvisChangeCompleteTime`; this build's equivalent: grep `PercentInvisible`
  under `src/tf2` and name it.

## Build

`src/tf2/server/Player/TFActorFacts.cs` (new; original code, no header):

```
public readonly record struct TFActorFacts(
    TFClass Class,            // the real class (a client sees the model)
    TFTeam Team,              // the REAL team: the AI binding renders the disguise itself from the fields below
    TFClass DisguiseClass,    // Civilian/0 when not disguised
    TFTeam DisguiseTeam,
    int DisguiseTargetIndex,  // 0 when none: the player whose name the disguise borrows
    float PercentInvisible,   // 0..1 as the client draws it
    int AirDash,
    bool Burning, bool Stunned, bool Ubered, bool Disguised, bool Cloaked, bool Taunting);
public interface ITFActorFacts { bool TryRead(int playerIndex, out TFActorFacts facts); }
```

`[SideService(Side.Server, As = [typeof(ITFActorFacts)])] TFActorFacts(ITFPlayerData players, ITeamRoundRoster roster, ...)`:
`TryRead` answers false for a slot with no carrier. The six bools map from
the condition words through the existing `InCond` helper (grep `InCond` in
`src/tf2/shared/Player`) and the `TFCond` names (`Burning`, `Stunned`,
`Ubercharged`, `Disguised`, `Cloaked`, `Taunting`: confirm each name in
`src/tf2/shared/TFSharedDefs.cs`'s `TFCond` enum and use the exact ones).

Facts, `src/tf2/tests/Player/TFActorFactsTests.cs` (new), over
`src/tf2/tests/Spy/SpyRig.cs` (it disguises and cloaks a player) and
`src/tf2/tests/Player/PlayerRig.cs`: `ADisguisedSpyReportsTheDisguiseAndTheBorrowedIndex`,
`ACloakedSpysPercentInvisibleRises`, `AnUndisguisedPlayerReportsNoDisguise`,
`TryReadIsFalseForAnEmptySlot`, and the negative shape fact
`TFActorFactsCarriesNoHealthOrClip` (reflection over the record's fields:
no member named Health, Clip, Reserve, Ammo).

## Red first

`ADisguisedSpyReportsTheDisguiseAndTheBorrowedIndex` with `DisguiseTargetIndex`
left at 0: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFActorFacts'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; boot `lane-boot.sh tf2 ctf_2fort
<port> 'cmd jointeam red'`: `first paint` 1, `threw` 0.
