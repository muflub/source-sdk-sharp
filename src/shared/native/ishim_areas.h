//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// THE CONTRACT (hand-kept since C6a; tools/shim-areas reads it). Append-only: an edit is an ABI change.
//
// plan_engineapi_vtables.md Phase C: every EngineApi slot the server fills,
// as a slot of one of these interfaces, acquired from IShimRoot by the version
// string beside it. Shared bases are what both DLLs will implement; each
// extension appends the server-only slots of the same family. Rules as
// ishim_frame.h: no virtual destructor, blittable arguments, APPEND-ONLY.

#ifndef ISHIM_AREAS_H
#define ISHIM_AREAS_H
#pragma once

#include <stdint.h>
#include "tier0/platform.h"
#include "sourcesharp.h"

#define SOURCESHARP_SHIM_ENGINE_VERSION "SourceSharpEngine001"
#define SOURCESHARP_SHIM_CONSOLESERVER_VERSION "SourceSharpConsoleServer001"
#define SOURCESHARP_SHIM_EVENTS_VERSION "SourceSharpEvents001"
#define SOURCESHARP_SHIM_EVENTSSERVER_VERSION "SourceSharpEventsServer001"
#define SOURCESHARP_SHIM_STRINGTABLES_VERSION "SourceSharpStringTables001"
#define SOURCESHARP_SHIM_NETWORK_VERSION "SourceSharpNetwork001"
#define SOURCESHARP_SHIM_MODELS_VERSION "SourceSharpModels001"
#define SOURCESHARP_SHIM_PRECACHE_VERSION "SourceSharpPrecache001"
#define SOURCESHARP_SHIM_MOVEMENT_VERSION "SourceSharpMovement001"
#define SOURCESHARP_SHIM_WORLD_VERSION "SourceSharpWorld001"
#define SOURCESHARP_SHIM_ENTITIES_VERSION "SourceSharpEntities001"
#define SOURCESHARP_SHIM_ENTITYLIFECYCLE_VERSION "SourceSharpEntityLifecycle001"
#define SOURCESHARP_SHIM_TRANSFORM_VERSION "SourceSharpTransform001"
#define SOURCESHARP_SHIM_HIERARCHY_VERSION "SourceSharpHierarchy001"
#define SOURCESHARP_SHIM_COLLISION_VERSION "SourceSharpCollision001"
#define SOURCESHARP_SHIM_PHYSICS_VERSION "SourceSharpPhysics001"
#define SOURCESHARP_SHIM_CONSTRAINTS_VERSION "SourceSharpConstraints001"
#define SOURCESHARP_SHIM_SPRINGS_VERSION "SourceSharpSprings001"
#define SOURCESHARP_SHIM_MOTIONCONTROLLERS_VERSION "SourceSharpMotionControllers001"
#define SOURCESHARP_SHIM_DAMAGE_VERSION "SourceSharpDamage001"
#define SOURCESHARP_SHIM_GAMERULES_VERSION "SourceSharpGameRules001"
#define SOURCESHARP_SHIM_PLAYERS_VERSION "SourceSharpPlayers001"
#define SOURCESHARP_SHIM_WEAPONS_VERSION "SourceSharpWeapons001"
#define SOURCESHARP_SHIM_INVENTORY_VERSION "SourceSharpInventory001"
#define SOURCESHARP_SHIM_SOUND_VERSION "SourceSharpSound001"
#define SOURCESHARP_SHIM_EFFECTS_VERSION "SourceSharpEffects001"
#define SOURCESHARP_SHIM_DEBUGOVERLAY_VERSION "SourceSharpDebugOverlay001"
#define SOURCESHARP_SHIM_DIAGNOSTICS_VERSION "SourceSharpDiagnostics001"
#define SOURCESHARP_SHIM_USERMESSAGES_VERSION "SourceSharpUserMessages001"
#define SOURCESHARP_SHIM_STRINGTABLESSERVER_VERSION "SourceSharpStringTablesServer001"
#define SOURCESHARP_SHIM_NETWORKSERVER_VERSION "SourceSharpNetworkServer001"
#define SOURCESHARP_SHIM_MODELSSERVER_VERSION "SourceSharpModelsServer001"
#define SOURCESHARP_SHIM_MOVEMENTSERVER_VERSION "SourceSharpMovementServer001"
#define SOURCESHARP_SHIM_WORLDSERVER_VERSION "SourceSharpWorldServer001"
#define SOURCESHARP_SHIM_ENTITIESSERVER_VERSION "SourceSharpEntitiesServer001"
#define SOURCESHARP_SHIM_WEAPONSSERVER_VERSION "SourceSharpWeaponsServer001"
#define SOURCESHARP_SHIM_EFFECTSSERVER_VERSION "SourceSharpEffectsServer001"
#define SOURCESHARP_SHIM_COLLISIONSERVER_VERSION "SourceSharpCollisionServer001"

abstract_class IShimEngine
{
public:
	virtual float GetCurrentTime() = 0;
	virtual void * GetGlobalVarsPtr() = 0;
	virtual int32_t FilesGameFileSize( const char * pszPath ) = 0;
	virtual int32_t FilesReadGameFile( const char * pszPath, char * pOut, int32_t nOutSize ) = 0;
	virtual int32_t FilesSizeOnPath( const char * pszPath, const char * pszPathId ) = 0;
	virtual int32_t FilesReadOnPath( const char * pszPath, const char * pszPathId, void * pOut, int32_t nOutSize ) = 0;
	virtual void SoundPatchEmit( int32_t hPatch, float flVolume01, float flPitch, int32_t nFlags, float flStartTime, int32_t bEmitCaption ) = 0;
	virtual void SoundPatchStop( int32_t hPatch ) = 0;
	virtual void SoundPatchState( int32_t hPatch, int32_t * pnEntIndex, int32_t * pbFilterActive ) = 0;
	virtual void SoundPatchFree( int32_t hPatch ) = 0;
	virtual int32_t SoundScriptParameters( const char * pSoundName, float * pOut ) = 0;
	virtual int32_t SoundPatchCreate( uint32_t hEntity, int32_t nChannel, const char * pSoundName, int32_t nSoundLevel ) = 0;
	virtual int32_t SurfaceScrapeSound( int32_t nSurfaceProps, int32_t bSmooth, char * pOut, int32_t nOut ) = 0;
};

abstract_class IShimConsoleServer
{
public:
	virtual void ExecuteServerCommand( const char * pCommand ) = 0;
	virtual void SendClientCommand( uint32_t hPlayer, const char * pCommand ) = 0;
	virtual uint32_t GetCommandClientHandle() = 0;
};

abstract_class IShimEvents
{
public:
	virtual void ListenForGameEvent( const char * pEventName ) = 0;
	virtual int32_t EventGetInt( void * hEvent, const char * pKey ) = 0;
	virtual float EventGetFloat( void * hEvent, const char * pKey ) = 0;
	virtual int32_t EventGetBool( void * hEvent, const char * pKey ) = 0;
	virtual const char * EventGetString( void * hEvent, const char * pKey ) = 0;
};

abstract_class IShimStringTables
{
public:
	virtual int32_t StringTableFind( const char * pszName ) = 0;
	virtual int32_t StringTableFindStringIndex( int32_t nTableId, const char * pszValue ) = 0;
	virtual int32_t StringTableGetString( int32_t nTableId, int32_t nStringIndex, char * pOut, int32_t nOutSize ) = 0;
	virtual int32_t StringTableGetNumStrings( int32_t nTableId ) = 0;
};

abstract_class IShimNetwork
{
public:
	virtual int32_t NetHierarchyDeclare( int32_t nBlobBytes ) = 0;
	virtual int32_t NetPublishType( const char * pClassName, int32_t nShell, uint32_t nSchemaHash, int32_t nLayoutBytes, uint32_t * pOutTypeId ) = 0;
	virtual int32_t NetTierInfo( int32_t nShell, int32_t iTier, const char ** ppOutTable, int32_t * pOutBytes, int32_t * pOutWords, int32_t * pOutProps, int32_t * pOutLive ) = 0;
	virtual int32_t WorldNetDeclare( int32_t nBlobBytes ) = 0;
};

abstract_class IShimModels
{
public:
	virtual int32_t ModelDescriptor( int32_t nModelIndex, char * pBuffer, int32_t nBytes ) = 0;
	virtual void EntityGetModelName( uint32_t hEntity, char * pOut, int32_t nOutSize ) = 0;
	virtual int32_t EntitiesInvalidateMdlCacheAll() = 0;
	virtual void StandaloneSetModelScale( uint32_t hEntity, float flScale ) = 0;
};

abstract_class IShimPrecache
{
public:
	virtual int32_t PrecacheModel( const char * pszModel ) = 0;
	virtual void PrecacheSound( const char * pSoundName ) = 0;
	virtual int32_t PrecacheDecal( const char * pName ) = 0;
	virtual void PrecacheMaterial( const char * pMaterialName ) = 0;
	virtual void PrecacheParticleSystem( const char * pszName ) = 0;
};

abstract_class IShimMovement
{
public:
	virtual void MoveTrace( const float * pStartXYZ, const float * pEndXYZ, const float * pMinsXYZ, const float * pMaxsXYZ, int32_t nMask, int32_t nCollisionGroup, SourceSharpMoveTraceResult * pOut ) = 0;
	virtual int32_t MovePointContents( const float * pXYZ, int32_t nSlot ) = 0;
	virtual int32_t MoveSurface( int32_t nSurfaceProps, SourceSharpMoveSurface * pOut ) = 0;
	virtual int32_t MoveEffect( int32_t nKind, const float * pXYZ, float flValue, int32_t nValue ) = 0;
	virtual float CurrentGravity() = 0;
	virtual void RegisterMovementParams( const SourceSharpMovementParams * pParams ) = 0;
};

abstract_class IShimWorld
{
public:
	virtual int32_t PointContents( const float * pOrigin ) = 0;
	virtual int32_t SurfacePropGameMaterial( int32_t nSurfaceProp ) = 0;
};

abstract_class IShimEntities
{
public:
	virtual int32_t EntityGetIndex( uint32_t hEntity ) = 0;
	virtual void EntityGetOrigin( uint32_t hEntity, float * pOutXYZ ) = 0;
	virtual void EntityGetAngles( uint32_t hEntity, float * pOutPYR ) = 0;
	virtual void EntitySetSkin( uint32_t hEntity, int32_t nSkin ) = 0;
	virtual int32_t EntityIsValid( uint32_t hEntity ) = 0;
};

abstract_class IShimEntityLifecycle
{
public:
	virtual uint32_t CreateEntity( const char * pClassName ) = 0;
	virtual void EntitySpawn( uint32_t hEntity ) = 0;
	virtual void EntityRemove( uint32_t hEntity ) = 0;
	virtual void DestroyShell( const uint32_t * pHandles, int32_t nCount ) = 0;
	virtual uint32_t MapSpawnCreate( const char * pClassName, const char * pPackedKeys, int32_t nBytes ) = 0;
	virtual int32_t TemplateCapture( const char * pszSourceName, int32_t bRemoveSources, int32_t * pOutIndices, float * pOutOriginsXYZ, float * pOutAnglesPYR, int32_t nMax ) = 0;
	virtual void EntitiesPurgeMapRefs() = 0;
	virtual void EntitiesCleanupDeleteList() = 0;
	virtual void RegisterScriptedEntity( const char * pClassName, const char ** ppOutputNames, int32_t nOutputCount, int32_t nKind ) = 0;
	virtual void RegisterWeaponEntity( const char * pClassName ) = 0;
	virtual void RegisterClassOps( const char * pClassName, uint32_t nOpMask ) = 0;
	virtual int32_t EntityDispatch( uint32_t hEntity, int32_t nOp, const SourceSharpOpArgs * pArgs ) = 0;
	virtual int32_t EntityFactoryCount() = 0;
	virtual const char * EntityFactoryNameAt( int32_t index ) = 0;
	virtual int32_t EntityFactoryRefusalCount() = 0;
	virtual const char * EntityFactoryRefusalNameAt( int32_t index ) = 0;
	virtual int32_t EntityFactoryRefusalTimesAt( int32_t index ) = 0;
	virtual int32_t TemplateSpawnKeyed( const char * pClassNames, const char * pPackedKeys, const int32_t * pPackBytes, const float * pOriginsXYZ, const float * pAnglesPYR, int32_t nCount, uint32_t * pOutHandles, int32_t nMax ) = 0;
};

abstract_class IShimTransform
{
public:
	virtual void EntitySetOrigin( uint32_t hEntity, const float * pXYZ ) = 0;
	virtual void EntitySetAngles( uint32_t hEntity, const float * pPYR ) = 0;
	virtual void EntityTeleport( uint32_t hEntity, const float * pOrigin, const float * pAngles, int32_t bZeroVelocity ) = 0;
	virtual void EntityGetVelocity( uint32_t hEntity, float * pOutXYZ ) = 0;
	virtual void EntitySetVelocity( uint32_t hEntity, const float * pXYZ ) = 0;
	virtual void EntityAddBaseVelocity( uint32_t hEntity, const float * pVelocityXYZ ) = 0;
	virtual void EntityGetAngularVelocity( uint32_t hEntity, float * pOutPYR ) = 0;
	virtual void EntitySetAngularVelocity( uint32_t hEntity, const float * pPYR ) = 0;
	virtual void EntityGetEyePosition( uint32_t hEntity, float * pOutXYZ ) = 0;
	virtual void EntityGetEyeAngles( uint32_t hEntity, float * pOutPYR ) = 0;
	virtual void EntityGetWorldSpaceCenter( uint32_t hEntity, float * pOutXYZ ) = 0;
	virtual void EntityMoveToPosition( uint32_t hEntity, const float * pXYZ, float flSpeed ) = 0;
	virtual void EntityMoveToAngles( uint32_t hEntity, const float * pPYR, float flSpeed ) = 0;
	virtual void EntityStopMoving( uint32_t hEntity ) = 0;
	virtual float EntityGetGravity( uint32_t hEntity ) = 0;
	virtual void EntitySetGravity( uint32_t hEntity, float flGravity ) = 0;
	virtual void EntitySetFriction( uint32_t hEntity, float flFriction ) = 0;
	virtual void EntitySetBounce( uint32_t hEntity, int32_t nBounces ) = 0;
};

abstract_class IShimHierarchy
{
public:
	virtual int32_t EntitySetParent( uint32_t hChild, uint32_t hParent, int32_t nParentAttachment ) = 0;
	virtual uint32_t EntityGetParent( uint32_t hChild ) = 0;
	virtual int32_t EntityGetParentAttachment( uint32_t hChild ) = 0;
	virtual void EntitySetLocalTransform( uint32_t hChild, const float * pOrigin, const float * pAngles ) = 0;
	virtual void EntityGetLocalTransform( uint32_t hChild, float * pOutOrigin, float * pOutAngles ) = 0;
	virtual int32_t EntityParentProbe( uint32_t hEntity ) = 0;
	virtual int32_t EntityGetAttachmentFrame( uint32_t hEntity, int32_t nAttachment, float * pOutOriginXYZ, float * pOutAnglesPYR ) = 0;
	virtual int32_t EntityLookupAttachmentAny( uint32_t hEntity, const char * pAttachmentName ) = 0;
};

abstract_class IShimCollision
{
public:
	virtual void RegisterCollisionRules( const SourceSharpCollisionRules * pRules ) = 0;
};

abstract_class IShimPhysics
{
public:
	virtual void EntityDestroyPhysics( uint32_t hEntity ) = 0;
	virtual void EntitySetForceServerRagdoll( uint32_t hEntity, int32_t bForce ) = 0;
	virtual void * EntityPhysicsObject( uint32_t hEntity ) = 0;
	virtual void * PhysicsEnvironmentPointer() = 0;

	virtual void * PhysicsWorldObject() = 0;
	virtual int32_t PhysicsUpdateOwners( void ** ppObjects, float * pVelocities, int32_t nCount, uint32_t * pOutOwners ) = 0;
	virtual int32_t EntitySwapPhysicsObject( uint32_t hEntity, void * pObject, void ** ppPrevious ) = 0;
};

abstract_class IShimDamage
{
public:
	virtual void ApplyDamage( uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType ) = 0;
	virtual void EntityTakeDamage( uint32_t hEntity, uint32_t hAttacker, float flDamage, int32_t nDamageType, const float * pDirXYZ, const float * pForceOriginXYZ, int32_t bPhysicsOnly ) = 0;
	virtual void RadiusDamage( const float * pOrigin, float flDamage, float flRadius, int32_t nDamageType, uint32_t hAttacker ) = 0;
	virtual void ExplosionDamage( const float * pOrigin, float flMagnitude, float flRadius, int32_t nDamageType, uint32_t hAttacker, uint32_t hInflictor, uint32_t hIgnored, float flDamageForce ) = 0;
	virtual int32_t EntitySetTakeDamageMode( uint32_t hEntity, int32_t nMode ) = 0;
	virtual void EntityIgnite( uint32_t hEntity, float flLifetime ) = 0;
	virtual int32_t EntityDissolve( uint32_t hEntity, int32_t nType, float flMagnitude, const float * pOriginXYZ ) = 0;
	virtual void ApplyDamageForce( uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType, const float * pForceXYZ, const float * pPositionXYZ ) = 0;
};

abstract_class IShimGameRules
{
public:
	virtual void RegisterGameRulesPolicy( const SourceSharpGameRulesPolicy * pPolicy ) = 0;
	virtual void RegisterDamageRules( const SourceSharpDamageRules * pRules ) = 0;
	virtual void EndRound() = 0;
};

abstract_class IShimPlayers
{
public:
	virtual uint32_t GetPlayerHandle( int32_t nPlayerIndex ) = 0;
	virtual int32_t GetPlayerUserId( uint32_t hPlayer ) = 0;
	virtual int32_t GetPlayerTeam( uint32_t hPlayer ) = 0;
	virtual void AddPlayerScore( uint32_t hPlayer, int32_t nPoints, int32_t bAllowNegative ) = 0;
	virtual int32_t PlayerIsAlive( uint32_t hPlayer ) = 0;
	virtual void PlayerRespawn( uint32_t hPlayer ) = 0;
	virtual void PlayerPlaceAtSpawn( uint32_t hPlayer, const float * pOriginXYZ, const float * pAnglesPYR ) = 0;
	virtual int32_t PlayerGetObserverMode( uint32_t hPlayer ) = 0;
	virtual void PlayerSetObserverMode( uint32_t hPlayer, int32_t nMode ) = 0;
	virtual void PlayerSetViewEntity( uint32_t hPlayer, uint32_t hView ) = 0;
	virtual void PlayerSetFov( uint32_t hPlayer, uint32_t hRequester, int32_t nFov, float flRate ) = 0;
	virtual void PlayerSnapEyeAngles( uint32_t hPlayer, const float * pPitchYawRoll ) = 0;
	virtual void GetPlayerShootOrigin( uint32_t hPlayer, float * pOutXYZ ) = 0;
	virtual void PlayerSetSoundscape( uint32_t hPlayer, int32_t nEntIndex, int32_t nSoundscapeIndex, int32_t nLocalBits, const float * pLocalPositions ) = 0;
	virtual int32_t ClientsStandalonePlayerEarPosition( int32_t iEdict, float * pOutXYZ ) = 0;
	virtual int32_t ClientsSdkPlayerEarPosition( int32_t iEdict, float * pOutXYZ ) = 0;
	// "Is this world-space box inside this player's PVS?" The engine's own
	// cluster/PVS read — GetClusterForOrigin at the player's eyes,
	// GetPVSForCluster, CheckBoxInPVS — the same trio stock's check-client
	// machinery builds (triggers.cpp:1615, 1711). Native carries out the
	// read; who asks and what they do with the answer is managed code's
	// decision (stock's CTriggerHurt::RadiationThink gates a geiger ping).
	virtual int32_t PlayerBoxInPvs( uint32_t hPlayer, const float * pMins, const float * pMaxs ) = 0;
};

abstract_class IShimWeapons
{
public:
	virtual void RegisterAmmoType( const char * pName, int32_t nDamageType, int32_t nMaxCarry, int32_t nTracerType, float flDamageForce, float flDamage ) = 0;
	virtual void RegisterWeaponType( const char * pClassName, float flFireRate, float flSpreadDegrees, int32_t nPellets ) = 0;
};

abstract_class IShimInventory
{
public:
	virtual int32_t GivePlayerItem( uint32_t hPlayer, const char * pClassName ) = 0;
	virtual int32_t GivePlayerAmmo( uint32_t hPlayer, int32_t nCount, const char * pAmmoName ) = 0;
	virtual void GetPlayerActiveWeapon( uint32_t hPlayer, char * pOut, int32_t nOutSize ) = 0;
	virtual uint32_t PlayerGetActiveWeaponEntity( uint32_t hPlayer ) = 0;
	virtual uint32_t PlayerGetWeaponEntity( uint32_t hPlayer, int32_t nSlot ) = 0;
	virtual uint32_t PlayerGetViewModelEntity( uint32_t hPlayer ) = 0;
	virtual int32_t PlayerDropActiveWeapon( uint32_t hPlayer, const float * pVelocityXYZ ) = 0;
	virtual int32_t PlayerHolsterActiveWeapon( uint32_t hPlayer ) = 0;
	virtual int32_t PlayerDeployLastWeapon( uint32_t hPlayer ) = 0;
	virtual void PlayerRemoveAllItems( uint32_t hPlayer, int32_t bStripSuit ) = 0;
};

abstract_class IShimSound
{
public:
	virtual void EntityEmitSound( uint32_t hEntity, const char * pSoundName ) = 0;
	virtual void EntityStopSound( uint32_t hEntity, const char * pSoundName ) = 0;
	virtual void EmitSoundAtPosition( const char * pSoundName, const float * pXYZ ) = 0;
	virtual void EmitAmbientSound( int32_t nEntIndex, const float * pXYZ, const char * pSoundName, float flVolume, int32_t nSoundLevel, int32_t nFlags, int32_t nPitch ) = 0;
	virtual int32_t EmitWorldSoundScript( const char * pszName, const float * pXYZ, int32_t nChannel, float flVolume ) = 0;
};

abstract_class IShimEffects
{
public:
	virtual int32_t TempEntRegister( const SourceSharpTeClass_t * pDesc ) = 0;
};

abstract_class IShimDebugOverlay
{
public:
	virtual void DrawWorldText( const float * pOriginXYZ, int32_t nLine, const char * pText, float flDuration, int32_t r, int32_t g, int32_t b, int32_t a ) = 0;
	virtual int32_t DrawEntityOverlay( uint32_t hEntity, int32_t nLine, const char * pText, float flDuration, int32_t r, int32_t g, int32_t b, int32_t a ) = 0;
	virtual void AddLineOverlay( const float * pStartXYZ, const float * pEndXYZ, int32_t r, int32_t g, int32_t b, int32_t bNoDepthTest, float flDuration ) = 0;
	virtual void OverlayCounts( uint32_t * pOutDrawn, uint32_t * pOutClipped, uint32_t * pOutNoViewer, uint32_t * pOutNoOverlay, int32_t * pOutViewerSet ) = 0;
	virtual void OverlayResetCounts() = 0;
};

abstract_class IShimDiagnostics
{
public:
	virtual int32_t PlayerShadowCounts( int32_t * pOut, int32_t nMax ) = 0;
	virtual int32_t ExitWatchArm( const char * pRecordPath ) = 0;
	virtual int32_t ExitWatchRead( int32_t * pOut, int32_t nMax ) = 0;
	virtual int32_t ExitWatchDisarm() = 0;
};

abstract_class IShimUserMessages
{
public:
	virtual int32_t UserMessageSend( int32_t nMsgId, const unsigned char * pData, int32_t nBytes, int32_t nPlayerIndex, int32_t bReliable ) = 0;
};

abstract_class IShimEventsServer : public IShimEvents
{
public:
	virtual void * EventCreate( const char * pName ) = 0;
	virtual void EventSetInt( void * hEvent, const char * pKey, int32_t nValue ) = 0;
	virtual void EventSetFloat( void * hEvent, const char * pKey, float flValue ) = 0;
	virtual void EventSetBool( void * hEvent, const char * pKey, int32_t bValue ) = 0;
	virtual void EventSetString( void * hEvent, const char * pKey, const char * pValue ) = 0;
	virtual int32_t EventFire( void * hEvent ) = 0;
	virtual void EventFree( void * hEvent ) = 0;
};

abstract_class IShimStringTablesServer : public IShimStringTables
{
public:
	virtual int32_t StringTableCreate( const char * pszName, int32_t nMaxEntries ) = 0;
	virtual int32_t StringTableAddString( int32_t nTableId, const char * pszValue ) = 0;
};

abstract_class IShimNetworkServer : public IShimNetwork
{
public:
	virtual void * EntityGetDataBlob( uint32_t hEntity, int32_t * pOutSize ) = 0;
	virtual void * EntityBindBlob( uint32_t hEntity, int32_t * pOutSize, SourceSharpEntitySlot_t ** pOutSlot, uint32_t * pOutSerial ) = 0;
	virtual void EntityMarkStateChanged( uint32_t hEntity ) = 0;
	virtual void EntityMarkBlockChanged( uint32_t hEntity, int32_t nBlockOffset ) = 0;
	virtual int32_t EntityPayloadAgreement( uint32_t hEntity, int32_t * pOutWords, int32_t * pOutFirstBadWord ) = 0;
	virtual void * EntityGetEngineBlock( uint32_t hEntity, int32_t * pOutSize ) = 0;
	virtual void EntitySetTransmitAlways( uint32_t hEntity, int32_t bAlways ) = 0;
};

abstract_class IShimModelsServer : public IShimModels
{
public:
	virtual void EntitySetModel( uint32_t hEntity, const char * pModel ) = 0;
	virtual void EntitySetSequence( uint32_t hEntity, int32_t nSequence ) = 0;
	virtual int32_t EntityGetTextureFrame( uint32_t hEntity ) = 0;
	virtual void EntitySetTextureFrame( uint32_t hEntity, int32_t nFrame ) = 0;
};

abstract_class IShimMovementServer : public IShimMovement
{
public:
	virtual int32_t PlayerRunMove( uint32_t hPlayer, int32_t nIndex, int32_t nStage ) = 0;
	virtual int32_t PlayerShadowFacts( uint32_t hPlayer, SourceSharpPlayerShadowFacts * pOut ) = 0;
	virtual int32_t PlayerShadowAdopt( uint32_t hPlayer, void * pStand, void * pCrouch, void * pController ) = 0;
	virtual void PlayerShadowRelease( uint32_t hPlayer ) = 0;
	virtual void PlayerShadowCommit( uint32_t hPlayer, int32_t nState, int32_t bAfterCommand ) = 0;
	virtual int32_t PlayerSetMoveType( uint32_t hPlayer, int32_t nMoveType ) = 0;
	virtual void PlayerForceButtons( uint32_t hPlayer, int32_t nButtonMask, int32_t bForced ) = 0;
	virtual int32_t PlayerGetButtons( uint32_t hPlayer ) = 0;
	virtual void PlayerSetButtonsDisabled( uint32_t hPlayer, int32_t nButtonMask, int32_t bDisabled ) = 0;
	virtual void PlayerSetLaggedMovement( uint32_t hPlayer, float flValue ) = 0;
	virtual void PlayerSetAllowAutoMovement( uint32_t hPlayer, int32_t bAllow ) = 0;
	virtual void PlayerSetSlowMovement( uint32_t hPlayer, int32_t bSlow ) = 0;
	virtual void PlayerSetMovementConstraint( uint32_t hPlayer, const float * pCenterXYZ, float flRadius, float flWidth, float flSpeedFactor ) = 0;
	virtual void PlayerSetFrozen( uint32_t hPlayer, int32_t bFrozen ) = 0;
};

abstract_class IShimWorldServer : public IShimWorld
{
public:
	virtual int32_t TraceLine( const float * pStartXYZ, const float * pEndXYZ, int32_t nMask, uint32_t hIgnore, float * pOutFraction, float * pOutEndXYZ, float * pOutNormalXYZ, uint32_t * pOutHit ) = 0;
	virtual int32_t TraceHull( const float * pStartXYZ, const float * pEndXYZ, const float * pMinsXYZ, const float * pMaxsXYZ, int32_t nMask, uint32_t hIgnore, uint32_t * pOutHit, int32_t * pOutStartSolid ) = 0;
	virtual int32_t TraceLineSurface( const float * pStartXYZ, const float * pEndXYZ, int32_t nMask, uint32_t hIgnore, float * pOutFraction, float * pOutEndXYZ, float * pOutNormalXYZ, uint32_t * pOutHit, int32_t * pOutSurfaceProps, int32_t * pOutSurfaceFlags ) = 0;
	virtual uint32_t TracePlayerAim( uint32_t hPlayer, float flRange, float * pOutHitXYZ ) = 0;
	virtual void AngleVectorsFromForward( const float * pForwardXYZ, float * pOutRightXYZ, float * pOutUpXYZ ) = 0;
	virtual int32_t WaterSurfaceBelow( const float * pStartXYZ, float * pOutXYZ, int32_t * pOutSlime ) = 0;
	virtual int32_t SurfacePropIndex( const char * pszName ) = 0;
	virtual int32_t EntityModelSurfaceProp( uint32_t hEntity ) = 0;
	virtual int32_t InvisibleRangeReach() = 0;
	virtual void EngineLightStyle( int32_t nStyle, const char * pPattern ) = 0;
	virtual void SetSkyboxParams( const SourceSharpSkyboxParams * pParams ) = 0;
	virtual const char * WorldChapterTitle() = 0;
	virtual uint32_t AreaPortalCreate( int32_t nPortalNumber, int32_t nPortalVersion ) = 0;
	virtual void AreaPortalDestroy( uint32_t hPortal ) = 0;
	virtual void AreaPortalSetOpen( uint32_t hPortal, int32_t bOpen ) = 0;
	virtual void AreaPortalSetWindow( uint32_t hPortal, uint32_t hEntity, float flFadeDist ) = 0;
	virtual int32_t EntityTraceHull( uint32_t hEntity, const float * pStartXYZ, const float * pEndXYZ, const float * pMinsXYZ, const float * pMaxsXYZ, int32_t nMask, int32_t nCollisionGroup, float * pOutEndXYZ ) = 0;
};

abstract_class IShimEntitiesServer : public IShimEntities
{
public:
	virtual int32_t EntityIsPlayer( uint32_t hEntity ) = 0;
	virtual void EntityGetClassName( uint32_t hEntity, char * pOut, int32_t nOutSize ) = 0;
	virtual void EntityGetName( uint32_t hEntity, char * pOut, int32_t nOutSize ) = 0;
	virtual int32_t EntityGetState( uint32_t hEntity, int32_t * pOutMoveType ) = 0;
	virtual uint32_t FindEntityByClassname( uint32_t hStartAfter, const char * pClassName ) = 0;
	virtual uint32_t FindBaseEntityByName( const char * pName ) = 0;
	virtual int32_t EntitiesInSphere( const float * pCenterXYZ, float flRadius, uint32_t * pOutHandles, int32_t nMax ) = 0;
	virtual int32_t EntitySetKeyValue( uint32_t hEntity, const char * pKey, const char * pValue ) = 0;
	virtual void EntityGetSpawnOrigin( uint32_t hEntity, float * pOutXYZ ) = 0;
	virtual void EntityGetSpawnAngles( uint32_t hEntity, float * pOutPYR ) = 0;
	virtual int32_t EntityGetSpawnFlags( uint32_t hEntity ) = 0;
	virtual void EntitySetSpawnFlags( uint32_t hEntity, int32_t nSpawnFlags ) = 0;
	virtual int32_t EntityGetHealth( uint32_t hEntity ) = 0;
	virtual void EntitySetHealth( uint32_t hEntity, int32_t nHealth ) = 0;
	virtual int32_t EntityGetArmor( uint32_t hEntity ) = 0;
	virtual void EntitySetArmor( uint32_t hEntity, int32_t nArmor ) = 0;
	virtual void EntitySetVisible( uint32_t hEntity, int32_t bVisible ) = 0;
	virtual void EntitySetOwner( uint32_t hEntity, uint32_t hOwner ) = 0;
	virtual void EntityMakePickup( uint32_t hEntity, float flSizeX, float flSizeY, float flSizeZ ) = 0;
	virtual void EntitySetPickupEnabled( uint32_t hEntity, int32_t bEnabled ) = 0;
	virtual int32_t EntityDetectInSkybox( uint32_t hEntity ) = 0;
	virtual int32_t EntitiesSdkPlayerSkyboxArea( int32_t iEdict, int32_t * pOutArea ) = 0;
	virtual int32_t EntitiesStandalonePlayerSkyboxArea( int32_t iEdict, int32_t * pOutArea ) = 0;
};

abstract_class IShimWeaponsServer : public IShimWeapons
{
public:
	virtual int32_t WeaponGetState( uint32_t hWeapon, SourceSharpWeaponState * pOut ) = 0;
	virtual void WeaponIdle( uint32_t hWeapon ) = 0;
	virtual void WeaponSetNextPrimaryAttack( uint32_t hWeapon, float flTime ) = 0;
	virtual void WeaponSetNextSecondaryAttack( uint32_t hWeapon, float flTime ) = 0;
	virtual void WeaponSetClip( uint32_t hWeapon, int32_t nClip ) = 0;
	virtual void WeaponSetInReload( uint32_t hWeapon, int32_t bInReload ) = 0;
	virtual int32_t WeaponTakeAmmo( uint32_t hWeapon, int32_t nCount ) = 0;
	virtual int32_t WeaponTakeSecondaryAmmo( uint32_t hWeapon, int32_t nCount ) = 0;
	virtual void WeaponPlaySound( uint32_t hWeapon, int32_t nSound ) = 0;
	virtual void WeaponSendAnim( uint32_t hWeapon, int32_t nAnim ) = 0;
	virtual float WeaponAnimDuration( uint32_t hWeapon, int32_t nAnim ) = 0;
	virtual void WeaponFireBullets( uint32_t hWeapon, int32_t nShots, float flSpreadDegrees ) = 0;
	virtual void WeaponMuzzleFlash( uint32_t hWeapon ) = 0;
	virtual void WeaponAddViewKick( uint32_t hWeapon ) = 0;
};

abstract_class IShimCollisionServer : public IShimCollision
{
public:
	virtual void EntityGetBounds( uint32_t hEntity, float * pOutMinsXYZ, float * pOutMaxsXYZ ) = 0;
	virtual void EntityGetBoundsSize( uint32_t hEntity, float * pOutXYZ ) = 0;
	virtual void EntitySetBounds( uint32_t hEntity, const float * pMinsXYZ, const float * pMaxsXYZ ) = 0;
	virtual int32_t EntityGetSolidType( uint32_t hEntity ) = 0;
	virtual void EntitySetSolidity( uint32_t hEntity, int32_t nSolidType, int32_t nSolidFlags ) = 0;
	virtual int32_t EntityGetSolidFlags( uint32_t hEntity ) = 0;
	virtual void EntitySetNotSolid( uint32_t hEntity, int32_t bNotSolid ) = 0;
	virtual void EntitySetBrushSolidity( uint32_t hEntity, int32_t nSolidity ) = 0;
	virtual int32_t EntityGetCollisionGroup( uint32_t hEntity ) = 0;
	virtual void EntitySetCollisionGroup( uint32_t hEntity, int32_t nGroup ) = 0;

	virtual int32_t EntityOverlapReport( uint32_t * pOutPairs, int32_t nMaxPairs ) = 0;

};

abstract_class IShimEffectsServer : public IShimEffects
{
public:
	virtual void TempEntPlayback( int32_t hClass, const void * pBlob, int32_t nBytes, const SourceSharpTeFilter_t * pFilter, float flDelay ) = 0;
	virtual int32_t EffectDispatchIndex( const char * pName ) = 0;
	virtual void ExplosionEffect( const float * pOrigin, int32_t nModelIndex, float flScale, int32_t nFrameRate, int32_t nFlags, int32_t nRadius, int32_t nMagnitude ) = 0;
	virtual void ViewPunch( const float * pOrigin, const float * pPunchPYR, float flRadius, int32_t bInAir ) = 0;
	virtual void SparkEffect( const float * pOriginXYZ, int32_t nMagnitude, int32_t nTrailLength, int32_t bMakeSound, const float * pDirXYZ ) = 0;
	virtual void ImpactEffect( const float * pStartXYZ, const float * pEndXYZ, int32_t nSurfaceProp, int32_t nDamageType, uint32_t hEntity ) = 0;
	virtual void MuzzleFlashEffect( const float * pOriginXYZ, const float * pAnglesXYZ, float flScale, int32_t nType ) = 0;
	virtual void TracerEffect( const float * pStartXYZ, const float * pEndXYZ ) = 0;
	virtual void BubbleEffect( uint32_t hEntity, int32_t nCount, float flSpeed ) = 0;
	virtual void FunnelEffect( const float * pOriginXYZ, int32_t nSpriteIndex, int32_t bReversed ) = 0;
	virtual void DustEffect( const float * pOriginXYZ, const float * pDirXYZ, float flSize, float flSpeed ) = 0;
	virtual void SplashEffect( const float * pOriginXYZ, float flScale, int32_t bInSlime ) = 0;
	virtual void BloodEffect( const float * pOriginXYZ, const float * pDirXYZ, int32_t nColor, float flAmount, int32_t bStream, int32_t nSprayFlags ) = 0;
	virtual int32_t BloodDecal( const float * pStartXYZ, const float * pEndXYZ, int32_t nColor ) = 0;
	virtual int32_t PaintDecal( int32_t nTexture, const float * pOriginXYZ, const float * pAnglesXYZ, float flProjectDistance, int32_t bPermanent, int32_t bLowPriority ) = 0;
	virtual int32_t ShootGib( const char * pszModel, const SourceSharpGibShot * pShot ) = 0;
	virtual uint32_t FireEffectCreate( int32_t nType, const float * pOriginXYZ, float flScale, int32_t nFlags ) = 0;
	virtual void FireEffectScale( uint32_t hEffect, float flScale, float flTime ) = 0;
	virtual void FireEffectEnable( uint32_t hEffect, int32_t bEnable ) = 0;
};

#endif // ISHIM_AREAS_H
