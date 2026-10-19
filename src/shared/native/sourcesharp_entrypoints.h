//========= SourceSharp — the shim's entry points, GENERATED =================//
//
// DO NOT EDIT. Written by tools/entrypoint-decls from
//
//     src/sourcesharp/host/sourcesharp.h                   the signatures
//     src/sourcesharp/game/shared/sourcesharp_gamesystem.cpp   which, and which side
//
// One prototype per engine-API slot whose body is in some other shim file.
// These were 483 hand-written lines at the top of sourcesharp_gamesystem.cpp,
// each a second spelling of an argument list SourceSharpEngineApi already
// declares -- so `int32_t` where the field said `int`, or a dropped `const`,
// was a difference no compiler and no reviewer would see. They are derived now.
//
// WHERE THE PROSE WENT. Each block carried a paragraph naming the file the body
// lives in and why it is there. That is in docs/engine-boundary.md, keyed by
// slot name, and this file carries the one fact a generator can keep honest:
// the signature, and the side.
//
// TO REGENERATE:  tools/entrypoint-decls --write
// TO CHECK:       tools/entrypoint-decls        (exit 1 on drift)
//
//=============================================================================//

#ifndef SOURCESHARP_ENTRYPOINTS_H
#define SOURCESHARP_ENTRYPOINTS_H
#ifdef _WIN32
#pragma once
#endif

// sourcesharp.h is what these signatures are derived FROM, so it has to be
// included before them: every argument type below is one of its typedefs or a
// plain C type.
#include "sourcesharp.h"

//---------------------------------------------------------------------------
// BOTH SIDES -- 14 slots.
//
// A body on the server and a body on the client, or one shared body compiled
// into each.
//---------------------------------------------------------------------------
int32_t SourceSharp_EventGetBool( void *hEvent, const char *pKey );
float SourceSharp_EventGetFloat( void *hEvent, const char *pKey );
int32_t SourceSharp_EventGetInt( void *hEvent, const char *pKey );
const char *SourceSharp_EventGetString( void *hEvent, const char *pKey );
void SourceSharp_ListenForGameEvent( const char *pEventName );
int32_t SourceSharp_MoveEffect( int32_t nKind, const float *pXYZ, float flValue,
                                int32_t nValue );
void SourceSharp_MoveTrace( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ,
                            const float *pMaxsXYZ, int32_t nMask, int32_t nCollisionGroup,
                            SourceSharpMoveTraceResult *pOut );
int32_t SourceSharp_NetHierarchyDeclare( int32_t nBlobBytes );
int32_t SourceSharp_NetPublishType( const char *pClassName, int32_t nShell,
                                    uint32_t nSchemaHash, int32_t nLayoutBytes,
                                    uint32_t *pOutTypeId );
int32_t SourceSharp_NetTierInfo( int32_t nShell, int32_t iTier, const char **ppOutTable,
                                 int32_t *pOutBytes, int32_t *pOutWords, int32_t *pOutProps,
                                 int32_t *pOutLive );
void SourceSharp_RegisterCollisionRules( const SourceSharpCollisionRules *pRules );
void SourceSharp_RegisterMovementParams( const SourceSharpMovementParams *pParams );
void SourceSharp_StandaloneSetModelScale( uint32_t hEntity, float flScale );
int32_t SourceSharp_WorldNetDeclare( int32_t nBlobBytes );

//---------------------------------------------------------------------------
// SERVER ONLY -- 132 slots.
//
// Served by the server's shim areas (docs/engineapi-areas.tsv), so the
// client has no body to declare.
//---------------------------------------------------------------------------
#ifndef CLIENT_DLL
void SourceSharp_AddLineOverlay( const float *pStartXYZ, const float *pEndXYZ, int32_t r,
                                 int32_t g, int32_t b, int32_t bNoDepthTest, float flDuration );
void SourceSharp_AngleVectorsFromForward( const float *pForwardXYZ, float *pOutRightXYZ,
                                          float *pOutUpXYZ );
void SourceSharp_ApplyDamage( uint32_t hVictim, uint32_t hAttacker, float flDamage,
                              int32_t nDamageType );
void SourceSharp_ApplyDamageForce( uint32_t hVictim, uint32_t hAttacker, float flDamage,
                                   int32_t nDamageType, const float *pForceXYZ,
                                   const float *pPositionXYZ );
void SourceSharp_BloodEffect( const float *pOriginXYZ, const float *pDirXYZ, int32_t nColor,
                              float flAmount, int32_t bStream, int32_t nSprayFlags );
void SourceSharp_BubbleEffect( uint32_t hEntity, int32_t nCount, float flSpeed );
int32_t SourceSharp_ClientsStandalonePlayerEarPosition( int32_t iEdict, float *pOutXYZ );
uint32_t SourceSharp_CreateEntity( const char *pClassName );
void SourceSharp_DestroyShell( const uint32_t *pHandles, int32_t nCount );
int32_t SourceSharp_DrawEntityOverlay( uint32_t hEntity, int32_t nLine, const char *pText,
                                       float flDuration, int32_t r, int32_t g, int32_t b,
                                       int32_t a );
void SourceSharp_EndRound( void );
int32_t SourceSharp_EntitiesInSphere( const float *pCenterXYZ, float flRadius,
                                      uint32_t *pOutHandles, int32_t nMax );
int32_t SourceSharp_EntitiesInvalidateMdlCacheAll( void );
int32_t SourceSharp_EntitiesStandalonePlayerSkyboxArea( int32_t iEdict, int32_t *pOutArea );
void SourceSharp_EntityAddBaseVelocity( uint32_t hEntity, const float *pVelocityXYZ );
void *SourceSharp_EntityBindBlob( uint32_t hEntity, int32_t *pOutSize,
                                  SourceSharpEntitySlot_t **pOutSlot, uint32_t *pOutSerial );
void SourceSharp_EntityDestroyPhysics( uint32_t hEntity );
int32_t SourceSharp_EntityDispatch( uint32_t hEntity, int32_t nOp,
                                    const SourceSharpOpArgs *pArgs );
int32_t SourceSharp_EntityFactoryCount( void );
const char *SourceSharp_EntityFactoryNameAt( int32_t index );
int32_t SourceSharp_EntityFactoryRefusalCount( void );
const char *SourceSharp_EntityFactoryRefusalNameAt( int32_t index );
int32_t SourceSharp_EntityFactoryRefusalTimesAt( int32_t index );
void SourceSharp_EntityGetAngles( uint32_t hEntity, float *pOutPYR );
void SourceSharp_EntityGetAngularVelocity( uint32_t hEntity, float *pOutPYR );
int32_t SourceSharp_EntityGetAttachmentFrame( uint32_t hEntity, int32_t nAttachment,
                                              float *pOutOriginXYZ, float *pOutAnglesPYR );
void SourceSharp_EntityGetBounds( uint32_t hEntity, float *pOutMinsXYZ, float *pOutMaxsXYZ );
void SourceSharp_EntityGetBoundsSize( uint32_t hEntity, float *pOutXYZ );
void SourceSharp_EntityGetClassName( uint32_t hEntity, char *pOut, int32_t nOutSize );
int32_t SourceSharp_EntityGetCollisionGroup( uint32_t hEntity );
void *SourceSharp_EntityGetDataBlob( uint32_t hEntity, int32_t *pOutSize );
void *SourceSharp_EntityGetEngineBlock( uint32_t hEntity, int32_t *pOutSize );
void SourceSharp_EntityGetEyeAngles( uint32_t hEntity, float *pOutPYR );
void SourceSharp_EntityGetEyePosition( uint32_t hEntity, float *pOutXYZ );
float SourceSharp_EntityGetGravity( uint32_t hEntity );
int32_t SourceSharp_EntityGetHealth( uint32_t hEntity );
int32_t SourceSharp_EntityGetIndex( uint32_t hEntity );
void SourceSharp_EntityGetLocalTransform( uint32_t hChild, float *pOutOrigin,
                                          float *pOutAngles );
void SourceSharp_EntityGetModelName( uint32_t hEntity, char *pOut, int32_t nOutSize );
void SourceSharp_EntityGetName( uint32_t hEntity, char *pOut, int32_t nOutSize );
void SourceSharp_EntityGetOrigin( uint32_t hEntity, float *pOutXYZ );
uint32_t SourceSharp_EntityGetParent( uint32_t hChild );
int32_t SourceSharp_EntityGetParentAttachment( uint32_t hChild );
int32_t SourceSharp_EntityGetSolidFlags( uint32_t hEntity );
int32_t SourceSharp_EntityGetSolidType( uint32_t hEntity );
void SourceSharp_EntityGetSpawnAngles( uint32_t hEntity, float *pOutPYR );
int32_t SourceSharp_EntityGetSpawnFlags( uint32_t hEntity );
void SourceSharp_EntityGetSpawnOrigin( uint32_t hEntity, float *pOutXYZ );
int32_t SourceSharp_EntityGetState( uint32_t hEntity, int32_t *pOutMoveType );
void SourceSharp_EntityGetVelocity( uint32_t hEntity, float *pOutXYZ );
void SourceSharp_EntityGetWorldSpaceCenter( uint32_t hEntity, float *pOutXYZ );
int32_t SourceSharp_EntityIsPlayer( uint32_t hEntity );
int32_t SourceSharp_EntityIsValid( uint32_t hEntity );
void SourceSharp_EntityMarkBlockChanged( uint32_t hEntity, int32_t nBlockOffset );
void SourceSharp_EntityMarkStateChanged( uint32_t hEntity );
int32_t SourceSharp_EntityModelSurfaceProp( uint32_t hEntity );
int32_t SourceSharp_EntityOverlapReport( uint32_t *pOutPairs, int32_t nMaxPairs );
int32_t SourceSharp_EntityParentProbe( uint32_t hEntity );
int32_t SourceSharp_EntityPayloadAgreement( uint32_t hEntity, int32_t *pOutWords,
                                            int32_t *pOutFirstBadWord );
void *SourceSharp_EntityPhysicsObject( uint32_t hEntity );
void SourceSharp_EntityRemove( uint32_t hEntity );
void SourceSharp_EntitySetAngles( uint32_t hEntity, const float *pPYR );
void SourceSharp_EntitySetAngularVelocity( uint32_t hEntity, const float *pPYR );
void SourceSharp_EntitySetBounce( uint32_t hEntity, int32_t nBounces );
void SourceSharp_EntitySetBounds( uint32_t hEntity, const float *pMinsXYZ,
                                  const float *pMaxsXYZ );
void SourceSharp_EntitySetCollisionGroup( uint32_t hEntity, int32_t nGroup );
void SourceSharp_EntitySetGravity( uint32_t hEntity, float flGravity );
void SourceSharp_EntitySetHealth( uint32_t hEntity, int32_t nHealth );
int32_t SourceSharp_EntitySetKeyValue( uint32_t hEntity, const char *pKey, const char *pValue );
void SourceSharp_EntitySetLocalTransform( uint32_t hChild, const float *pOrigin,
                                          const float *pAngles );
void SourceSharp_EntitySetModel( uint32_t hEntity, const char *pModel );
void SourceSharp_EntitySetOrigin( uint32_t hEntity, const float *pXYZ );
void SourceSharp_EntitySetOwner( uint32_t hEntity, uint32_t hOwner );
int32_t SourceSharp_EntitySetParent( uint32_t hChild, uint32_t hParent,
                                     int32_t nParentAttachment );
void SourceSharp_EntitySetSolidity( uint32_t hEntity, int32_t nSolidType, int32_t nSolidFlags );
void SourceSharp_EntitySetSpawnFlags( uint32_t hEntity, int32_t nSpawnFlags );
void SourceSharp_EntitySetTransmitAlways( uint32_t hEntity, int32_t bAlways );
void SourceSharp_EntitySetVelocity( uint32_t hEntity, const float *pXYZ );
void SourceSharp_EntitySetVisible( uint32_t hEntity, int32_t bVisible );
void SourceSharp_EntitySpawn( uint32_t hEntity );
int32_t SourceSharp_EntitySwapPhysicsObject( uint32_t hEntity, void *pObject,
                                             void **ppPrevious );
void SourceSharp_EntityTakeDamage( uint32_t hEntity, uint32_t hAttacker, float flDamage,
                                   int32_t nDamageType, const float *pDirXYZ,
                                   const float *pForceOriginXYZ, int32_t bPhysicsOnly );
void SourceSharp_EntityTeleport( uint32_t hEntity, const float *pOrigin, const float *pAngles,
                                 int32_t bZeroVelocity );
int32_t SourceSharp_EntityTraceHull( uint32_t hEntity, const float *pStartXYZ,
                                     const float *pEndXYZ, const float *pMinsXYZ,
                                     const float *pMaxsXYZ, int32_t nMask,
                                     int32_t nCollisionGroup, float *pOutEndXYZ );
void SourceSharp_EventSetBool( void *hEvent, const char *pKey, int32_t bValue );
void SourceSharp_EventSetFloat( void *hEvent, const char *pKey, float flValue );
void SourceSharp_EventSetInt( void *hEvent, const char *pKey, int32_t nValue );
void SourceSharp_EventSetString( void *hEvent, const char *pKey, const char *pValue );
int32_t SourceSharp_ExitWatchArm( const char *pRecordPath );
int32_t SourceSharp_ExitWatchDisarm( void );
int32_t SourceSharp_ExitWatchRead( int32_t *pOut, int32_t nMax );
uint32_t SourceSharp_FindEntityByClassname( uint32_t hStartAfter, const char *pClassName );
uint32_t SourceSharp_GetCommandClientHandle( void );
uint32_t SourceSharp_GetPlayerHandle( int32_t nPlayerIndex );
int32_t SourceSharp_GetPlayerTeam( uint32_t hPlayer );
int32_t SourceSharp_InvisibleRangeReach( void );
uint32_t SourceSharp_MapSpawnCreate( const char *pClassName, const char *pPackedKeys,
                                     int32_t nBytes );
void SourceSharp_OverlayCounts( uint32_t *pOutDrawn, uint32_t *pOutClipped,
                                uint32_t *pOutNoViewer, uint32_t *pOutNoOverlay,
                                int32_t *pOutViewerSet );
void SourceSharp_OverlayResetCounts( void );
int32_t SourceSharp_PaintDecal( int32_t nTexture, const float *pOriginXYZ,
                                const float *pAnglesXYZ, float flProjectDistance,
                                int32_t bPermanent, int32_t bLowPriority );
void *SourceSharp_PhysicsEnvironmentPointer( void );
int32_t SourceSharp_PhysicsUpdateOwners( void **ppObjects, float *pVelocities, int32_t nCount,
                                         uint32_t *pOutOwners );
void *SourceSharp_PhysicsWorldObject( void );
int32_t SourceSharp_PlayerGetObserverMode( uint32_t hPlayer );
int32_t SourceSharp_PlayerIsAlive( uint32_t hPlayer );
void SourceSharp_PlayerPlaceAtSpawn( uint32_t hPlayer, const float *pOriginXYZ,
                                     const float *pAnglesPYR );
int32_t SourceSharp_PlayerRunMove( uint32_t hPlayer, int32_t nIndex, int32_t nStage );
int32_t SourceSharp_PlayerSetMoveType( uint32_t hPlayer, int32_t nMoveType );
void SourceSharp_PlayerSetObserverMode( uint32_t hPlayer, int32_t nMode );
void SourceSharp_PlayerSetSlowMovement( uint32_t hPlayer, int32_t bSlow );
void SourceSharp_PlayerSetTeam( uint32_t hPlayer, int32_t nTeam );
int32_t SourceSharp_PlayerShadowAdopt( uint32_t hPlayer, void *pStand, void *pCrouch,
                                       void *pController );
void SourceSharp_PlayerShadowCommit( uint32_t hPlayer, int32_t nState, int32_t bAfterCommand );
int32_t SourceSharp_PlayerShadowCounts( int32_t *pOut, int32_t nMax );
int32_t SourceSharp_PlayerShadowFacts( uint32_t hPlayer, SourceSharpPlayerShadowFacts *pOut );
void SourceSharp_PlayerShadowRelease( uint32_t hPlayer );
void SourceSharp_PlayerSnapEyeAngles( uint32_t hPlayer, const float *pPitchYawRoll );
int32_t SourceSharp_PrecacheModel( const char *pszModel );
void SourceSharp_RegisterClassOps( const char *pClassName, uint32_t nOpMask );
void SourceSharp_RegisterDamageRules( const SourceSharpDamageRules *pRules );
void SourceSharp_RegisterGameRulesPolicy( const SourceSharpGameRulesPolicy *pPolicy );
void SourceSharp_RegisterScriptedEntity( const char *pClassName, const char **ppOutputNames,
                                         int32_t nOutputCount, int32_t nKind );
void SourceSharp_SendClientCommand( uint32_t hPlayer, const char *pCommand );
void SourceSharp_SetSkyboxParams( const SourceSharpSkyboxParams *pParams );
int32_t SourceSharp_TempEntBuild( const void *pBlock, int32_t nBlockBytes,
                                  SourceSharpTeBuiltTable_t *pOut );
int32_t SourceSharp_TemplateSpawnKeyed( const char *pClassNames, const char *pPackedKeys,
                                        const int32_t *pPackBytes, const float *pOriginsXYZ,
                                        const float *pAnglesPYR, int32_t nCount,
                                        uint32_t *pOutHandles, int32_t nMax );
int32_t SourceSharp_TraceHull( const float *pStartXYZ, const float *pEndXYZ,
                               const float *pMinsXYZ, const float *pMaxsXYZ, int32_t nMask,
                               uint32_t hIgnore, uint32_t *pOutHit, int32_t *pOutStartSolid );
int32_t SourceSharp_TraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask,
                               uint32_t hIgnore, float *pOutFraction, float *pOutEndXYZ,
                               float *pOutNormalXYZ, uint32_t *pOutHit );
int32_t SourceSharp_TraceLineSurface( const float *pStartXYZ, const float *pEndXYZ,
                                      int32_t nMask, uint32_t hIgnore, float *pOutFraction,
                                      float *pOutEndXYZ, float *pOutNormalXYZ,
                                      uint32_t *pOutHit, int32_t *pOutSurfaceProps,
                                      int32_t *pOutSurfaceFlags, int32_t *pOutHitgroup );
int32_t SourceSharp_UserMessageSend( int32_t nMsgId, const unsigned char *pData, int32_t nBytes,
                                     int32_t nPlayerIndex, int32_t bReliable );
void SourceSharp_ViewPunch( const float *pOrigin, const float *pPunchPYR, float flRadius,
                            int32_t bInAir );
int32_t SourceSharp_WaterSurfaceBelow( const float *pStartXYZ, float *pOutXYZ,
                                       int32_t *pOutSlime );
#endif

//---------------------------------------------------------------------------
// CLIENT ONLY -- 248 slots.
//
// The fill wires these inside `#ifdef CLIENT_DLL`, so the server has no body
// to declare.
//---------------------------------------------------------------------------
#ifdef CLIENT_DLL
void SourceSharp_ActivateOccluder( int32_t nOccluderIndex, int32_t bActive );
int32_t SourceSharp_BeamBindSprite( int32_t nModelIndex, int32_t nFrame, int32_t nRenderMode,
                                    int32_t bWireframe );
int32_t SourceSharp_BeamBoxVisibility( const float *pMinsXYZ, const float *pMaxsXYZ );
void SourceSharp_BeamDrawMesh( int32_t nPrimitive, int32_t nPrimitives, int32_t nColorKind,
                               const float *pVerts, int32_t nVerts );
void SourceSharp_BeamDrawStrip( const float *pSegs, int32_t nSegs );
uint32_t SourceSharp_BeamEntityAt( int32_t nEntry );
int32_t SourceSharp_BeamEntityFacts( uint32_t hEntity, int32_t nAttachment,
                                     int32_t bTryAttachment, float *pOut9 );
int32_t SourceSharp_BeamEntityHasModel( uint32_t hEntity );
int32_t SourceSharp_BeamGlow( int32_t nModelIndex, int32_t bBind );
float SourceSharp_BeamHaloVisibility( const float *pOriginXYZ, float flProxySize );
int32_t SourceSharp_BeamModelIndex( const char *pszName );
int32_t SourceSharp_BeamModelInfo( int32_t nModelIndex, int32_t *pOutFrameCount );
int32_t SourceSharp_BeamRenderableAdd( const float *pOriginMinsMaxs9 );
void SourceSharp_BeamRenderableChanged( int32_t nRenderable, const float *pOriginMinsMaxs9 );
void SourceSharp_BeamRenderableRemove( int32_t nRenderable );
int32_t SourceSharp_BeamScreenPosition( const float *pPointXYZ, float *pOutScreenXYZ );
void SourceSharp_BeamSetHdrColorScale( int32_t nModelIndex, int32_t nRenderMode,
                                       float flScale );
void SourceSharp_BeamView( int32_t bCurrentView, float *pOut42 );
int32_t SourceSharp_ClientBeginMoveCommand( uint32_t hPlayer, int32_t nCommandNumber,
                                            SourceSharpMoveState *pOutConstants );
int32_t SourceSharp_ClientColorCorrection( int32_t nOp, int32_t nHandle,
                                           const char *pLookupFile, float flWeight );
void SourceSharp_ClientComputeLighting( const float *pPosXYZ, float *pOutColorRGB,
                                        float *pOutBoxRGB18 );
void SourceSharp_ClientDecalBrushModel( int32_t nEntity, const float *pDecalCenterXYZ,
                                        int32_t nDecalIndex );
int32_t SourceSharp_ClientDecalIndexFromName( const char *pName );
int32_t SourceSharp_ClientDecalNameFromIndex( int32_t nDecalIndex, char *pOut,
                                              int32_t nOutSize );
void SourceSharp_ClientDecalShoot( int32_t nDecalIndex, int32_t nEntity,
                                   const float *pPositionXYZ, const float *pSAxisXYZ,
                                   int32_t nFlags );
void SourceSharp_ClientDecalStaticProp( const float *pStartXYZ, const float *pEndXYZ,
                                        int32_t nStaticPropIndex, int32_t nDecalIndex );
void SourceSharp_ClientDecalStudioModel( int32_t nEntity, const float *pRayStartXYZ,
                                         const float *pRayEndXYZ, int32_t nDecalIndex );
void SourceSharp_ClientEndMoveCommand( int32_t bRan, int32_t *pOutGroundIndex );
void SourceSharp_ClientEnergySplash( const float *pPosXYZ, const float *pDirXYZ,
                                     int32_t bExplosive );
int32_t SourceSharp_ClientEntitiesInvalidateMdlCacheAll( void );
int32_t SourceSharp_ClientEntitiesUpdateVisibilityAll( void );
void SourceSharp_ClientEntityGetAngles( uint32_t hEntity, float *pOutPYR );
int32_t SourceSharp_ClientEntityGetIndex( uint32_t hEntity );
void SourceSharp_ClientEntityGetModelName( uint32_t hEntity, char *pOut, int32_t nOutSize );
void SourceSharp_ClientEntityGetOrigin( uint32_t hEntity, float *pOutXYZ );
int32_t SourceSharp_ClientEntityIsValid( uint32_t hEntity );
int32_t SourceSharp_ClientEntityModelBounds( int32_t nEntityIndex, float *pOutMinsXYZ,
                                             float *pOutMaxsXYZ, float *pOutAnglesPYR );
int32_t SourceSharp_ClientEntityModelType( int32_t nEntity );
int32_t SourceSharp_ClientEntityOrigin( int32_t nEntityIndex, float *pOutXYZ );
int32_t SourceSharp_ClientEntityRenderPose( int32_t nEntityIndex, float *pOutOriginXYZ,
                                            float *pOutAngles );
void SourceSharp_ClientExplosionFireball( const float *pOriginXYZ );
int32_t SourceSharp_ClientGetUserCmd( int32_t nCommandNumber, SourceSharpUserCmd *pOut );
void **SourceSharp_ClientInterpBlocks( int32_t *pOutCount, float *pOutInterpAmount );
int32_t SourceSharp_ClientIsLowViolence( void );
void SourceSharp_ClientMetalSparks( const float *pPositionXYZ, const float *pDirectionXYZ );
void SourceSharp_ClientMuzzleFlash( const float *pOriginXYZ, const float *pAnglesPYR,
                                    float flScale, int32_t nType );
int32_t SourceSharp_ClientNetChannel( float *pOut, int32_t nOut );
int32_t SourceSharp_ClientNetFlow( int32_t nFlow, float *pOut, int32_t nOut );
int32_t SourceSharp_ClientNetPacket( int32_t nFlow, int32_t nFrame, float *pOut, int32_t nOut );
int32_t SourceSharp_ClientNotShipped( int32_t iIndex, char *pOutName, int32_t nNameBytes,
                                      char *pOutReason, int32_t nReasonBytes,
                                      int32_t *pOutCount, int32_t *pOutTotal );
void SourceSharp_ClientPlayerDecalShoot( const float *pPositionXYZ, int32_t nPlayer,
                                         int32_t nEntity );
void SourceSharp_ClientPlayerPredictionDrive( uint32_t hEntity,
                                              const SourceSharpClientPlayerView *pView );
void **SourceSharp_ClientPolicyBlocks( int32_t *pOutCount );
int32_t SourceSharp_ClientRecvProp( int32_t iIndex, char *pOutClass, int32_t nClassBytes,
                                    char *pOutTable, int32_t nTableBytes, char *pOutProp,
                                    int32_t nPropBytes, int32_t *pOutType, int32_t *pOutFlags,
                                    int32_t *pOutOffset, int32_t *pOutElements );
int32_t SourceSharp_ClientRefusal( int32_t iIndex, char *pOutMethod, int32_t nMethodBytes,
                                   int32_t *pOutCount, int32_t *pOutTotal );
void SourceSharp_ClientRicochet( const float *pPositionXYZ, const float *pDirectionXYZ );
int32_t SourceSharp_ClientSchemaRefusal( int32_t iIndex, uint32_t *pOutTypeId,
                                         uint32_t *pOutExpected, uint32_t *pOutSeen,
                                         int32_t *pOutCount, char *pOutClass,
                                         int32_t nClassBytes, int32_t *pOutRefusedUpdates );
void SourceSharp_ClientSetWorldRenderState( int32_t nWhich, const float *pValues,
                                            int32_t nCount );
void SourceSharp_ClientSmoke( const float *pOriginXYZ, int32_t nModelIndex, float flScale,
                              float flFrameRate );
int32_t SourceSharp_ClientSoundPatchCreate( uint32_t hEntity, int32_t nChannel,
                                            const char *pSoundName, int32_t nSoundLevel );
void SourceSharp_ClientSoundPatchEmit( int32_t hPatch, float flVolume01, float flPitch,
                                       int32_t nFlags, float flStartTime,
                                       int32_t bEmitCaption );
void SourceSharp_ClientSoundPatchFree( int32_t hPatch );
void SourceSharp_ClientSoundPatchState( int32_t hPatch, int32_t *pnEntIndex,
                                        int32_t *pbFilterActive );
void SourceSharp_ClientSoundPatchStop( int32_t hPatch );
void SourceSharp_ClientSparks( const float *pPositionXYZ, int32_t nMagnitude,
                               int32_t nTrailLength, const float *pDirectionXYZ );
int32_t SourceSharp_ClientTraceHull( const float *pStartXYZ, const float *pEndXYZ,
                                     const float *pMinsXYZ, const float *pMaxsXYZ,
                                     int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex,
                                     float *pOutFraction, float *pOutEndXYZ );
float SourceSharp_ClientTraceHullWorld( const float *pStartXYZ, const float *pEndXYZ,
                                        const float *pMinsXYZ, const float *pMaxsXYZ,
                                        int32_t nMask, int32_t *pOutSolidFlags,
                                        float *pOutPlaneXYZD );
int32_t SourceSharp_ClientTraceLine( const float *pStartXYZ, const float *pEndXYZ,
                                     int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex,
                                     float *pOutFraction, float *pOutEndXYZ,
                                     float *pOutNormalXYZ, int32_t *pOutHitbox,
                                     int32_t *pOutEntityIndex, int32_t *pOutSurfaceProps,
                                     int32_t *pOutSurfaceFlags );
void SourceSharp_ClientViewState( int32_t bCurrentView, float *pOutForwardXYZ,
                                  float *pOutOriginXYZ, float *pOutMainOriginXYZ,
                                  float *pOutCameraXYZ, int32_t *pOutScreenWidth,
                                  int32_t *pOutDxLevel, int32_t *pOutAAEnabled );
int32_t SourceSharp_ClientViewportQuery( const char *pPanel );
void SourceSharp_ClientViewportShow( const char *pPanel, int32_t bShow,
                                     const unsigned char *pPairs, int32_t nPairBytes,
                                     int32_t nFlags );
void SourceSharp_ClientWindSpeed( float flTime, float *pOutXYZ );
void SourceSharp_ConNPrintf( void *pEngineClient, int32_t nPos, const char *pszText );
void SourceSharp_ConNXPrintf( void *pEngineClient, int32_t nIndex, float flTimeToLive,
                              const float *pColorRGB, int32_t bFixedWidthFont,
                              const char *pszText );
int32_t SourceSharp_ConVarsClientSetValue( const char *pName, float flValue );
int32_t SourceSharp_ConVarsClientWatch( const char *pName );
int32_t SourceSharp_DrawSprite( uint32_t hOwner, int32_t nModelIndex, float flFrame,
                                int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g,
                                int32_t b, int32_t a, float flScale, int32_t bWorldSpaceScale,
                                float flHDRColorScale, float flGlowProxySize );
int32_t SourceSharp_DrawSpriteOffset( uint32_t hOwner, const float *pOffsetXYZ,
                                      int32_t nModelIndex, float flFrame, int32_t nRenderMode,
                                      int32_t nRenderFx, int32_t r, int32_t g, int32_t b,
                                      int32_t a, float flScale, int32_t bWorldSpaceScale,
                                      float flHDRColorScale, float flGlowProxySize );
void SourceSharp_DynamicLightRelease( int32_t nKey );
void SourceSharp_DynamicLightUpdate( const SourceSharpDynamicLight *pLight );
void SourceSharp_EffectDispatchName( int32_t nIndex, char *pOut, int32_t nOutBytes );
int32_t SourceSharp_EffectRegistrationCount( void );
int32_t SourceSharp_EffectRegistrationName( int32_t iIndex, char *pOut, int32_t nOutSize );
void SourceSharp_EffectRegistrationRun( int32_t iIndex, const SourceSharpEffectData_t *pData );
void SourceSharp_EntitySetSkin( uint32_t hEntity, int32_t nSkin );
int32_t SourceSharp_FlashlightCreate( const SourceSharpFlashlightState *pState );
void SourceSharp_FlashlightDestroy( int32_t nHandle );
void SourceSharp_FlashlightProject( int32_t nHandle );
int32_t SourceSharp_FlashlightSupportsBorderColor( void );
int32_t SourceSharp_FlashlightTexture( const char *pszName );
void SourceSharp_FlashlightUpdate( int32_t nHandle, const SourceSharpFlashlightState *pState );
void SourceSharp_FxCacheMaterialHandles( void );
void SourceSharp_FxCacheUsedMaterials( void );
int32_t SourceSharp_FxCanPredict( void );
void SourceSharp_FxDispatchParticleEffect( int32_t nSystem, const float *pStartXYZ,
                                           const float *pEndXYZ, const float *pAnglesPYR,
                                           int32_t nEntIndex );
void SourceSharp_FxDrawQuads( int32_t nMaterial, int32_t nVertices, const float *pVertices,
                              int32_t bByteColor );
void SourceSharp_FxEjectBrass( const float *pOriginXYZ, const float *pAnglesPYR,
                               const float *pGunAnglesPYR, int32_t nType );
void SourceSharp_FxElight( int32_t nKey, const float *pOriginXYZ, int32_t r, int32_t g,
                           int32_t b, int32_t nExponent, float flRadius, float flDecay,
                           float flDie );
int32_t SourceSharp_FxEntityAttachment( int32_t nEntIndex, int32_t nAttachment, float *pOutXYZ,
                                        char *pModelOut, int32_t nModelSize );
int32_t SourceSharp_FxEntityRenderAngles( int32_t nEntIndex, float *pOutPYR );
float SourceSharp_FxGlowSightDistance( const float *pPositionXYZ );
int32_t SourceSharp_FxHandleEntityIndex( uint32_t hEntity );
void SourceSharp_FxImpactOverlay( const float *pPositionXYZ, const float *pColorRGB );
void SourceSharp_FxLoadModel( const char *pModel );
int32_t SourceSharp_FxLocalPlayer( void );
void SourceSharp_FxMainViewOrigin( float *pOutXYZ );
void SourceSharp_FxMaterialAddRef( int32_t nMaterial );
int32_t SourceSharp_FxMaterialFind( const char *pName );
int32_t SourceSharp_FxMaterialName( int32_t nMaterial, char *pOut, int32_t nOutSize );
void SourceSharp_FxMaterialRelease( int32_t nMaterial );
int32_t SourceSharp_FxMaterialsCached( void );
int32_t SourceSharp_FxModelFrameCount( const char *pModel );
void SourceSharp_FxPrecacheCache( int32_t nIndex, int32_t bPrecache );
int32_t SourceSharp_FxPrecacheCount( void );
int32_t SourceSharp_FxRecipientCount( const float *pOriginXYZ );
int32_t SourceSharp_FxRenderableAttachment( uint32_t hEntity, int32_t nAttachment,
                                            float *pOut6 );
int32_t SourceSharp_FxSpriteDrawBegin( uint32_t hOwner, const float *pOffsetXYZ,
                                       int32_t nModelIndex, float flFrame, int32_t nRenderMode,
                                       int32_t nRenderFx, int32_t r, int32_t g, int32_t b,
                                       int32_t a, float flScale, int32_t bWorldSpaceScale,
                                       float flHDRColorScale, float flGlowProxySize,
                                       float *pIn );
int32_t SourceSharp_FxSpriteDrawEnd( const float *pOut );
int32_t SourceSharp_FxSpriteDrawStore( const float *pOut );
float SourceSharp_FxSpriteGlowFraction( const float *pPositionXYZ, float flProxySize,
                                        float flProxyAspect );
int32_t SourceSharp_FxTempEntCreateNamed( int32_t nSlot, const float *pOriginXYZ,
                                          const char *pModel, int32_t nPriority );
void SourceSharp_FxVectorRandom( float flLo, float flHi, float *pOutXYZ );
void SourceSharp_FxView( float *pOut );
void SourceSharp_FxViewAngles( float *pOutPYR );
void SourceSharp_GetLightForPoint( void *pEngineClient, const float *pPosXYZ, int32_t bClamp,
                                   float *pOutXYZ );
void SourceSharp_GetLightForPointFast( void *pEngineClient, const float *pPosXYZ,
                                       int32_t bClamp, float *pOutXYZ );
int32_t SourceSharp_GetLocalPlayerHealth( void );
int32_t SourceSharp_GetLocalPlayerIndex( void );
int32_t SourceSharp_GetLocalPlayerOrigin( float *pOutXYZ );
void SourceSharp_HudLocalise( const char *pToken, char *pOut, int32_t nOutSize );
void SourceSharp_HudLookupBinding( const char *pBinding, char *pOut, int32_t nOutSize );
int32_t SourceSharp_HudTextMessageGet( const char *pName, SourceSharpTextMessage *pOut );
void SourceSharp_InputCursorState( int32_t *pVisible, int32_t *pLocked );
void SourceSharp_InputGetCursorPos( int32_t *pX, int32_t *pY );
void SourceSharp_InputHudFacts( float *pSensitivity, int32_t *pKeyBits, float *pFovAdjust );
void *SourceSharp_InputMailbox( void );
void SourceSharp_InputSetCursorArrow( void );
void SourceSharp_InputSetCursorPos( int32_t x, int32_t y );
void SourceSharp_InputSetLocalViewAngles( const float *pAngles );
void SourceSharp_InputShowScoreboard( int32_t bShow );
void SourceSharp_InputStopPitchDrift( void );
void SourceSharp_InputVguiScreenSize( int32_t *pWide, int32_t *pTall );
int32_t SourceSharp_InputViewerAimIsAssisted( void );
void SourceSharp_InputViewerCameraModeChanged( void );
int32_t SourceSharp_ParticleDefinitionReads( void );
int32_t SourceSharp_ParticleQueryUnanswered( int32_t *pOut, int32_t nMax );
int32_t SourceSharp_ParticleSystemFollowPoints( uint32_t hOwner, int32_t nPointMask );
int32_t SourceSharp_ParticleSystemNameFromIndex( int32_t nIndex, char *pszOut,
                                                 int32_t nOutMax );
int32_t SourceSharp_ParticleSystemOneShot( const char *pszName, const float *pSortOriginXYZ,
                                           const float *pPoints, int32_t nPoints,
                                           const float *pOrientation0 );
int32_t SourceSharp_ParticleSystemStart( uint32_t hOwner, const char *pszName );
void SourceSharp_ParticleSystemStop( uint32_t hOwner );
int32_t SourceSharp_ParticleSystemsRead( int32_t nIndex, SourceSharpParticleSystemInfo_t *pOut,
                                         char *pszName, int32_t nNameMax );
int32_t SourceSharp_ParticlesBrushPointContents( uint32_t hOwner, const float *pPointXYZ );
void SourceSharp_ParticlesConfigure( uint32_t hOwner, int32_t nChannel, float flCullRadius );
int32_t SourceSharp_ParticlesCreate( uint32_t hOwner, int32_t nChannel, const char *pszMaterial,
                                     float flCullRadius, int32_t bAlwaysSimulate );
int32_t SourceSharp_ParticlesCreateAt( int32_t nId, const char *pszMaterial,
                                       const float *pOriginXYZ, float flCullRadius,
                                       int32_t bAlwaysSimulate );
void SourceSharp_ParticlesDestroy( uint32_t hOwner, int32_t nChannel );
int32_t SourceSharp_ParticlesMaterialVarInt( uint32_t hOwner, int32_t nChannel,
                                             const char *pszVar, int32_t *pValue );
float SourceSharp_ParticlesPixelVisible( const float *pOriginXYZ, int32_t *pQueryHandle );
void SourceSharp_ParticlesSetLights( uint32_t hOwner, int32_t nChannel, const float *pLights,
                                     int32_t nFloats );
int32_t SourceSharp_ParticlesSetSpace( uint32_t hOwner, int32_t nChannel, uint32_t hEntity,
                                       int32_t nAttachment, int32_t nFlags,
                                       SourceSharpParticleFollow_t *pFollow );
int32_t SourceSharp_ParticlesTraceLine( const float *pStartXYZ, const float *pEndXYZ,
                                        int32_t nMask, float *pOut, int32_t nOut );
int32_t SourceSharp_ParticlesTraceMask( const float *pStartXYZ, const float *pEndXYZ,
                                        uint32_t nMask, float *pOut, int32_t nOut );
void SourceSharp_ParticlesView( float *pModelView12, int32_t *pViewId, float *pOriginForward6 );
int32_t SourceSharp_ParticlesWorldTrace( const float *pStartXYZ, const float *pEndXYZ,
                                         float *pOut, int32_t nOut );
void SourceSharp_PhysicsClientCollideExtent( void *pObject, const float *pOrigin,
                                             const float *pAngles, const float *pDirection,
                                             float *pOut );
int32_t SourceSharp_PhysicsClientPointContents( const float *pPoint );
void SourceSharp_PhysicsClientSplashEffect( int32_t nKind, const float *pOrigin,
                                            const float *pNormal, float flScale,
                                            const float *pColor, float flLifetime,
                                            float flAlpha );
void SourceSharp_PhysicsClientSplashLighting( const float *pPoint, float *pOut4 );
int32_t SourceSharp_PhysicsClientSurfaceAudio( int32_t nSurfaceProps, char *pHard,
                                               int32_t nHard, char *pSoft, int32_t nSoft,
                                               float *pOut3 );
int32_t SourceSharp_PrecipitationInPvs( uint32_t hOwner );
int32_t SourceSharp_PrecipitationPlayer( float *pOut );
int32_t SourceSharp_PrecipitationPointContents( const float *pXYZ );
int32_t SourceSharp_PrecipitationScreenWidth( void );
int32_t SourceSharp_PrecipitationTraceLine( const float *pStartXYZ, const float *pEndXYZ,
                                            int32_t nMask, int32_t bIgnorePlayer, float *pOut );
int32_t SourceSharp_PrecipitationTraceVolume( uint32_t hOwner, const float *pStartXYZ,
                                              const float *pEndXYZ, const float *pHullMinXYZ,
                                              const float *pHullMaxXYZ, float *pOut );
void SourceSharp_PrecipitationWorldBounds( float *pOut6 );
int32_t SourceSharp_ProxyBindEntity( int32_t hProxy, SourceSharpProxyEntity_t *pOut );
int32_t SourceSharp_ProxyFindTexture( int32_t hProxy, const char *pszName, const char *pszGroup,
                                      int32_t bComplain, int32_t *pIsError );
int32_t SourceSharp_ProxyFindVar( int32_t hProxy, const char *pszName, int32_t bComplain,
                                  int32_t *pFound );
void SourceSharp_ProxyNotShipped( const char *pszCapability, const char *pszReason );
int32_t SourceSharp_ProxyPlayerCustomFile( int32_t nPlayer, uint32_t *pCrc );
int32_t SourceSharp_ProxyReadView( int32_t nKind, float *pOut, int32_t nFloats );
int32_t SourceSharp_ProxyVarTexture( int32_t hProxy, int32_t nSlot, char *pOutName,
                                     int32_t nOutSize );
void SourceSharp_RegisterAmmoType( const char *pName, int32_t nDamageType, int32_t nMaxCarry,
                                   int32_t nTracerType, float flDamageForce, float flDamage );
int32_t SourceSharp_RenderViewToTarget( int32_t nTarget,
                                        const SourceSharpViewTargetSetup *pSetup );
void SourceSharp_RopeDraw( int32_t nMaterial, const float *pSegs, int32_t nSegs,
                           const float *pSolid, int32_t nSolid, int32_t nFlags );
int32_t SourceSharp_RopeMaterial( const char *pszName, int32_t *pOutTextureHeight,
                                  int32_t *pOutHasBack );
int32_t SourceSharp_SendServerMessage( const char *pKey, const uint8_t *pData, int32_t nLen );
void SourceSharp_SetViewerFogParams( const SourceSharpFogParams *pParams );
void SourceSharp_SetViewerSkyboxParams( const SourceSharpSkyboxParams *pParams );
void SourceSharp_SoundscapeMainView( float *pOut9 );
int32_t SourceSharp_SoundscapeMakingResLists( void );
void SourceSharp_SoundscapeSetPlayerDsp( int32_t nDspType );
void SourceSharp_SoundscapeSetRoomType( int32_t nRoomType );
void SourceSharp_SoundscapeTouchSoundFile( const char *pszWave );
void SourceSharp_SpriteTrailDestroy( uint32_t hOwner );
void SourceSharp_SpriteTrailUpdate( uint32_t hOwner, int32_t nRenderMode, int32_t bVisible );
void *SourceSharp_StandaloneEngineBlock( uint32_t hEntity, int32_t *pOutBytes );
int32_t SourceSharp_StandaloneIsDormant( uint32_t hEntity );
void SourceSharp_StandaloneSetBlending( uint32_t hEntity, int32_t bTranslucent,
                                        int32_t bIgnoreZ, int32_t nAlpha );
void SourceSharp_StandaloneSetCustomDraw( uint32_t hEntity, const float *pMinsXYZ,
                                          const float *pMaxsXYZ );
void SourceSharp_StandaloneSetFade( uint32_t hEntity, int32_t nStyle, float flStartDist,
                                    float flEndDist, float flMinAlpha );
void SourceSharp_StandaloneSetOverlayModel( uint32_t hEntity, int32_t nModelIndex );
void SourceSharp_StandaloneSetRagdoll( uint32_t hEntity, int32_t bRagdoll );
void SourceSharp_StandaloneSetShadowCast( uint32_t hEntity, int32_t nChoice );
void SourceSharp_StandaloneSetViewModel( uint32_t hEntity, int32_t bEnable );
int32_t SourceSharp_StandaloneShadowCast( uint32_t hEntity, int32_t *pOutChoice,
                                          int32_t *pOutHasShadow );
void SourceSharp_StandaloneStoreSequence( uint32_t hEntity, int32_t nSequence,
                                          float flPlaybackRate, float flAnimTime,
                                          float flDuration, int32_t bLooping );
void SourceSharp_SurfaceSetEdges( uint32_t hEntity, const unsigned char *pEdges,
                                  int32_t nCount );
int32_t SourceSharp_TempEntBuildClient( const void *pBlock, int32_t nBlockBytes,
                                        int32_t nBlobBytes, SourceSharpTeRecvBuilt_t *pOut );
int32_t SourceSharp_TempEntPoolAddVisible( int32_t nSlot );
int32_t SourceSharp_TempEntPoolCapacity( void );
int32_t SourceSharp_TempEntPoolCreate( int32_t nSlot, const float *pOriginXYZ,
                                       int32_t nModelIndex, int32_t nPriority );
void SourceSharp_TempEntPoolDestroy( int32_t nSlot );
int32_t SourceSharp_TempEntPoolReadLife( int32_t nSlot, SourceSharpTempEntLife_t *pOutLife );
int32_t SourceSharp_TempEntPoolReadStatics( int32_t nSlot, SourceSharpTempEntStatics_t *pOut );
int32_t SourceSharp_TempEntPoolReadTransform( int32_t nSlot,
                                              SourceSharpTempEntTransform_t *pOutTransform );
int32_t SourceSharp_TempEntPoolSimulate( int32_t nSlot, float flFrameTime,
                                         int32_t nFrameNumber );
int32_t SourceSharp_TempEntPoolWriteLife( int32_t nSlot,
                                          const SourceSharpTempEntLife_t *pLife );
int32_t SourceSharp_TempEntPoolWriteSpawn( int32_t nSlot,
                                           const SourceSharpTempEntSpawn_t *pSpawn );
int32_t SourceSharp_TempEntPoolWriteTransform( int32_t nSlot,
                                               const SourceSharpTempEntTransform_t *pTransform );
void SourceSharp_UiDebugOverlayClearDead( void );
int32_t SourceSharp_UiDebugOverlayText( int32_t nIndex, float *pOutX, float *pOutY,
                                        int32_t *pOutLine, uint32_t *pOutRgba, uint8_t *pText,
                                        int32_t nTextBytes );
void SourceSharp_UiDrawFilledRect( int32_t x0, int32_t y0, int32_t x1, int32_t y1,
                                   uint32_t nRgba );
void SourceSharp_UiDrawFilledRectFade( int32_t x0, int32_t y0, int32_t x1, int32_t y1,
                                       uint32_t nRgba, int32_t nAlpha0, int32_t nAlpha1,
                                       int32_t bHorizontal );
void SourceSharp_UiDrawLine( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba );
void SourceSharp_UiDrawOutlinedRect( int32_t x0, int32_t y0, int32_t x1, int32_t y1,
                                     uint32_t nRgba );
void SourceSharp_UiDrawPolygon( int32_t nTexture, const float *pXyuv, int32_t nVertices,
                                uint32_t nRgba );
void SourceSharp_UiDrawText( int32_t nFont, int32_t x, int32_t y, const uint16_t *pText,
                             int32_t nLength, uint32_t nRgba );
void SourceSharp_UiDrawTexturedRect( int32_t nTexture, int32_t x0, int32_t y0, int32_t x1,
                                     int32_t y1, float s0, float t0, float s1, float t1,
                                     uint32_t nRgba );
int32_t SourceSharp_UiFontAddFile( const char *pPath );
int32_t SourceSharp_UiFontAdvances( int32_t nFont, int32_t nFirst, int32_t nCount,
                                    int32_t *pOutAdvances );
int32_t SourceSharp_UiFontCreate( const char *pFamily, int32_t nTall, int32_t nWeight,
                                  int32_t nFlags );
void SourceSharp_UiFontMetrics( int32_t nFont, int32_t *pOutTall, int32_t *pOutAscent );
void SourceSharp_UiMeasureText( int32_t nFont, const uint16_t *pText, int32_t nLength,
                                int32_t *pOutWidth, int32_t *pOutHeight );
void SourceSharp_UiPlaySound( const char *pPath );
void SourceSharp_UiScreenSize( int32_t *pOutWide, int32_t *pOutTall );
void SourceSharp_UiSetClip( int32_t bEnabled, int32_t x0, int32_t y0, int32_t x1, int32_t y1 );
void SourceSharp_UiSetCursor( int32_t nShape );
void SourceSharp_UiSetMenuInput( int32_t bMouse, int32_t bKeyboard );
int32_t SourceSharp_UiTextureCreate( void );
void SourceSharp_UiTextureUpload( int32_t nTexture, const uint8_t *pRgba, int32_t nWide,
                                  int32_t nTall );
void SourceSharp_ViewOverrideSet( int32_t nActive, const float *pOriginXYZ,
                                  const float *pAngles, int32_t nSetViewAngles );
int32_t SourceSharp_ViewTargetInfo( int32_t nTarget, int32_t *pOut4 );
void SourceSharp_WindPublish( const float *pWind, const float *pSway, int32_t nWinds );
void SourceSharp_WorldTextMeshDestroy( uint32_t hOwner );
int32_t SourceSharp_WorldTextMeshUpdate( uint32_t hOwner, const char *pszMaterial,
                                         const float *pBounds, int32_t nFloats );
#endif


#endif // SOURCESHARP_ENTRYPOINTS_H
