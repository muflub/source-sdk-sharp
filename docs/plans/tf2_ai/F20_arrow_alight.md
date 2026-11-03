# F20: the Huntsman arrow lit by a flamethrower

Base: `dae055998`. Worktree `../ss-tf2ai-F20`, branch
`tf2ai-F20`. Size S–M, two commits. Fit: Qwen. Depends on nothing.

## Goal

A Pyro's flame on a teammate's drawn Huntsman sets the nocked arrow alight;
a lit arrow sets its victim on fire. Today `grep -rn Alight src/tf2
--include=*.cs` is empty; the bow exists only as a behaviour-free weapon
kind (`tf_weapon_compound_bow`, 2 distinct schema behaviours).

## Stock, read this session

- `tf_weapon_flamethrower.cpp:3130-3143` (inside the flame's touch of a
  player it already burnt-listed): when the touched player is a teammate
  holding `TF_WEAPON_COMPOUND_BOW` (`pWpn->GetWeaponID()`), `pBow->SetArrowAlight( true )`.
- `tf_weapon_compound_bow.cpp:36` networks `m_bArrowAlight`; `:92` and
  `:169` clear it (on deploy/holster); `:125` passes it to the fired arrow
  (`pMainArrow->SetArrowAlight( m_bArrowAlight )`); `:380` clears it after
  the shot.
- `tf_projectile_arrow.cpp:59` networks the arrow's `m_bArrowAlight`;
  `:101` default false; `:486` on impact, a lit arrow applies burning to
  the victim (read `:480-495` for the exact condition and the burn call,
  and cite it).
- First confirm the bow is a weapon the mod fires at all: grep
  `compound_bow` and `CompoundBow` under `src/tf2/shared/Weapons` and
  `src/tf2/server/Projectiles`. If the mod has no arrow projectile, this
  stage is the projectile too (say so and size it: the arrow is a
  projectile like the syringe/flare rows in `TFShellBehaviours.cs`), else
  only the alight bit.

## Build

1. The bow and arrow carry `ArrowAlight` (commit 1): a bool on the bow's
   predicted weapon state (the way other per-weapon flags are kept: grep
   `TFWeaponNetState` or the weapon state carrier under `src/tf2/shared/
   Weapons`) cleared on deploy, holster and after a shot, copied to the
   arrow projectile at launch; the flame's player touch in
   `src/tf2/server/Projectiles/TFFlameEffects.cs` (the flame's touch; `:20-39`
   is the deflect/cone part: find the burn-list touch) sets it on a
   teammate's bow. Valve header + row on any new file ("ports the arrow
   alight hand-off (tf_weapon_flamethrower.cpp, tf_weapon_compound_bow.cpp,
   tf_projectile_arrow.cpp)").
2. A lit arrow burns its victim (commit 2): on the arrow's impact, when lit,
   the burn path the flamethrower uses (`src/tf2/server/Combat/TFBurn.cs`).
   Facts, `src/tf2/tests/Projectiles/TFArrowAlightTests.cs` (new):
   `AFlameOnATeammatesDrawnBowLightsTheArrow`,
   `AnEnemysFlameDoesNotLightTheArrow` (stock lights teammates' bows only,
   `:3140`'s team test: read and confirm),
   `HolsteringClearsTheLitArrow`, `ALitArrowBurnsItsVictim`,
   `AnUnlitArrowDoesNot`. Update the table row for the Huntsman in
   `game/mod_tf2/scripts/items/items_working.tsv` to `works` with evidence
   if F17 has landed (grep the file; if absent, say so).

## Red first

`AFlameOnATeammatesDrawnBowLightsTheArrow` before commit 1: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFArrowAlight'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; boot `lane-boot.sh tf2
ctf_2fort <port> 'cmd jointeam red' 'cmd joinclass sniper'`: `first paint`
1, `threw` 0.
