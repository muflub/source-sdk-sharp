//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Hand-kept; tools/shim-areas reads it. Every slot is an ABI: append only, no
// virtual destructor, blittable arguments.

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
#define SOURCESHARP_SHIM_PHYSICS_VERSION "SourceSharpPhysics001"
#define SOURCESHARP_SHIM_CONSTRAINTS_VERSION "SourceSharpConstraints001"
#define SOURCESHARP_SHIM_SPRINGS_VERSION "SourceSharpSprings001"
#define SOURCESHARP_SHIM_MOTIONCONTROLLERS_VERSION "SourceSharpMotionControllers001"
#define SOURCESHARP_SHIM_DAMAGE_VERSION "SourceSharpDamage001"
#define SOURCESHARP_SHIM_GAMERULES_VERSION "SourceSharpGameRules001"
#define SOURCESHARP_SHIM_PLAYERS_VERSION "SourceSharpPlayers001"
#define SOURCESHARP_SHIM_WEAPONS_VERSION "SourceSharpWeapons001"
#define SOURCESHARP_SHIM_INVENTORY_VERSION "SourceSharpInventory001"
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
	virtual void *GetGlobalVarsPtr() = 0;
};

abstract_class IShimConsoleServer
{
public:
	virtual void SendClientCommand( uint32_t hPlayer, const char *pCommand ) = 0;
	virtual uint32_t GetCommandClientHandle() = 0;
};

abstract_class IShimEvents
{
public:
	virtual void ListenForGameEvent( const char *pEventName ) = 0;
	virtual int32_t EventGetInt( void *hEvent, const char *pKey ) = 0;
	virtual float EventGetFloat( void *hEvent, const char *pKey ) = 0;
	virtual int32_t EventGetBool( void *hEvent, const char *pKey ) = 0;
	virtual const char *EventGetString( void *hEvent, const char *pKey ) = 0;
};

abstract_class IShimNetwork
{
public:
	virtual int32_t NetHierarchyDeclare( int32_t nBlobBytes ) = 0;
	virtual int32_t NetPublishType( const char *pClassName, int32_t nShell, uint32_t nSchemaHash, int32_t nLayoutBytes,
									uint32_t *pOutTypeId ) = 0;
	virtual int32_t NetTierInfo( int32_t nShell, int32_t iTier, const char **ppOutTable, int32_t *pOutBytes,
								 int32_t *pOutWords, int32_t *pOutProps, int32_t *pOutLive ) = 0;
	virtual int32_t WorldNetDeclare( int32_t nBlobBytes ) = 0;
};

abstract_class IShimModels
{
public:
	virtual void EntityGetModelName( uint32_t hEntity, char *pOut, int32_t nOutSize ) = 0;
	virtual int32_t EntitiesInvalidateMdlCacheAll() = 0;
	virtual void StandaloneSetModelScale( uint32_t hEntity, float flScale ) = 0;
};

abstract_class IShimPrecache
{
public:
	virtual int32_t PrecacheModel( const char *pszModel ) = 0;
};

abstract_class IShimMovement
{
public:
	virtual void MoveTrace( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ, const float *pMaxsXYZ,
							int32_t nMask, int32_t nCollisionGroup, SourceSharpMoveTraceResult *pOut ) = 0;
	virtual int32_t MoveEffect( int32_t nKind, const float *pXYZ, float flValue, int32_t nValue ) = 0;
	virtual float CurrentGravity() = 0;
	virtual void RegisterMovementParams( const SourceSharpMovementParams *pParams ) = 0;
};

abstract_class IShimEntities
{
public:
	virtual int32_t EntityGetIndex( uint32_t hEntity ) = 0;
	virtual void EntityGetOrigin( uint32_t hEntity, float *pOutXYZ ) = 0;
	virtual void EntityGetAngles( uint32_t hEntity, float *pOutPYR ) = 0;
	virtual int32_t EntityIsValid( uint32_t hEntity ) = 0;
};

abstract_class IShimEntityLifecycle
{
public:
	virtual uint32_t CreateEntity( const char *pClassName ) = 0;
	virtual void EntitySpawn( uint32_t hEntity ) = 0;
	virtual void EntityRemove( uint32_t hEntity ) = 0;
	virtual void DestroyShell( const uint32_t *pHandles, int32_t nCount ) = 0;
	virtual uint32_t MapSpawnCreate( const char *pClassName, const char *pPackedKeys, int32_t nBytes ) = 0;
	virtual void RegisterScriptedEntity( const char *pClassName, const char **ppOutputNames, int32_t nOutputCount,
										 int32_t nKind ) = 0;
	virtual void RegisterClassOps( const char *pClassName, uint32_t nOpMask ) = 0;
	virtual int32_t EntityDispatch( uint32_t hEntity, int32_t nOp, const SourceSharpOpArgs *pArgs ) = 0;
	virtual int32_t EntityFactoryCount() = 0;
	virtual const char *EntityFactoryNameAt( int32_t index ) = 0;
	virtual int32_t EntityFactoryRefusalCount() = 0;
	virtual const char *EntityFactoryRefusalNameAt( int32_t index ) = 0;
	virtual int32_t EntityFactoryRefusalTimesAt( int32_t index ) = 0;
	virtual int32_t TemplateSpawnKeyed( const char *pClassNames, const char *pPackedKeys, const int32_t *pPackBytes,
										const float *pOriginsXYZ, const float *pAnglesPYR, int32_t nCount,
										uint32_t *pOutHandles, int32_t nMax ) = 0;
};

abstract_class IShimTransform
{
public:
	virtual void EntitySetOrigin( uint32_t hEntity, const float *pXYZ ) = 0;
	virtual void EntitySetAngles( uint32_t hEntity, const float *pPYR ) = 0;
	virtual void EntityTeleport( uint32_t hEntity, const float *pOrigin, const float *pAngles,
								 int32_t bZeroVelocity ) = 0;
	virtual void EntityGetVelocity( uint32_t hEntity, float *pOutXYZ ) = 0;
	virtual void EntitySetVelocity( uint32_t hEntity, const float *pXYZ ) = 0;
	virtual void EntityGetAngularVelocity( uint32_t hEntity, float *pOutPYR ) = 0;
	virtual void EntitySetAngularVelocity( uint32_t hEntity, const float *pPYR ) = 0;
	virtual void EntityGetEyePosition( uint32_t hEntity, float *pOutXYZ ) = 0;
	virtual void EntityGetEyeAngles( uint32_t hEntity, float *pOutPYR ) = 0;
	virtual float EntityGetGravity( uint32_t hEntity ) = 0;
	virtual void EntitySetGravity( uint32_t hEntity, float flGravity ) = 0;
	virtual void EntitySetBounce( uint32_t hEntity, int32_t nBounces ) = 0;
};

abstract_class IShimHierarchy
{
public:
	virtual int32_t EntitySetParent( uint32_t hChild, uint32_t hParent, int32_t nParentAttachment ) = 0;
	virtual uint32_t EntityGetParent( uint32_t hChild ) = 0;
	virtual int32_t EntityGetParentAttachment( uint32_t hChild ) = 0;
	virtual void EntitySetLocalTransform( uint32_t hChild, const float *pOrigin, const float *pAngles ) = 0;
	virtual void EntityGetLocalTransform( uint32_t hChild, float *pOutOrigin, float *pOutAngles ) = 0;
	virtual int32_t EntityParentProbe( uint32_t hEntity ) = 0;
	virtual int32_t EntityGetAttachmentFrame( uint32_t hEntity, int32_t nAttachment, float *pOutOriginXYZ,
											  float *pOutAnglesPYR ) = 0;
};

abstract_class IShimPhysics
{
public:
	virtual void EntityDestroyPhysics( uint32_t hEntity ) = 0;
	virtual void *EntityPhysicsObject( uint32_t hEntity ) = 0;
	virtual void *PhysicsEnvironmentPointer() = 0;

	virtual void *PhysicsWorldObject() = 0;
	virtual int32_t PhysicsUpdateOwners( void **ppObjects, float *pVelocities, int32_t nCount,
										 uint32_t *pOutOwners ) = 0;
	virtual int32_t EntitySwapPhysicsObject( uint32_t hEntity, void *pObject, void **ppPrevious ) = 0;
};

abstract_class IShimDamage
{
public:
	virtual void ApplyDamage( uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType ) = 0;
	virtual void EntityTakeDamage( uint32_t hEntity, uint32_t hAttacker, float flDamage, int32_t nDamageType,
								   const float *pDirXYZ, const float *pForceOriginXYZ, int32_t bPhysicsOnly ) = 0;
	virtual void ApplyDamageForce( uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType,
								   const float *pForceXYZ, const float *pPositionXYZ ) = 0;
};

abstract_class IShimGameRules
{
public:
	virtual void RegisterGameRulesPolicy( const SourceSharpGameRulesPolicy *pPolicy ) = 0;
	virtual void RegisterDamageRules( const SourceSharpDamageRules *pRules ) = 0;
};

abstract_class IShimPlayers
{
public:
	virtual uint32_t GetPlayerHandle( int32_t nPlayerIndex ) = 0;
	virtual int32_t PlayerIsAlive( uint32_t hPlayer ) = 0;
	virtual void PlayerPlaceAtSpawn( uint32_t hPlayer, const float *pOriginXYZ, const float *pAnglesPYR ) = 0;
	virtual int32_t PlayerGetObserverMode( uint32_t hPlayer ) = 0;
	virtual void PlayerSetObserverMode( uint32_t hPlayer, int32_t nMode ) = 0;
	virtual void PlayerSnapEyeAngles( uint32_t hPlayer, const float *pPitchYawRoll ) = 0;
	virtual int32_t ClientsStandalonePlayerEarPosition( int32_t iEdict, float *pOutXYZ ) = 0;
	// Stores the team; validating and choosing it is managed's.
};

abstract_class IShimDebugOverlay
{
public:
	virtual int32_t DrawEntityOverlay( uint32_t hEntity, int32_t nLine, const char *pText, float flDuration, int32_t r,
									   int32_t g, int32_t b, int32_t a ) = 0;
	virtual void AddLineOverlay( const float *pStartXYZ, const float *pEndXYZ, int32_t r, int32_t g, int32_t b,
								 int32_t bNoDepthTest, float flDuration ) = 0;
	virtual void OverlayCounts( uint32_t *pOutDrawn, uint32_t *pOutClipped, uint32_t *pOutNoViewer,
								uint32_t *pOutNoOverlay, int32_t *pOutViewerSet ) = 0;
};

abstract_class IShimDiagnostics
{
public:
	virtual int32_t PlayerShadowCounts( int32_t *pOut, int32_t nMax ) = 0;
	virtual int32_t ExitWatchArm( const char *pRecordPath ) = 0;
	virtual int32_t ExitWatchRead( int32_t *pOut, int32_t nMax ) = 0;
	virtual int32_t ExitWatchDisarm() = 0;
};

abstract_class IShimUserMessages
{
public:
	virtual int32_t UserMessageSend( int32_t nMsgId, const unsigned char *pData, int32_t nBytes, int32_t nPlayerIndex,
									 int32_t bReliable ) = 0;
};

abstract_class IShimEventsServer : public IShimEvents
{
public:
	virtual void EventSetInt( void *hEvent, const char *pKey, int32_t nValue ) = 0;
	virtual void EventSetBool( void *hEvent, const char *pKey, int32_t bValue ) = 0;
	virtual void EventSetString( void *hEvent, const char *pKey, const char *pValue ) = 0;
};

abstract_class IShimNetworkServer : public IShimNetwork
{
public:
	virtual void *EntityGetDataBlob( uint32_t hEntity, int32_t *pOutSize ) = 0;
	virtual void *EntityBindBlob( uint32_t hEntity, int32_t *pOutSize, SourceSharpEntitySlot_t **pOutSlot,
								  uint32_t *pOutSerial ) = 0;
	virtual void EntityMarkBlockChanged( uint32_t hEntity, int32_t nBlockOffset ) = 0;
	virtual int32_t EntityPayloadAgreement( uint32_t hEntity, int32_t *pOutWords, int32_t *pOutFirstBadWord ) = 0;
	virtual void *EntityGetEngineBlock( uint32_t hEntity, int32_t *pOutSize ) = 0;
	virtual void EntitySetTransmitAlways( uint32_t hEntity, int32_t bAlways ) = 0;
};

abstract_class IShimModelsServer : public IShimModels
{
public:
	virtual void EntitySetModel( uint32_t hEntity, const char *pModel ) = 0;
};

abstract_class IShimMovementServer : public IShimMovement
{
public:
	virtual int32_t PlayerShadowFacts( uint32_t hPlayer, SourceSharpPlayerShadowFacts *pOut ) = 0;
	virtual int32_t PlayerShadowAdopt( uint32_t hPlayer, void *pStand, void *pCrouch, void *pController ) = 0;
	virtual void PlayerShadowRelease( uint32_t hPlayer ) = 0;
	virtual void PlayerShadowCommit( uint32_t hPlayer, int32_t nState, int32_t bAfterCommand ) = 0;
	virtual int32_t PlayerSetMoveType( uint32_t hPlayer, int32_t nMoveType ) = 0;
	virtual int32_t PlayerCommandBegin( uint32_t hPlayer, SourceSharpPlayerCommandFacts *pFacts ) = 0;
	virtual void PlayerCommandView( uint32_t hPlayer, const float *pAnglePYR, int32_t nFixAngle ) = 0;
	virtual void PlayerCommandFinish( uint32_t hPlayer, const SourceSharpPlayerCommandResult *pResult ) = 0;
	virtual void MoveTraceServer( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ,
								  const float *pMaxsXYZ, int32_t nMask, int32_t nCollisionGroup,
								  SourceSharpMoveTraceResult *pOut, void *pFilter, uint32_t *pOutHandles ) = 0;
	virtual int32_t PlayerFallDamage( uint32_t hPlayer, float flDamage ) = 0;
	virtual void PlayerSyncEngineSide( uint32_t hPlayer ) = 0;
};

abstract_class IShimWorldServer
{
public:
	virtual int32_t EntityModelSurfaceProp( uint32_t hEntity ) = 0;
	virtual int32_t InvisibleRangeReach() = 0;
	virtual void SetSkyboxParams( const SourceSharpSkyboxParams *pParams ) = 0;
};

abstract_class IShimEntitiesServer : public IShimEntities
{
public:
	virtual int32_t EntityIsPlayer( uint32_t hEntity ) = 0;
	virtual void EntityGetClassName( uint32_t hEntity, char *pOut, int32_t nOutSize ) = 0;
	virtual void EntityGetName( uint32_t hEntity, char *pOut, int32_t nOutSize ) = 0;
	virtual int32_t EntityGetState( uint32_t hEntity, int32_t *pOutMoveType ) = 0;
	virtual uint32_t FindEntityByClassname( uint32_t hStartAfter, const char *pClassName ) = 0;
	virtual int32_t EntitiesInSphere( const float *pCenterXYZ, float flRadius, uint32_t *pOutHandles,
									  int32_t nMax ) = 0;
	virtual int32_t EntitySetKeyValue( uint32_t hEntity, const char *pKey, const char *pValue ) = 0;
	virtual void EntityGetSpawnOrigin( uint32_t hEntity, float *pOutXYZ ) = 0;
	virtual void EntityGetSpawnAngles( uint32_t hEntity, float *pOutPYR ) = 0;
	virtual int32_t EntityGetSpawnFlags( uint32_t hEntity ) = 0;
	virtual void EntitySetSpawnFlags( uint32_t hEntity, int32_t nSpawnFlags ) = 0;
	virtual int32_t EntityGetHealth( uint32_t hEntity ) = 0;
	virtual void EntitySetHealth( uint32_t hEntity, int32_t nHealth ) = 0;
	virtual void EntitySetVisible( uint32_t hEntity, int32_t bVisible ) = 0;
	virtual void EntitySetOwner( uint32_t hEntity, uint32_t hOwner ) = 0;
	virtual int32_t EntitiesStandalonePlayerSkyboxArea( int32_t iEdict, int32_t *pOutArea ) = 0;
	virtual void EntitySetClassName( uint32_t hEntity, const char *pName ) = 0;
};

abstract_class IShimCollisionServer
{
public:
	virtual void EntityGetBounds( uint32_t hEntity, float *pOutMinsXYZ, float *pOutMaxsXYZ ) = 0;
	virtual void EntityGetBoundsSize( uint32_t hEntity, float *pOutXYZ ) = 0;
	virtual void EntitySetBounds( uint32_t hEntity, const float *pMinsXYZ, const float *pMaxsXYZ ) = 0;
	virtual void EntitySetSolidity( uint32_t hEntity, int32_t nSolidType, int32_t nSolidFlags ) = 0;
	virtual int32_t EntityGetCollisionGroup( uint32_t hEntity ) = 0;
	virtual void EntitySetCollisionGroup( uint32_t hEntity, int32_t nGroup ) = 0;

	virtual int32_t EntityOverlapReport( uint32_t *pOutPairs, int32_t nMaxPairs ) = 0;

	virtual void EntitySetTriggerBloat( uint32_t hEntity, int32_t nBloat, int32_t bUniform ) = 0;
};

abstract_class IShimEffectsServer
{
public:
	// Stores one temp entity table planned in C#; the block is length-prefixed
	// (SourceSharpTeTableHeader_t). Answers the handle, or -1.
	virtual int32_t TempEntBuild( const void *pBlock, int32_t nBlockBytes, SourceSharpTeBuiltTable_t *pOut ) = 0;
};
