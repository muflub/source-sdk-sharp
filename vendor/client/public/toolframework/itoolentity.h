//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "engine/ishadowmgr.h"

// Forward declarations
class IServerEntity;
class IClientEntity;
class IClientRenderable;
class Vector;
class QAngle;
class CBaseEntity;
class CBaseAnimating;
class CTakeDamageInfo;
class ITempEntsSystem;

enum
{
	HTOOLHANDLE_INVALID = 0
};

// Purpose: Interface from engine to tools for manipulating entities
class IServerTools : public IBaseInterface
{
public:
	virtual void unused_GetIServerEntity() = 0;
	virtual void unused_SnapPlayerToPosition() = 0;
	virtual void unused_GetPlayerPosition() = 0;
	virtual void unused_SetPlayerFOV() = 0;
	virtual void unused_GetPlayerFOV() = 0;
	virtual void unused_IsInNoClipMode() = 0;

	// entity searching
	virtual void unused_FirstEntity() = 0;
	virtual void unused_NextEntity() = 0;
	virtual void unused_FindEntityByHammerID() = 0;

	// entity query
	virtual void unused_GetKeyValue() = 0;
	virtual bool SetKeyValue(CBaseEntity* pEntity, const char* szField, const char* szValue) = 0;
	virtual bool SetKeyValue(CBaseEntity* pEntity, const char* szField, float flValue) = 0;
	virtual bool SetKeyValue(CBaseEntity* pEntity, const char* szField, const Vector& vecValue) = 0;

	// entity spawning
	virtual CBaseEntity* CreateEntityByName(const char* szClassName) = 0;
	virtual void unused_DispatchSpawn() = 0;

	// This reloads a portion or all of a particle definition file.
	// It's up to the server to decide if it cares about this file
	// Use a UtlBuffer to crack the data
	virtual void unused_ReloadParticleDefintions() = 0;

	virtual void AddOriginToPVS(const Vector& org) = 0;
	virtual void unused_MoveEngineViewTo() = 0;

	virtual void unused_DestroyEntityByHammerId() = 0;
	virtual void unused_GetBaseEntityByEntIndex() = 0;
	virtual void unused_RemoveEntity() = 0;
	virtual void unused_RemoveEntityImmediate() = 0;
	virtual void unused_GetEntityFactoryDictionary() = 0;

	virtual void SetMoveType(CBaseEntity* pEntity, int val) = 0;
	virtual void SetMoveType(CBaseEntity* pEntity, int val, int moveCollide) = 0;
	virtual void unused_ResetSequence() = 0;
	virtual void unused_ResetSequenceInfo() = 0;

	virtual void unused_ClearMultiDamage() = 0;
	virtual void unused_ApplyMultiDamage() = 0;
	virtual void unused_AddMultiDamage() = 0;
	virtual void unused_RadiusDamage() = 0;

	virtual void unused_GetTempEntsSystem() = 0;
	virtual void unused_GetTempEntList() = 0;

	virtual void unused_GetEntityList() = 0;
	virtual void unused_IsEntityPtr() = 0;
	virtual CBaseEntity* FindEntityByClassname(CBaseEntity* pStartEntity, const char* szName) = 0;
	virtual void unused_FindEntityByName() = 0;
	virtual void unused_FindEntityInSphere() = 0;
	virtual void unused_FindEntityByTarget() = 0;
	virtual void unused_FindEntityByModel() = 0;
	virtual void unused_FindEntityByNameNearest() = 0;
	virtual void unused_FindEntityByNameWithin() = 0;
	virtual void unused_FindEntityByClassnameNearest() = 0;
	virtual CBaseEntity* FindEntityByClassnameWithin(CBaseEntity* pStartEntity, const char* szName,
													 const Vector& vecSrc, float flRadius) = 0;
	virtual CBaseEntity* FindEntityByClassnameWithin(CBaseEntity* pStartEntity, const char* szName,
													 const Vector& vecMins, const Vector& vecMaxs) = 0;
	virtual void unused_FindEntityGeneric() = 0;
	virtual void unused_FindEntityGenericWithin() = 0;
	virtual void unused_FindEntityGenericNearest() = 0;
	virtual void unused_FindEntityNearestFacing() = 0;
	virtual void unused_FindEntityClassNearestFacing() = 0;
	virtual void unused_FindEntityProcedural() = 0;
};
