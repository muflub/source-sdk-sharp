//========= SourceSharp — the per-entity side table, keyed by nothing ==========//
//
// WHAT THIS IS. Eight kinds of data hang off an entity without being members of
// it -- touch links, ground links, the step-simulation record, the model scale,
// the position and vphysics watcher lists, the push list and the AI vphysics
// update. `shareddefs.h`'s enum names them and `CDataObjectAccessSystem` owns
// one hash table per kind.
//
// WHY IT MOVED. The system, its instance and both of its game-system accessors
// were defined in `game/shared/physics_main_shared.cpp`, which LEAVES both vpcs
// in the client flip (plan_entity.md §6's LEAVE table, and its own carve-out
// clause: "physics_main_shared.cpp also defines ClientSystem_DataObjectAccess(),
// a live kClientSystems[] row, which has to be re-homed rather than deleted with
// the file"). Lane `c8-clientsystems` asked for exactly that in
// `.claude/entity-port/requests/c8-clientsystems.md`, and asked for the NAME to
// be kept so that `shell_clientsystems.cpp` and `shell_serversystems.cpp` do not
// change: a declaration does not care which TU satisfies it.
//
// (plan_physics_bones_native_zero 2.6: physics_shared.cpp left the repository.
// Nothing in either DLL creates a data object -- EntityDataObject_Create had no
// referrer on either link line -- so the server's system went and the client
// answers Has from shell_physics_shared_client.cpp, for fork physics.cpp's wake
// and sleep. What follows is the history.)
//
// It then lived in `game/shared/physics_shared.cpp` -- §6's PhysicsShell row,
// which STAYS on both sides and already has a row in both vpcs. Six of the
// eight kinds are physics-simulation state, which is what makes that the home
// rather than a new file: a new .cpp would need a vpc row in each project and
// both vpcs belong to the integrator.
//
// THE KEY IS A `const void *` AND STOCK SAYS IT MAY BE.
// `entitydatainstantiator.h` was keyed on `const CBaseEntity *` and carries its
// own comment on the line that declares it:
//
//     // This is the hash key type, but it could just as easily be and int or void *
//
// Nothing in the instantiator does anything with the key but compare it for
// equality (`CompareFunc`) and hash its address (`KeyFunc`). So the side table
// no longer names an entity type, which is what §6 requires of everything that
// stays native, and the stored values and hash buckets are bit-for-bit what
// they were: the same pointers, the same `>> 10`.
//
// `Has` REPLACES A BITFIELD, and is the one real change. `CBaseEntity` carried
// `m_fDataObjectTypes`, a bit per kind, as a fast path in front of the hash --
// `HasDataObjectType(type)` was a bit test. The bit was only ever set by
// `CreateDataObject` and cleared by `DestroyDataObject`, the same two calls that
// insert into and remove from the hash, so "the bit is set" and "the hash holds
// this key" cannot disagree. `EntityDataObject_Has` asks the hash. That is a
// lookup where there was a bit test, in `ObjectWake`/`ObjectSleep` -- recorded
// as a cost in this lane's handover, per LANE_BRIEF's "move first, measure
// after".
//
//=============================================================================//
#ifndef SOURCESHARP_DATA_OBJECT_ACCESS_H
#define SOURCESHARP_DATA_OBJECT_ACCESS_H

//-----------------------------------------------------------------------------
// The side table, keyed by an opaque pointer. `type` is one of shareddefs.h's
// GROUNDLINK..VPHYSICSWATCHER; out-of-range asserts and answers empty, as
// stock's CDataObjectAccessSystem::IsValidType did.
//
// Has     whether a data object of that kind hangs off this key. Stock's bit
//         test; false in this build, where nothing creates one (Get, Create
//         and Destroy left with physics_shared.cpp at 2.6, unreferenced).
//-----------------------------------------------------------------------------
bool  EntityDataObject_Has( int type, const void *pKey );

#endif // SOURCESHARP_DATA_OBJECT_ACCESS_H
