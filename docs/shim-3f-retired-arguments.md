# Arguments that outlived the code they were written in — Phase 3f

`plan_shim.md` §5 3f, lane `3f-sharedlibs`.

Every entry here is prose that was load-bearing inside a function body this
phase deleted. A deletion that takes the argument with it is how a port
rediscovers the same thing twice, so the argument moves and the code goes. Each
entry says what was deleted, why it was safe, and — the part worth keeping —
what the body knew.

None of this is a description of code that exists. If you are reading it to
find out what the tree does today, you are in the wrong file.

---

## `CTakeDamageInfo::GetAmmoName` — a classname is askable on the server and not on the client

**Deleted** from `src/sourcesharp/game/shared/takedamageinfo_library.cpp`
(0 importers on either link line, 0 callers in the translation unit; the
declaration stays in `game/shared/takedamageinfo.h` for unported stock callers).

The body named the inflictor's ammo, and to do that it needed the inflictor's
classname. Getting one is asymmetric between the two sides, and the asymmetry is
not obvious from either header:

* **The server can answer from the handle.** `GetClassname()` is
  `STRING(m_iClassname)`, and that is the same string as
  `IServerNetworkable::GetClassName()` (`ServerNetworkProperty.cpp:164`). So the
  route is handle → `SourceSharp_EntityFromHandle` → `IServerEntity` →
  `GetNetworkable()->GetClassName()`, and it works for an entity on either
  hierarchy because the handle is the identity both share.

* **The client cannot.** `C_BaseEntity::GetClassname()`
  (`c_baseentity.cpp:4903`) looks the name up in the **prediction datamap** —
  `GetClassMap().Lookup( GetPredDescMap()->dataClassName )` — and falls back to
  `typeid(*this).name()`. Both are SDK-internal state that no engine interface
  exposes, so there is nothing on the boundary to ask.

* **`IClientNetworkable::GetClientClass()->GetName()` is not the substitute it
  looks like.** It is the *DT class name* (`"C_PhysicsProp"`), never a map
  classname. Substituting it would turn a test like `"prop_physics"` from
  sometimes-true into never-true **while appearing to have been answered**,
  which is worse than refusing.

The deleted body therefore answered `"Unknown"` on the client and said so. If a
managed or native replacement ever needs a client-side classname, that is the
problem to solve first — it is not a spelling difficulty.

---

## `CTakeDamageInfo::DebugGetDamageTypeString` — where the DMG_ names live

**Deleted** with the 31-entry file-static `s_DamageTypeToStrTable` that was its
only reader. It spelled a `DMG_` bitfield out in words. It is a member function
and not a ConCommand, so there was no console route to it either.

The bit names are not this file's to own and did not go with it: they are in
`game/shared/shareddefs.h`, which is where the table was copied from and where a
replacement would read them.

---

## `gib_head_throw.h` — stock's third outcome, and which way it falls

**Deleted** with its subject; `game/server/gib.cpp` is not in this tree at all.
The tombstone in `src/buildcpptests` carries the short form. The long form:

`CGib::SpawnHeadGib` writes two branches and has **three** outcomes. Stock's
shape is a 5% roll and a `UTIL_FindClientInPVS`; if the roll comes up *and*
somebody is in the PVS, the head is thrown at their eyes, and if either fails it
gets a random tumble. The third outcome is the one nobody wrote: somebody is
watching, the roll came up, and the eye position did not resolve — nothing is
assigned and the gib keeps its spawn velocity.

It is unreachable in stock, where a client edict always holds a `CBasePlayer`.
It was the **only** outcome this port ever reached, because the C-cast that asked
for a player read NULL for the standalone one, so neither written branch had run
since the port. If the head gib is ever ported to managed, that third case is
the one to write first, and it falls to the random tumble — which is what the
code plainly meant.

---

## `client_class_names.h` — the dump sorts for diffability, not tidiness

**Deleted** when `cl_dumpentityfactories` left `shell_diagnostics.cpp`
(§4a item 9).

The server can be asked what entity classes it knows; the client's half of an
entity is registered separately and can go missing on its own, and nothing in
the SDK would tell you. The two dumps exist to be **diffed**, which is the whole
reason the client sorted at all: the server's list is a `CUtlDict` and sorts
with `CaselessStringLessThan` (`utldict.h:121`), while the client's classes live
on a linked list in registration order, which is neither sorted nor stable
across builds.

The tiebreak was not decoration. The compare was case-**insensitive** with a
case-**sensitive** tiebreak, because two network classes differing only in case
would otherwise compare equal, and `qsort` may order equal elements differently
between runs — stable within a run and unstable across them is exactly the
property a diff must not have. A NULL name sorted first rather than crashing: it
ran over a list the engine owns, and a diagnostic that takes the game down is
worse than one that prints an odd first line.

---

## `npc_filter_rule.h` — "everything BUT people" is not the negation of "only people"

**Deleted** with its two and only callers,
`CTraceFilterOnlyNPCsAndPlayer::ShouldHitEntity` and
`CTraceFilterNoNPCsOrPlayer::ShouldHitEntity`.

The pair are opposites and the second is **not** the negation of the first: it
refuses a `CLASS_PLAYER_ALLY` that is not an NPC, carrying stock's "CS hostages
are `CLASS_PLAYER_ALLY` but not `IsNPC()`". That is a game rule about what
counts as a person, and it had been a bare integer comparison inside a
`ShouldHitEntity` body where nothing could ask it a question.

A trace filter cannot cross to managed code — it is asked per candidate per
trace from inside the engine's partition walk — so if these filters ever come
back, the rule goes back into a header in `push_blocker_rule.h`'s shape, over
booleans, with no entity type in its interface. Not back into the body.

---

## `ragdoll_retire_rule.h` and `collision_rules_recheck.h`

Both are covered in full by the tombstones in `src/buildcpptests` and by the
comments at their surviving call sites (`ragdoll_shared.cpp`'s
`ShouldRemoveThisRagdoll`, `physics_shared.cpp`'s
`PhysRecheckEntityCollisionRules`). In one line each:

* **Ragdoll retirement.** Stock asked the local player whether a ragdoll was
  behind him; there is no `CBasePlayer` in this build to ask, and "I do not
  know" falls to **KEEP**, because falling to REMOVE retires every ragdoll in
  the PVS on its first pass and looks exactly like the cap doing its job. The
  rule runs in C# as `RagdollRetirementPolicy.Retire`.
* **Collision recheck.** IVP asks a collision filter **once** per pair and
  caches the answer, so a pair refused once stays refused for the object's life.
  A shell holds one `IPhysicsObject` where an SDK ragdoll holds one per limb, so
  the walk is one iteration here and is written out; if a shell ever holds more
  than one, rechecking only `VPhysicsGetObject()` leaves the other limbs holding
  an answer IVP will not revisit and one limb falls through the floor the rest
  of it is lying on.
