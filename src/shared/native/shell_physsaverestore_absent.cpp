//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the physics save/restore manager, without the saving =//
// SS_REASON a  a vtable the engine calls through
//
// plan_shared.md Phase 2. game/shared/physics_saverestore.cpp has left both
// `_csharp.vpc` file lists along with game/shared/saverestore.cpp, because this
// build refuses to save (§3c: the shell answers ShellGameDll_Unreachable for
// the whole 11-method save cluster, and the one instrumented run that forwarded
// to stock's machinery captured 0 of 473 entities).
//
// THE COMPLICATION THE PLAN NAMED, AND IT IS REAL. Most of that file is saving.
// `g_pPhysSaveRestoreManager` is not. It is LIVE BOOKKEEPING: seven surviving
// translation units call its Associate/Forget paths every time a physics object
// is created or destroyed --
//
//     server   baseentity.o  physics.o  physics_prop_ragdoll.o  physics_shared.o
//     client   cdll_client_int.o  physics_shared.o  ragdoll.o
//
// -- and none of those calls is a save. They maintain the model-pointer table
// that a save WOULD have read: which CPhysCollide or model index a given
// IPhysicsObject was built from, so that a restore could rebuild it. With no
// save there is nothing to read it, but the calls are still made, on paths that
// run constantly.
//
// So this is a no-op manager rather than a deletion. Deleting the calls instead
// would mean editing seven files -- four of them stock, three of them deep in
// the physics cluster §3d keeps -- to remove bookkeeping that costs nothing and
// whose absence would have to be re-added by whoever writes managed
// save/restore. plan-audit D16 has that lane; this is not it.
//
// WHY A NO-OP IS SAFE AND NOT MERELY CHEAP. Everything the real manager stores
// is read by exactly one thing: `CPhysSaveRestoreBlockHandler::Save`, walking
// the table to write it out. Nothing asks it a question during play. Two
// specific behaviours are preserved all the same, because they are what the
// callers actually depend on:
//
//   * ForgetModel on an object never associated is harmless. It is stock's
//     behaviour (a dict Find that misses) and it happens on every physics
//     object built by code that does not associate one.
//   * GetPhysObjSaveRestoreOps returns a REAL ops object rather than NULL.
//     DEFINE_PHYSPTR bakes its return into a typedescription_t at static-init
//     -- physics_saverestore.h:29 -- in whatever TU carries the datamap, and a
//     NULL there is a null vtable pointer sitting in a table that outlives
//     every decision about whether saving happens. The ops answer "empty" and
//     do nothing, which is what a field with no save behind it means.
//
// THIS FILE DIES WITH ITS CALLERS. Every one of the seven is in §3d's physics
// cluster or on plan_baseentity2.md's arm horizon; when the last of them goes,
// so does the last Associate call, and so does this.
//
//=============================================================================//

// THE SERVER'S HALF IS ONE EMPTY FUNCTION (P5 lane SP). Of the seven callers
// above, the server compiles one: physics.cpp's level shutdown, asking
// ForgetAllModels, which had nothing to forget. It asks
// ss::PhysSaveRestore_ForgetAllModels (ss_physsaverestore.h) instead, so on
// the server this file is that and nothing else, and needs no Valve header.
// The client keeps the manager and the field ops below for its own callers.
#ifndef CLIENT_DLL
#include "ss_physsaverestore.h"

void ss::PhysSaveRestore_ForgetAllModels()
{
}

#else // CLIENT_DLL

#include "cbase.h"
#include "physics_saverestore.h"
#include "isaverestore.h"


//-----------------------------------------------------------------------------
// THE THREE SYMBOLS THIS FILE BANKS in
// docs/statics-audit/shell-globals-baseline.tsv, with the reasons that list
// asks for.
//
//   ShellPhysSaveRestoreManager()::s_Absent      the manager instance, and
//   GetPhysObjSaveRestoreOps(...)::s_Absent      the ops instance. Both are
//                                   function-local statics, which is the shape
//                                   the rule points at -- neither is state, in
//                                   the sense the rule means: they are
//                                   stateless objects that exist only to have
//                                   an address and a vtable.
//
//   g_pPhysSaveRestoreManager       EXTERNED BY NAME. physics_saverestore.h:48
//                                   declares it and seven translation units
//                                   spell it, and this file does not get to
//                                   change an upstream header. A field would
//                                   not link.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The manager. Five no-ops and nothing else.
//
// It keeps no table on purpose. A table nothing reads is a table that can
// disagree with reality without anybody finding out, and this one would grow
// one entry per physics object per level -- so the version that stores nothing
// is both the honest one and the one that cannot leak.
//-----------------------------------------------------------------------------
class CShellPhysSaveRestoreAbsent : public IPhysSaveRestoreManager
{
public:
	void NoteBBox( const Vector &, const Vector &, CPhysCollide * ) override
	{
	}

	void AssociateModel( IPhysicsObject *, int ) override
	{
	}

	void AssociateModel( IPhysicsObject *, const CPhysCollide * ) override
	{
	}

	void ForgetModel( IPhysicsObject * ) override
	{
	}

	void ForgetAllModels() override
	{
	}
};

static IPhysSaveRestoreManager *ShellPhysSaveRestoreManager()
{
	static CShellPhysSaveRestoreAbsent s_Absent;
	return &s_Absent;
}

// The name seven translation units spell. A pointer rather than an object
// because that is what physics_saverestore.h:48 declares and this file does not
// get to change the header.
IPhysSaveRestoreManager *g_pPhysSaveRestoreManager = ShellPhysSaveRestoreManager();

//-----------------------------------------------------------------------------
// The field ops DEFINE_PHYSPTR names. See the header comment for why this is
// an object and not NULL.
//
// IsEmpty answers TRUE: the field holds a pointer into a physics environment
// that no save is going to write and no restore is going to rebuild, so
// "nothing here" is the truthful answer rather than a convenient one. Parse
// answers false for the same reason -- there is no text form of a physics
// object to accept.
//-----------------------------------------------------------------------------
class CShellPhysObjOpsAbsent : public ISaveRestoreOps
{
public:
	void Save( const SaveRestoreFieldInfo_t &, ISave * ) override
	{
	}

	void Restore( const SaveRestoreFieldInfo_t &, IRestore * ) override
	{
	}

	bool IsEmpty( const SaveRestoreFieldInfo_t & ) override
	{
		return true;
	}

	void MakeEmpty( const SaveRestoreFieldInfo_t & ) override
	{
	}

	bool Parse( const SaveRestoreFieldInfo_t &, char const * ) override
	{
		return false;
	}
};

ISaveRestoreOps *GetPhysObjSaveRestoreOps( PhysInterfaceId_t )
{
	// One object for every PhysInterfaceId_t, which stock does not do -- it
	// keeps one per id. The id selects which KIND of physics pointer the field
	// holds, and that only matters to code that writes or rebuilds one.
	static CShellPhysObjOpsAbsent s_Absent;
	return &s_Absent;
}

#endif // CLIENT_DLL
