//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A level's native steps. Each method is one line of what stock does; the order,
// conditions and early returns are in C#.
//
// Append only: the forwarder table binds by slot, so never insert, reorder or remove. No
// virtual destructor, which would shift every slot; the singleton is never deleted.

#pragma once



#define SOURCESHARP_SHIM_LEVEL_VERSION "SourceSharpLevel001"

abstract_class IShimLevel
{
public:
	// LevelInit's steps.

	// The point_templates' contents, precached.
	//
	// Empty, but kept as a sequencing point: stock sets low violence before precaching
	// templates, and templates after the entities they read.
	virtual void PrecachePointTemplates( const char *pMapDataBlobs,
	                                     const int32_t *pStringSizes,
	                                     int32_t nCount ) = 0;

	// g_RagdollLVManager.SetLowViolence( pMapName ).
	virtual void SetLowViolence( const char *pMapName ) = 0;

	virtual void LoadMessageOfTheDay() = 0;

	// ServerActivate's and LevelShutdown's steps.

	virtual void SetPrecacheAllowed( int32_t bAllowed ) = 0;

	// The standalone hierarchy's teardown, before the entity list is cleared.
	virtual void ShutdownStandaloneEntities() = 0;

	virtual void ShutdownSoundEnt() = 0;
	virtual void ClearEntityList() = 0;

	// engine->LoadGameState, which only the game DLL calls (from LevelInit's loadGame arm).
	// It drives ReadRestoreHeaders, the header and LIGHTSTYLE reads, and Restore.
	virtual int32_t LoadSaveGameState( const char *pMapName, int32_t bCreatePlayers ) = 0;

	// Whole-file reads on a named search path ("GAME", "BSP"), as bytes. FileSizeOnPath
	// answers -1 for a missing file. ReadFileOnPath answers the bytes read, or -1 for a
	// missing file or one larger than nOutSize; never truncated.
	virtual int32_t FileSizeOnPath( const char *pPath, const char *pPathId ) = 0;
	virtual int32_t ReadFileOnPath( const char *pPath, const char *pPathId,
	                                void *pOut, int32_t nOutSize ) = 0;

	// Creates the world through CWorldFactory from worldspawn's key/value pairs (parsed in
	// managed code), and spawns it. 1 on success, 0 if the factory or the singleton refused.
	virtual int32_t SpawnWorld( const char *const *ppKeys, const char *const *ppValues,
	                            int32_t nKeys ) = 0;

	// Whether a precache is in order now (the flag SetPrecacheAllowed writes). 1 or 0.
	virtual int32_t IsPrecacheAllowed() = 0;

	// gpGlobals->eLoadType, as the managed LevelInit arm chose it.
	virtual void SetLoadType( int32_t eLoadType ) = 0;

	// The native systems' stages, one step each, in the order ServerSystems.cs gives.
	virtual int32_t PhysicsInit() = 0;
	virtual void PhysicsLevelInitPreEntity() = 0;
	virtual void PhysicsLevelInitPostEntity() = 0;
	virtual void PhysicsLevelShutdownPreEntity() = 0;
	virtual void PhysicsLevelShutdownPostEntity() = 0;
	virtual void StringPoolLevelShutdownPostEntity() = 0;

	// Every entity control-block slot closed, after the managed level's end.
	virtual void ResetEntitySlots() = 0;

	// NEW METHODS GO HERE, AT THE END.
};
