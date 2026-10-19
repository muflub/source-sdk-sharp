# Save and restore, and why it is still native

**2026-10-19: it is not native any more.** The field codec this page argues
for is managed on both sides: `SaveBlockCodec` and `SaveBlockSession`
(`src/shared/interop`), held byte for byte to stock's `CSave::WriteFields` /
`CRestore::ReadFields` by `SaveCodecOracleTests` over `savegame-stock.tsv`.
The server's save cluster forwards to them since plan_server_native_v2 T4c and
the client's since the client save codec lane; `shell_savegame.cpp` is deleted
and `shell_savegame.h` (now `src/server/native`) holds only §5's load-game
player gate. The sections below are kept as the record of why each field type
and each refusal is what it is; the managed codec's facts cite them.

The reasoning that used to live in `src/sourcesharp/game/shared/shell_savegame.h`
and `save_field_codec.h`. The two headers are one now — `shell_savegame.h` — and
every run of prose longer than a few lines is here instead, with a pointer left
at the place it came from.

**Why it moved.** The pair was 1,478 lines and 620 of them were comment:
measurements taken on live saves, refusals argued at length, and the history of
two arguments this code got wrong before it got them right. All of that is worth
keeping and none of it is worth scrolling past to reach an inline function.
`plan_shim.md` §5 3e.

**What did NOT move.** A comment shorter than eight lines explains the line
beside it, and the person it was written for is looking at that line. Those
stayed in the header.

**The cluster STAYS NATIVE**, `plan_shim.md` reason (f). Save and load are wired
end to end: the live gate's `save-load` step is in `RUN_ORDER` and drives real
saves, `SavedEntityCodec` / `SaveTiers` / `LogicAutosave` exist on the managed
side, and the engine itself parses the block framing this header lays out. What
is native is the FRAMING and the encoding of the engine's own `typedescription_t`
arrays — neither of which is this mod's to decide.

---

## 1. What this header is, and why save/restore is still native

*From `shell_savegame.h`.*

====== SourceSharp — the save buffer's RECORD layout =======================//
 save_field_codec.h answers "how wide is one field, and how do its bytes
 move". This answers the question above it: how the fields of ONE
 `SaveWriteFields` call are laid out in the save buffer, and how the matching
 `SaveReadFields` finds them again on load.
 THE LAYOUT IS STOCK'S, AND THAT IS A MEASUREMENT RATHER THAN A PREFERENCE.
 docs/managed-saverestore.md §2a said the engine does not parse the game's
 bytes, and the first version of this file believed it: twelve bytes of
 self-describing frame per block, a name HASH of our own, no symbol table. It
 wrote a `.sav`. It then SEGFAULTED THE ENGINE on load, inside engine.so, four
 frames below anything of ours -- and the register that took it down held
 0x3142, which is the 'B' and the '1' of that frame's own 'SSB1' magic.
 That is the engine reading our framing as stock's: it takes the SHORT AT +2
 OF THE GAME'S OWN BLOCK -- where stock keeps a block's name token -- as an
 index into the symbol table it rebuilds from the file's token blob, and looks
 up the block name there. Ours held 'B1'.
 So the engine parses the game's block headers. §2a is true of the field DATA
 and false of the framing, and docs/managed-saverestore.md §6 item 2 -- "does
 a SaveReadFields have to resolve CSave::WriteHeader's 2-byte hash against the
 symbol table?" -- is answered YES, by a crash, on the strongest possible
 evidence.
 THEREFORE, the format below is `CSave::WriteFields`', byte for byte
 (game/shared/saverestore.cpp:831, :936, :944 -- still in the tree, out of the
 build):
     block:   short size = 4          the count record's payload width
              short token             FindCreateSymbol( blockName )
              int   count             how many field records follow
     record:  short size              this field's payload width
              short token             FindCreateSymbol( fieldName )
              <size bytes>
 WHAT THAT BUYS, AND IT REVERSES ONE OF THIS LANE'S OWN ARGUMENTS.
 save_field_codec.h says an unrecognised field type must refuse the whole
 block, because "a codec that skipped it would write a block one field short
 and every later field would be read from the wrong offset". That is true of a
 FLAT layout and FALSE of this one: every record carries its own name and its
 own width, so a field the writer omitted is simply a name the reader never
 sees, and a field the reader does not know is skipped by the size in front of
 it. Stock relies on precisely this -- `ShouldSaveField` omits empty fields and
 adjusts `count`.
 So an unencodable field is OMITTED and LOUD rather than refused-and-silent:
 the block is still written, still framed, still readable, the field is absent
 from it, and ShellSave_WriteFields warns with the field's name and type
 number. Refusing the whole block would have been the safe answer under the
 old layout and is the DAMAGING one under this one -- it would take the block
 out of a stream the engine is counting.
 ENGINE-FREE, so shell_savegame_test.cpp can drive every path without a
 running Source: the symbol table is an interface, the buffer is a bounded
 cursor, and the field list is an accessor over four integers and a name. The
 caller reads them off a `typedescription_t`; nothing here knows what one is,
 which is also what makes "a field type nobody has thought of" testable.
=============================================================================//

## 2. The buffer this shell asks the engine for in SaveInit

*From `shell_savegame.h`.*

 The buffer this shell asks the engine for in SaveInit, and why it is this
 number.
 THE ENGINE PASSES 0 AND HAS NO OPINION -- measured, docs/managed-saverestore.md
 §4a, which also records that the 3 MiB in D16's trace was the instrument's own
 substitution and not the argument. So the size is a decision here.
 What actually goes in it, measured live on dm_lockdown at `-maxplayers 1`:
 the server writes 1,380 bytes of the ENGINE's own structs (one 'Save Header'
 of seven fields, fifteen 'LIGHTSTYLE's of two) and 432 more for the
 'GameHeader' stage.
 AND THEN STAGE 2 PUT THE WORLD IN IT. The managed entity block for the same
 map is 152,795 bytes -- 482 records at about 320 bytes each -- which is a
 hundred times everything else in the buffer put together and makes the old
 512 KiB a THREE-times margin rather than the two-hundred-times one the
 sentence here used to claim.
 4 MiB, therefore, and the arithmetic is ours rather than stock's: 320 bytes a
 record against MAX_EDICTS (2,048) is 650 KiB for a world of the largest size
 this engine can hold, and 4 MiB is six times that. Stock's 3 MiB was for 473
 entities of DATAMAP payload this build does not produce; landing near it is a
 coincidence of scale and not the number being copied.
 WHAT HAPPENS IF IT IS EVER SHORT is a warning naming the two sizes and a
 `.sav` with no managed entity block in it -- ShellSave_WriteBlob refuses
 rather than truncating, because half a block is a world restored in part.

## 3. The two time sentinels, and why a stored time is relative

*From `shell_savegame.h`.*

 game/shared/saverestore.cpp:63 and public/datamap.h:41. A time of exactly
 zero is written as ZERO_TIME rather than as a zero delta, so that it restores
 as zero rather than as "the moment the save was taken"; INVALID_TIME and
 FLT_MAX are passed through unrebased.
 ALIASES, NOT COPIES. These used to be their own definitions of the same two
 values, beside their own copy of the same two transforms -- see below. Two
 definitions of one sentinel is the drift this file cannot survive: a block
 written with one ZERO_TIME and read with the other restores every "never" as
 a real time, and nothing anywhere goes red.

## 4. A bounded cursor over the save buffer

*From `shell_savegame.h`.*

 A bounded cursor over the save buffer.
 EVERY READ AND EVERY WRITE IS BOUNDS-CHECKED, and an overrun POISONS the
 cursor rather than clamping: once `m_bOverflowed` is set nothing more moves,
 so a truncated file cannot produce a half-decoded struct by having its later
 reads succeed. docs/managed-saverestore.md §3, second consequence.
 This is deliberately NOT CSaveRestoreSegment::Read/Write. Those set `size` to
 `bufferSize` on an overrun, which destroys the position a diagnostic would
 want to report and cannot be undone.

## 5. Who creates the player on a savegame load

*From `shell_savegame.h`.*

 WHO CREATES THE PLAYER ON A SAVEGAME LOAD -- the gate, and it is the engine's
 own.
 On a fresh connect the engine calls ClientPutInServer( edict, name ) and then
 ClientActive( edict, false ). On a load-game it does NOT call
 ClientPutInServer at all: ClientActive arrives alone, with bLoadGame true.
 WHY STOCK CAN AFFORD THAT AND THIS PORT CANNOT. Stock's player arrives out of
 the save's own entity table: CEntitySaveRestoreBlockHandler::Restore
 (game/shared/saverestore.cpp:2586-2603) sees an entry whose edictindex is in
 1..maxClients, takes ed = INDEXENT( edictindex ), and calls
 CBasePlayer::CreatePlayer( classname, ed ). By the time ClientActive runs the
 player is already standing, which is exactly why hl2_client.cpp's ClientActive
 reads
     pPlayer->InitialSpawn();
     if ( !bLoadGame )
         pPlayer->Spawn();
 -- InitialSpawn always, Spawn only for a client who did NOT come out of a
 save. This port writes NO entity table (docs/managed-saverestore.md section
 2d) and its records are the MANAGED HALF of an entity, so nothing in Restore
 can bring the player into existence. Answering ClientPutInServer's job at
 ClientActive is what is left, and this predicate is that decision.
 THE COST OF NOT ANSWERING IT, measured: the world reloads with no player, and
 the NEXT save fails with the engine's own "Can't savegame without a player!"
 -- which the engine prints when IServerGameClients::GetPlayerState( edict )
 answers NULL.
 A PREDICATE RATHER THAN AN `if`, so that the gate is a thing a test can hold:
 creating a player needs the entity factory, the edict array and the managed
 runtime, so the CREATION cannot be linked into a gtest -- but the decision
 can, and the decision is the half that has two ways to be wrong. Both wrong
 answers are silent: false when a load has no player leaves the world
 playerless, and true when one is already bound builds a second player onto a
 live client's edict.

## 6. One SaveWriteFields call

*From `shell_savegame.h`.*

 One SaveWriteFields call.
 Returns the number of bytes written, or -1 if the block could not be written
 at all (a null argument, or a buffer with no room for even the header).
 `pnOmitted` counts the fields this codec cannot encode, and
 `ppszOmittedName` / `pnOmittedType` name the first of them, so the caller can
 say WHICH field and WHICH type rather than only that something was missing.
 Those fields are absent from the record stream and not counted in `count` --
 see the file header for why omitting is right here and refusing is not.

## 7. One SaveReadFields call

*From `shell_savegame.h`.*

 One SaveReadFields call.
 Returns the number of bytes consumed, or -1 with *ppszRefusal naming why.
 A REFUSAL LEAVES pBase AS THE ENGINE GAVE IT AND THE CURSOR WHERE IT WAS,
 and that costs a second pass rather than being free. The records are WALKED
 first -- header, width, skip, next -- and only decoded once every one of them
 has been proved to be entirely present. A single pass cannot promise this:
 a `.sav` truncated in the middle of its last record would have restored every
 field before it and then refused, which is `docs/managed-saverestore.md` §3's
 "half a restored world is worse than none" with the halving done one field at
 a time. Two passes over a few dozen records, once per load, is not a cost
 worth trading that for.
 A field named in the file but absent from this build's datamap is SKIPPED by
 the width in front of it; a field in the datamap but absent from the file is
 left ZEROED. Both are stock's, and both are what make the format survive a
 datamap that changed between the save and the load.

## 8. A blob block, and what makes one self-describing

*From `shell_savegame.h`.*

 A BLOB BLOCK -- one opaque byte string, in stock's block frame.
 This is the SECOND kind of block the game writes, and it is not a field
 block. The managed entity state is `SourceSharp.Abi.SavedEntityCodec`'s
 bytes: versioned, self-describing, and refused as a whole on any damage. It
 has no `typedescription_t` and never will, which is exactly the property
 docs/managed-saverestore.md section 2d turns into the scope reduction --
 entities that do not go through a datamap need no entity table.
 SO WHY FRAME IT LIKE STOCK AT ALL, when the payload is opaque. Because the
 engine parses the game's block HEADERS -- shell_savegame.h's own file header
 records the segfault that established it. A block written in a frame of our
 own would be a `short size` the engine reads at +2 of a block and resolves
 through its rebuilt symbol table, exactly as before.
 CHUNKED, AND THE CHUNK SIZE IS THE FORMAT'S RATHER THAN A TUNING. A record's
 width is a `short` (SaveRestoreRecordHeader_t), so one record cannot carry
 more than 32,767 bytes and a 473-entity block is larger than that. The block
 therefore carries N chunk records and its `count` says how many; the reader
 concatenates them in order. 30,000 leaves the short's headroom uncommitted
 rather than sitting on its edge.
 AND THE CONCATENATION MUST BE EXACT. SavedEntityCodec refuses TRAILING BYTES
 by design -- it is the one disagreement a version stamp cannot catch -- so a
 reader that padded the last chunk would turn every save into a refusal.

## 9. Reading a blob back, size-then-fill

*From `shell_savegame.h`.*

 Read one back, SIZE-THEN-FILL.
 Returns the PAYLOAD size in bytes. The cursor moves past the block ONLY when
 the payload was actually copied out -- so a caller sizes with (0, 0),
 allocates, and calls again, and the block is consumed exactly once.
 THREE ANSWERS, AND THEY ARE THREE DIFFERENT THINGS:
    > 0 / 0   this block is here and is that many bytes (0 meaning a block
              that was written empty -- which this build does not write, but
              which a file can carry)
   kShellSaveNoBlock   there is no such block at the cursor: either the save
              data has run out or what is here is another block's header.
              NOT an error -- a `.sav` from a build that wrote no managed
              entities is exactly this, and it must load.
        -1    the block IS here and is damaged. Named, and nothing is copied.
 The cursor is left where the caller's was in all three of the non-consuming
 cases, because the engine reads the game's buffer sequentially and a reader
 that swallowed bytes it did not understand would move every later block.

## 10. The shell's side of the engine's save cluster

*From `shell_savegame.h`.*

 THE SHELL'S SIDE, which is the half that needs a running engine.
 Declared here and defined in shell_savegame.cpp rather than inline, and the
 reason is a compile error rather than tidiness. `CSaveRestoreData` lives in
 public/saverestoretypes.h, whose entity table carries an `EHANDLE` when
 EHANDLE_H has been seen and an `EHandlePlaceholder_t` when it has not -- and
 `CGameSaveRestoreInfo::BuildEntityHash` calls `.Get()` on it under
 `#ifdef GAME_DLL`. So that header only compiles behind cbase.h, and
 shell_gamedll.cpp deliberately does not have cbase.h: it holds
 `CSaveRestoreData` as the incomplete type eiface.h forward-declares, exactly
 as it did while the whole cluster refused, and every dereference is on the
 other side of these six functions.
 FORWARD DECLARATIONS ONLY BELOW THIS LINE, so shell_savegame_test.cpp can
 include this header and drive the record codec with no engine at all.

## 11. The managed half, driven from the shell's Save and Restore

*From `shell_savegame.h`.*

 The managed half, driven from the shell's Save and Restore.
 SERVER ONLY, and that is docs/managed-saverestore.md section 2f rather than an
 oversight. Every record ClientStandaloneRegistry.Collect() produces is a
 DECODE of server-networked state -- no client class overrides SaveState, so
 the records carry [NetState] fields and nothing else, and those types live in
 SourceSharp.Shared and are written by the server. The argument for saving them
 anyway is a HOT-RELOAD argument (an entity that went quiet transmits no
 deltas), and a load rebuilds the client from full baselines rather than from
 deltas. Writing bytes nobody needs, in a format that then has to be versioned
 forever, is the more expensive mistake -- so the client's two slots stay wired
 to nothing until the comparison in section 2f has been made.
 TRUE means the save may continue. A managed side that refused to encode is a
 save that must NOT be written, and says so.

## 12. The engine's own save headers, encoded

*From `save_field_codec.h`.*

========= SourceSharp — the engine's own save headers, encoded =============//
 The engine calls `SaveWriteFields( pSaveData, blockName, pData, pMap,
 pFields, count )` with ITS OWN structs and ITS OWN datamaps, and calls
 `SaveReadFields` with the same ones on load. So a game DLL that saves has to
 be able to encode a `typedescription_t` array, whatever else it does with its
 entities.
 That was `CSave::WriteFields` in game/shared/saverestore.cpp, which left both
 builds in plan_shared.md Phase 2 -- so this is what replaces it, and it is
 very much smaller than that sentence suggests.
 SIX TYPES, AND EVERY ONE OF THEM IS MEASURED RATHER THAN CHOSEN.
 THE FIRST THREE are the SERVER's, from a temporary logging implementation of
 the cluster, one headless run at `-maxplayers 1` on dm_lockdown, one `save`
 -- docs/managed-saverestore.md section 4a. Across all 37 fields the engine
 asked the server to write:
     FIELD_INTEGER    (5)   19x   skillLevel, connectionCount, lightStyleCount,
                                  mapVersion, and each LIGHTSTYLE.index
     FIELD_CHARACTER  (8)   17x   mapName[32], skyName[32], LIGHTSTYLE.style[64]
     FIELD_TIME      (16)    1x   time__USE_VCR_MODE
 THE OTHER THREE ARE THE CLIENT'S, and they are the correction that measurement
 needed. §4a's census was of the server only, and the sentence it produced --
 "it is THREE" -- was read as a property of the save. It is not. The engine's
 client-state save drives the client's OWN cluster and asks it for
 a `DECALLIST` block per decal in the world; on dm_lockdown that is 76 of them,
 and three of its five fields are types the first version of this file refused:
     FIELD_VECTOR          (3)    impactPlaneNormal
     FIELD_SHORT           (7)    entityIndex
     FIELD_POSITION_VECTOR (15)   position
 Measured live, by this codec refusing them out loud and naming them -- which
 is the behaviour the refusal was built for, working as intended on the first
 engine datamap nobody had counted.
 Everything else CSave handled was for ENTITY datamaps, and this port's
 entities do not go through a datamap at all -- they are SourceSharp.Abi's
 SavedEntityCodec.
 FOUR OF THE SIX ARE A BYTE COPY AND TWO ARE NOT, which is a correction to
 what this file used to say. It argued that FIELD_POSITION_VECTOR could be
 copied because THIS build's SaveInit sets `fUseLandmark = false` and a
 landmark of vec3_origin makes stock's subtraction the identity. That is true
 of the offset and it was never true of the SENTINEL: stock passes
 `vec3_invalid` through untransformed, and a codec that only knew how to copy
 would restore "no position" as a real coordinate the moment a landmark
 existed -- and would look right until then. FIELD_TIME is the same shape and
 worse, because its sentinel bites at a zero landmark too: a time of 0 means
 NEVER, and copying it restores a think scheduled for the moment of the save.
 So both transforms are here, with the base time and the landmark passed IN by
 the caller -- see SaveField_Encode below.
 WHY A FOURTH TYPE MUST BE A REFUSAL AND NOT A SKIP, which is the whole
 argument of this file. The measurement above is of ONE engine build and ONE
 save. If a later engine adds a field to its save header, a codec that skipped
 what it did not recognise would write a block one field short -- and the
 reader would then read every subsequent field from the wrong offset and
 restore a map name out of the middle of a light style. Nothing would say so.
 So an unknown type stops the encode, and the caller reports it by name.
 ENGINE-FREE ON PURPOSE, so save_field_codec_test.cpp can drive it directly:
 nothing here includes datamap.h or knows what a typedescription_t is. The
 caller reads `fieldType` and `fieldSize` off the description and passes them
 as plain numbers, which is also what makes the "unknown type" case testable
 without inventing an engine that has one.
=============================================================================//

## 13. How many bytes one field occupies in the block

*From `save_field_codec.h`.*

 How many bytes one field occupies in the block.
 -1 for a type this codec does not know, which the caller must treat as a
 refusal of the WHOLE block rather than as a zero-width field. See the file
 comment for why skipping is the silent failure.
 `nFieldSize` is the datamap's element COUNT and not a byte count: it is 1 for
 a scalar and 32 or 64 for the fixed-width strings the engine's headers carry.
 An integer field with a count above 1 is an array of them, which the engine
 has not asked for and which is therefore refused rather than guessed at --
 this codec encodes what was measured, and says so when asked for more.

## 14. Two of the six field types are not a byte copy

*From `save_field_codec.h`.*

 TWO OF THE SIX ARE NOT THE STRUCT'S BYTES, and that is the correction this
 section is.
 The four above are copied. FIELD_TIME and FIELD_POSITION_VECTOR are not, and
 stock says so in `CSave::WriteTime` and `CSave::WritePositionVector`
 (game/shared/saverestore.cpp): a time goes down as a DELTA from the save's
 base time "so it can be re-based if loaded in a new level", and a position
 goes down RELATIVE TO THE LANDMARK so that a coordinate survives a transition
 into a map whose origin is somewhere else.
 A raw copy of either is not a refusal that anybody sees. It is a value: a
 think time restored as an absolute stamp from a run that has ended, a decal
 pinned to a coordinate the new level's origin has moved out from under. Both
 restore quietly and both are wrong.
 AND EACH HAS A SENTINEL, which is the half that a "just subtract the base"
 implementation still gets wrong:
   * A time of exactly 0.0 means NEVER -- no think scheduled -- so stock does
     not store a delta for it at all; it stores ZERO_TIME, and ReadTime turns
     ZERO_TIME back into 0. Store 0 as a delta and it restores as the base
     time itself, which is a think scheduled for the moment the save was
     taken.
   * INVALID_TIME and FLT_MAX are passed through un-rebased, both directions.
   * A position of exactly `vec3_invalid` (FLT_MAX in all three) means NO
     POSITION, and WritePositionVector passes it through untransformed.
     ReadPositionVector has the SAME guard the other way -- verified, not
     assumed: `if ( pValue[i] != vec3_invalid ) pValue[i] += basePosition;`.
   * One asymmetry, and it belongs to the write side alone: a real time whose
     delta lands within 0.001 of zero is nudged to 0.001, because a stored
     delta of exactly 0.0 would read back as "a time near now" rather than as
     ZERO_TIME's "never" -- stock's own comment. The read side has no
     counterpart because it needs none.
 The values are stock's own, by definition rather than by transcription:
 ZERO_TIME is `(FLT_MAX*-0.5)` at saverestore.cpp:63, INVALID_TIME is
 `(FLT_MAX * -1.0)` at public/datamap.h:41, and vec3_invalid is
 `Vector( FLT_MAX, FLT_MAX, FLT_MAX )` at mathlib/mathlib_base.cpp:47.
 STILL ENGINE-FREE, which is the constraint that shapes the signatures. The
 base time and the landmark are the CALLER'S -- it is the caller that has a
 CSaveRestoreData -- so they arrive as a float and three floats, and a
 position is `float[3]` rather than a Vector because this header does not
 include mathlib. save_field_codec_test.cpp drives all of it with no engine.

## 15. Copy one field's bytes in either direction

*From `save_field_codec.h`.*

 Copy one field's bytes in either direction.
 A straight byte copy, deliberately: the engine hands back the same struct on
 load that it handed over on save, on the same machine and the same build, so
 there is nothing to normalise. What this is NOT is a place to be clever --
 re-encoding a float or trimming a string would make the block's bytes depend
 on something other than the struct's bytes, and the only reader is the same
 codec.
 FALSE when the buffer cannot hold the field or the type is unknown, and
 NOTHING is copied in that case. A partial field is the failure that reads
 back as a plausible wrong value.

