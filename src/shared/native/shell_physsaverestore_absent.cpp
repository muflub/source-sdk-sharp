//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// A no-op physics save/restore manager: callers still associate and forget models on
// every physics object, but nothing saves. The server needs only ForgetAllModels.

#ifndef CLIENT_DLL
#include "ss_physsaverestore.h"

void ss::PhysSaveRestore_ForgetAllModels()
{
}

#else // CLIENT_DLL

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "physics_saverestore.h"
#include "isaverestore.h"

// Keeps no table: nothing would read it.
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

// physics_saverestore.h declares a pointer.
IPhysSaveRestoreManager *g_pPhysSaveRestoreManager = ShellPhysSaveRestoreManager();

// The field ops DEFINE_PHYSPTR bakes into a datamap at static init, so they must not be
// NULL. Every field reads as empty.
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
	// One object for every id; stock keeps one per id.
	static CShellPhysObjOpsAbsent s_Absent;
	return &s_Absent;
}

#endif // CLIENT_DLL
