# `soundemitter-client-stock.tsv`: where it came from

Stock's client sound emitter, `game/shared/SoundEmitterSystem.cpp` compiled
as the client compiles it (`CLIENT_DLL`): `CSoundEmitterSystem`'s emit path
(`EmitSound`, `EmitSoundByHandle`), both `EmitCloseCaption` overloads, the
stops (`StopSound` by channel and sample, by script, `StopSoundByHandle`),
both `EmitAmbientSound` overloads and `UTIL_EmitAmbientSound`,
`GetParametersForSound`, `PrecacheScriptSound`, and the system's `Init`,
`Shutdown`, `LevelInitPreEntity` and `LevelShutdownPostEntity`. Written by
`src/test/oracle/native/soundemitter_client_oracle_test.cpp`, which compiles
the pristine file whole from the clone at `valve-sdk.lock` into a scripted
world (`soundemitter_stub/soundemitter_harness.h` says what is stock and what
is stand-in). `ClientSoundEmitterOracleTests` replays it against the managed
client emitter.

Each call is driven through the entry point the client called it by: the
`C_BaseEntity` statics that `GameSound_Emit` (three overloads),
`GameSound_Stop` (two), `GameSound_EmitCloseCaption`,
`GameSound_GetParameters` and `GameSound_PrecacheScript` fronted, and
`UTIL_EmitAmbientSound`.

## The rows

Tab-separated, one event per row, in the order it happened:

- `S name`: a scenario begins.
- The world, as the scenario set it: `SCRIPT index name channel volume level
  pitch delay_msec wavecount waves paramsfail`, `DUR sample seconds`,
  `SENT name index`, `ENT index model isplayer owner`, `LOCAL slot` (the local
  player's slot, which `AddAllPlayers` adds), `HUD 0|1` (whether the caption
  element exists), `G maxclients curtime`, `V convar value`.
- `C op args...`: a call. A filter is `slots/reliable` (`-` for none).
- `A ask args... = answer...`: a question the subject asked the engine or the
  script table, and the answer it got. A replay must ask the same question in
  the same place and is handed the same answer. `ParamsEx` carries the handle
  the table left after `=>`; `Internal` carries the entry's channel and every
  wave of it (each through `GetWaveName`).
- `O what args...`: something the subject did: `EmitSound` (the soundlevel
  overload), `StopSound`, `Ambient` (the client's `EmitAmbientSound`),
  `Caption` (`CHudCloseCaption::ProcessCaption`: token, duration, fromplayer,
  direct), `AddOverrides`, `ClearOverrides`, `ModShutdown`.
- `M` / `D` / `W text`: `Msg`, `DevMsg`, `Warning`, newlines written `\n`.
- `R what values...`: a call's result: a duration written back, the handle
  after the call, `GetParametersForSound`'s answer.

Floats are their IEEE bits in hex; vectors three of them, comma-separated.

## The stand-in answers

The managed replay gets the same answers from the rows, so they are part of
the scenario:

- A script's parameters are its `SCRIPT` row's; its wave is the first, or for
  a script of three waves the one at the speaker's gender (none, male,
  female). `paramsfail` scripts have an index and no parameters.
  `GetParametersForSoundEx` resolves a handle of -1 by name, as the table
  does.
- `GetActorGender` answers female for a model naming "female", male for one
  naming "male", none otherwise. An entity with no model is asked about "",
  as stock's `STRING()` of a null name spells it.
- `C_BaseEntity::Instance` answers the `ENT` rows; `IsPlayer` and the owner
  chain are theirs. Every entity's `ModifyEmitSoundParams` is stock's non-TF
  body, which changes nothing.
- `RemoveRecipientsIfNotCloseCaptioning` is stock's client body: with
  `closecaption` off the copy is emptied.
- `GetCleanMapName` is stock's non-TF body, the identity.

To regenerate (only when the scenario changes, and say why in the commit):
touch `src/test/oracle/native/soundemitter_client_golden.regen`, run the
oracle test (`farm/debug/src/buildcpptests --oracle
soundemitter_client_oracle_test`), delete the marker.
