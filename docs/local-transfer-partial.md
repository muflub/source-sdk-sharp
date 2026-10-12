# The local network backdoor's partial transfer crashes on 64-bit

Found 2026-10-04 by the no-steam experiment lane; fixed on branch
`fix-backdoor-sendproxy` (base c6c53802) by `ServerActivate` setting
`dt_UsePartialChangeEnts 0` (`ServerGameDll.ApplyLocalTransferPolicy`).

## Symptom

A listen server launched with `-maxplayers 1` and `cl_LocalNetworkBackdoor` at
its default (1) SIGSEGVs in server.so about 11 s after `+map dm_lockdown`, before
the HUD's first paint:

```
#0 Vector::IsValid (this=0x73f)
#1 SendProxy_VectorToVector (pProp=<tier 0 prop 0, "m_vecOrigin", m_Offset 1856>,
   pStruct=0xffffffffffffffff, pData=0x73f, objectID=2 | 18 | 117)
#2..#18 engine.so
```

`pStruct` is -1 and `pData` is -1 + 0x740. The entity varies by run (2, 18,
117); the prop is the first one in the tier table, so any entity whose edict
recorded change offsets hits it. The live gate never saw it because every
single-player launch it makes passes `+cl_LocalNetworkBackdoor 0`.

## Root cause: an engine sentinel that is 32 bits wide

From engine.so (Source SDK Base 2013 Multiplayer, linux64), read with
`objdump -d` and `gdb -batch` over the core:

- `LocalTransfer_TransferEntity` (0x4f9360) takes its partial arm when the
  edict's change-info serial matches, the entity is neither new nor just in the
  PVS, and `dt_UsePartialChangeEnts` is non-zero (0x4f9860..0x4f9884).
- That arm calls `CDatatableStack::Init(true)` (0x5049c0), which runs
  `memset(m_pProxies, 0xFF, nProxyPaths * 8)`: every slot, the root included,
  becomes `0xFFFFFFFFFFFFFFFF`.
- The per-prop lookup rebuilds a slot only when it equals the sentinel, and
  the sentinel is loaded with `mov $0xffffffff,%r14d` (0x4f9ad8), which is
  `0x00000000FFFFFFFF`. The compare at 0x4f9bf8 (send side) and 0x4f9b5f (recv
  side) never matches, so the -1 is used as the struct base.

`dt_UsePartialChangeEnts` ("(SP only) - enable FL_EDICT_PARTIAL_CHANGE
optimization.", flags 0) has one reference in engine.so, in this function.
With it at 0 the engine copies a changed entity through the full-transfer arm,
which calls `Init(false)` and sets the root base. Nothing in our SendTables
can route around the bug: the root slot is broken too.

Stock server code records change offsets in the same way
(`CBaseEdict::StateChanged(unsigned short)`), so this is not specific to our
shells.

## Evidence

Debug build, dm_lockdown, `-maxplayers 1 +sv_lan 1`, no backdoor override,
one game at a time:

| build | trials | crashed | notes |
|---|---|---|---|
| c6c53802 (before) | 3 | 3 | SIGSEGV at 11 to 13 s, all at the frame above |
| c6c53802 + `+dt_UsePartialChangeEnts 0` on the command line | 2 | 0 | hypothesis check |
| fix | 10 | 0 | 60 s after first paint each; `cl_localnetworkbackdoor` = 1, `dt_UsePartialChangeEnts` = 0, `ss_player` count 1, client `getpos 2` equals the server's ss_player origin |
| fix, with `changelevel dm_lockdown` 15 s after first paint | 3 | 0 | second ServerLevelInit and ClientPutInServer, held 45 s |

P(0 crashes in 10 | the old rate of 3/3) is about 0 at any rate above 0.4.

Regression facts: `ServerGameDllTests.ServerActivateTurnsOffPartialChangeLocalTransfer`
and `WithDeveloperOnPartialChangeLocalTransferIsStillOff`, red before the fix
(2 failed, 100 passed) and red again with the value mutated to "1".
