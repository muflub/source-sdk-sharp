//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the player's state, between the loop and the blob ==//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// See the header. Compiled into both DLLs — the field list, the ask and the
// byte arithmetic must be the same code on the two sides or the ring compares
// one spelling against the other.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
// The client's player hierarchy (c_baseplayer.h), which completes C_FogController
// for playernet_vars.h's fog handle. The server needs none of it: server.so has
// no CBasePlayer (plan_server_novalve.md P5).
#include "baseplayer_shared.h"
#endif
#if defined( CLIENT_DLL )
#include "c_playerlocaldata.h"
#else
#include "ss_playerlocaldata.h"
#endif
#include "PlayerState.h"
#include "standalone_player_sync.h"


//-----------------------------------------------------------------------------
// The resolve generation.
//-----------------------------------------------------------------------------
static unsigned int g_nPlayerSyncGeneration = 1;

unsigned int SourceSharp_PlayerSyncGeneration()
{
	return g_nPlayerSyncGeneration;
}

void SourceSharp_PlayerSyncInvalidate( const char *pReason )
{
	g_nPlayerSyncGeneration++;

	Msg( "[SourceSharp] player sync: offsets invalidated (%s), generation %u\n",
		pReason ? pReason : "unspecified", g_nPlayerSyncGeneration );
}

//-----------------------------------------------------------------------------
// The ask. One warning per failed resolve attempt set, not per field — the
// interesting fact is "the class is not published here", not 29 spellings
// of it.
//-----------------------------------------------------------------------------
static int ResolveOffset( const SourceSharpNetStateAsk_t &ask, const char *pField, bool *pbAll )
{
	const int nOffset = ask.pfnFieldOffset( "ss_player", pField );

	if ( nOffset < 0 )
		*pbAll = false;

	return nOffset;
}

static SourceSharpPackedField_t ResolvePacked( const SourceSharpNetStateAsk_t &ask,
                                               const char *pField, bool *pbAll )
{
	const int nOffset = ask.pfnFieldOffset( "ss_player", pField );
	const int nBits   = ask.pfnFieldBits( "ss_player", pField );

	SourceSharpPackedField_t field = SourceSharpPayload_Place( nOffset, nBits );

	if ( !field.IsResolved() )
		*pbAll = false;

	return field;
}

bool SourceSharpPlayerFields_Resolve( SourceSharpPlayerFields_t *pFields )
{
	SourceSharpNetStateAsk_t ask;
	if ( !pFields || !SourceSharp_NetStateAsk( &ask ) )
		return false;

	bool bAll = true;

	pFields->nHealth          = ResolveOffset( ask, "Health", &bAll );
	pFields->nMaxSpeed        = ResolveOffset( ask, "MaxSpeed", &bAll );
	pFields->nFallVelocity    = ResolveOffset( ask, "FallVelocity", &bAll );
	pFields->nDuckTime        = ResolveOffset( ask, "DuckTime", &bAll );
	pFields->nDuckJumpTime    = ResolveOffset( ask, "DuckJumpTime", &bAll );
	pFields->nJumpTime        = ResolveOffset( ask, "JumpTime", &bAll );
	pFields->nStepSize        = ResolveOffset( ask, "StepSize", &bAll );
	pFields->nWaterJumpTime   = ResolveOffset( ask, "WaterJumpTime", &bAll );
	pFields->nSurfaceFriction = ResolveOffset( ask, "SurfaceFriction", &bAll );
	pFields->nOldForwardMove  = ResolveOffset( ask, "OldForwardMove", &bAll );
	pFields->nFlags           = ResolveOffset( ask, "Flags", &bAll );
	pFields->nOldButtons      = ResolveOffset( ask, "OldButtons", &bAll );
	pFields->nTickBase        = ResolveOffset( ask, "TickBase", &bAll );
	pFields->nGroundEntity    = ResolveOffset( ask, "GroundEntity", &bAll );

	pFields->Alive             = ResolvePacked( ask, "Alive", &bAll );
	pFields->Ducked            = ResolvePacked( ask, "Ducked", &bAll );
	pFields->Ducking           = ResolvePacked( ask, "Ducking", &bAll );
	pFields->InDuckJump        = ResolvePacked( ask, "InDuckJump", &bAll );
	pFields->AllowAutoMovement = ResolvePacked( ask, "AllowAutoMovement", &bAll );
	pFields->WaterLevel        = ResolvePacked( ask, "WaterLevel", &bAll );
	pFields->WaterType         = ResolvePacked( ask, "WaterType", &bAll );
	pFields->MoveType          = ResolvePacked( ask, "MoveType", &bAll );

	pFields->nOrigin        = ResolveOffset( ask, "Origin", &bAll );
	pFields->nVelocity      = ResolveOffset( ask, "Velocity", &bAll );
	pFields->nBaseVelocity  = ResolveOffset( ask, "BaseVelocity", &bAll );
	pFields->nViewOffset    = ResolveOffset( ask, "ViewOffset", &bAll );
	pFields->nPunchAngle    = ResolveOffset( ask, "PunchAngle", &bAll );
	pFields->nPunchAngleVel = ResolveOffset( ask, "PunchAngleVel", &bAll );
	pFields->nLadderNormal  = ResolveOffset( ask, "LadderNormal", &bAll );
	pFields->nWaterJumpVel  = ResolveOffset( ask, "WaterJumpVel", &bAll );

	if ( !bAll )
	{
		Warning( "[SourceSharp] player sync: ss_player's fields did not all resolve — "
		         "is PlayerNetState published on this side?\n" );
		return false;
	}

	pFields->nGeneration = g_nPlayerSyncGeneration;
	return true;
}

//-----------------------------------------------------------------------------
// The byte movers. memcpy per field rather than a struct: the blob's layout
// is C#'s and nothing here may assume adjacency.
//-----------------------------------------------------------------------------
static inline void WriteF32( unsigned char *pBlob, int nOffset, float flValue )
{
	if ( nOffset >= 0 )
		memcpy( pBlob + nOffset, &flValue, sizeof( flValue ) );
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
static inline float ReadF32( const unsigned char *pBlob, int nOffset )
{
	float flValue = 0.0f;
	if ( nOffset >= 0 )
		memcpy( &flValue, pBlob + nOffset, sizeof( flValue ) );
	return flValue;
}
#endif

static inline void WriteI32( unsigned char *pBlob, int nOffset, int nValue )
{
	if ( nOffset >= 0 )
		memcpy( pBlob + nOffset, &nValue, sizeof( nValue ) );
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
static inline int ReadI32( const unsigned char *pBlob, int nOffset )
{
	int nValue = 0;
	if ( nOffset >= 0 )
		memcpy( &nValue, pBlob + nOffset, sizeof( nValue ) );
	return nValue;
}
#endif

static inline void WriteVec( unsigned char *pBlob, int nOffset, const Vector &vec )
{
	if ( nOffset >= 0 )
	{
		float xyz[3] = { vec.x, vec.y, vec.z };
		memcpy( pBlob + nOffset, xyz, sizeof( xyz ) );
	}
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
static inline Vector ReadVec( const unsigned char *pBlob, int nOffset )
{
	float xyz[3] = { 0, 0, 0 };
	if ( nOffset >= 0 )
		memcpy( xyz, pBlob + nOffset, sizeof( xyz ) );
	return Vector( xyz[0], xyz[1], xyz[2] );
}
#endif

static inline void WriteAng( unsigned char *pBlob, int nOffset, const QAngle &ang )
{
	if ( nOffset >= 0 )
	{
		float pyr[3] = { ang.x, ang.y, ang.z };
		memcpy( pBlob + nOffset, pyr, sizeof( pyr ) );
	}
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
static inline QAngle ReadAng( const unsigned char *pBlob, int nOffset )
{
	float pyr[3] = { 0, 0, 0 };
	if ( nOffset >= 0 )
		memcpy( pyr, pBlob + nOffset, sizeof( pyr ) );
	return QAngle( pyr[0], pyr[1], pyr[2] );
}
#endif

//-----------------------------------------------------------------------------
// Native -> blob.
//-----------------------------------------------------------------------------
void SourceSharpPlayerSync_Write( unsigned char *pBlob,
                                  const SourceSharpPlayerFields_t &fields,
                                  IMovementPlayer *pPlayer,
                                  const SourceSharpPlayerExtras_t &extras )
{
	if ( !pBlob || !pPlayer || !fields.IsCurrent() )
		return;

	MvLocalData_t &local = pPlayer->MvLocal();

	WriteI32( pBlob, fields.nHealth,          extras.nHealth );
	WriteF32( pBlob, fields.nMaxSpeed,        extras.flMaxSpeed );
	WriteF32( pBlob, fields.nFallVelocity,    local.m_flFallVelocity );
	WriteF32( pBlob, fields.nDuckTime,        local.m_flDucktime );
	WriteF32( pBlob, fields.nDuckJumpTime,    local.m_flDuckJumpTime );
	WriteF32( pBlob, fields.nJumpTime,        local.m_flJumpTime );
	WriteF32( pBlob, fields.nStepSize,        local.m_flStepSize );
	WriteF32( pBlob, fields.nWaterJumpTime,   pPlayer->MvWaterJumpTime() );
	WriteF32( pBlob, fields.nSurfaceFriction, pPlayer->MvSurfaceFriction() );
	WriteF32( pBlob, fields.nOldForwardMove,  local.m_flOldForwardMove );
	WriteI32( pBlob, fields.nFlags,           pPlayer->MvGetFlags() );
	WriteI32( pBlob, fields.nOldButtons,      local.m_nOldButtons );
	WriteI32( pBlob, fields.nTickBase,        extras.nTickBase );
	WriteI32( pBlob, fields.nGroundEntity,    extras.nGroundEntity );

	SourceSharpPayload_WritePacked( pBlob, fields.Alive,             extras.bAlive ? 1u : 0u );
	SourceSharpPayload_WritePacked( pBlob, fields.Ducked,            local.m_bDucked ? 1u : 0u );
	SourceSharpPayload_WritePacked( pBlob, fields.Ducking,           local.m_bDucking ? 1u : 0u );
	SourceSharpPayload_WritePacked( pBlob, fields.InDuckJump,        local.m_bInDuckJump ? 1u : 0u );
	SourceSharpPayload_WritePacked( pBlob, fields.AllowAutoMovement, local.m_bAllowAutoMovement ? 1u : 0u );
	SourceSharpPayload_WritePacked( pBlob, fields.WaterLevel,        (unsigned int)pPlayer->MvGetWaterLevel() );
	SourceSharpPayload_WritePacked( pBlob, fields.WaterType,         (unsigned int)pPlayer->MvGetWaterType() );
	SourceSharpPayload_WritePacked( pBlob, fields.MoveType,          (unsigned int)pPlayer->MvGetMoveType() );

	WriteVec( pBlob, fields.nOrigin,        extras.vecOrigin );
	WriteVec( pBlob, fields.nVelocity,      extras.vecVelocity );
	WriteVec( pBlob, fields.nBaseVelocity,  pPlayer->MvGetBaseVelocity() );
	WriteVec( pBlob, fields.nViewOffset,    pPlayer->MvGetViewOffset() );
	WriteAng( pBlob, fields.nPunchAngle,    local.m_vecPunchAngle );
	WriteAng( pBlob, fields.nPunchAngleVel, local.m_vecPunchAngleVel );
	WriteVec( pBlob, fields.nLadderNormal,  pPlayer->MvLadderNormal() );
	WriteVec( pBlob, fields.nWaterJumpVel,  pPlayer->MvWaterJumpVel() );
}

//-----------------------------------------------------------------------------
// Blob -> native.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void SourceSharpPlayerSync_Read( const unsigned char *pBlob,
                                 const SourceSharpPlayerFields_t &fields,
                                 IMovementPlayer *pPlayer,
                                 SourceSharpPlayerExtras_t *pExtras )
{
	if ( !pBlob || !pPlayer || !pExtras || !fields.IsCurrent() )
		return;

	MvLocalData_t &local = pPlayer->MvLocal();

	pExtras->nHealth    = ReadI32( pBlob, fields.nHealth );
	pExtras->flMaxSpeed = ReadF32( pBlob, fields.nMaxSpeed );
	pExtras->nTickBase  = ReadI32( pBlob, fields.nTickBase );

	// The ground handle travels as bytes and is applied by the CALLER, into its
	// own m_hGround. Not through MvSetGroundEntity: that one takes a resolved
	// pointer, and this side has no business resolving — a client whose ground
	// entity has not been created yet would answer NULL and hand the replay the
	// exact contradiction (FL_ONGROUND set, no ground) this field removes.
	pExtras->nGroundEntity = ( fields.nGroundEntity >= 0 )
	                           ? ReadI32( pBlob, fields.nGroundEntity )
	                           : GROUNDWIRE_NONE;
	pExtras->bAlive     = SourceSharpPayload_ReadPacked( pBlob, fields.Alive ) != 0;

	local.m_flFallVelocity     = ReadF32( pBlob, fields.nFallVelocity );
	local.m_flDucktime         = ReadF32( pBlob, fields.nDuckTime );
	local.m_flDuckJumpTime     = ReadF32( pBlob, fields.nDuckJumpTime );
	local.m_flJumpTime         = ReadF32( pBlob, fields.nJumpTime );
	local.m_flStepSize         = ReadF32( pBlob, fields.nStepSize );
	local.m_flOldForwardMove   = ReadF32( pBlob, fields.nOldForwardMove );
	local.m_nOldButtons        = ReadI32( pBlob, fields.nOldButtons );
	local.m_bDucked            = SourceSharpPayload_ReadPacked( pBlob, fields.Ducked ) != 0;
	local.m_bDucking           = SourceSharpPayload_ReadPacked( pBlob, fields.Ducking ) != 0;
	local.m_bInDuckJump        = SourceSharpPayload_ReadPacked( pBlob, fields.InDuckJump ) != 0;
	local.m_bAllowAutoMovement = SourceSharpPayload_ReadPacked( pBlob, fields.AllowAutoMovement ) != 0;
	local.m_vecPunchAngle      = ReadAng( pBlob, fields.nPunchAngle );
	local.m_vecPunchAngleVel   = ReadAng( pBlob, fields.nPunchAngleVel );

	pPlayer->MvWaterJumpTime()   = ReadF32( pBlob, fields.nWaterJumpTime );
	pPlayer->MvSurfaceFriction() = ReadF32( pBlob, fields.nSurfaceFriction );
	pPlayer->MvLadderNormal()    = ReadVec( pBlob, fields.nLadderNormal );
	pPlayer->MvWaterJumpVel()    = ReadVec( pBlob, fields.nWaterJumpVel );

	// Flags wholesale: the interface spells it add/remove, and the blob's word
	// is the whole truth here.
	pPlayer->MvRemoveFlag( ~0 );
	pPlayer->MvAddFlag( ReadI32( pBlob, fields.nFlags ) );

	pPlayer->MvSetWaterLevel( (int)SourceSharpPayload_ReadPacked( pBlob, fields.WaterLevel ) );
	pPlayer->MvSetWaterType( (int)SourceSharpPayload_ReadPacked( pBlob, fields.WaterType ) );
	pPlayer->MvSetMoveType( (MoveType_t)SourceSharpPayload_ReadPacked( pBlob, fields.MoveType ) );

	pPlayer->MvSetBaseVelocity( ReadVec( pBlob, fields.nBaseVelocity ) );
	pPlayer->MvSetViewOffset( ReadVec( pBlob, fields.nViewOffset ) );

	pPlayer->MvPlayerState().deadflag = !pExtras->bAlive;

	pExtras->vecOrigin   = ReadVec( pBlob, fields.nOrigin );
	pExtras->vecVelocity = ReadVec( pBlob, fields.nVelocity );
}
#endif
