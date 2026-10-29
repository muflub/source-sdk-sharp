//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The legacy temp entity pool's native seam. The pool's bookkeeping is managed
// (ClientTempEntPool); the C_LocalTempEntity objects, their per-frame physics
// (Frame(), which needs a full trace) and their sounds stay native.
//
// Managed addresses a tempent by slot, never by pointer: the managed pool
// survives a reload and the objects do not. An emptied slot answers false to
// everything and the managed pool drops it on its next pass.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "tempent.h"
#include "c_te_legacytempents.h"

#include "env_wind_shared.h"
#include "fx.h"
#include "iefx.h"
#include "dlight.h"
#include "engine/IEngineSound.h"
#include "model_types.h"

#include "imanaged_areas_client.h"

// Forward-declared for Clear's sweep; defined with the ABI slots below.
void SourceSharp_TempEntPoolDestroy( int32_t nSlot );

// The slot table: one row per slot, NULL when empty, sized to the pool's own
// capacity.
static C_LocalTempEntity *g_pTempEntSlots[CTempEnts::MAX_TEMP_ENTITIES];

// The pool instance. CTempEnts is the only ITempEnts implementation linked into
// this build, so the downcast is safe.
static inline CTempEnts *ThePool()
{
	return static_cast<CTempEnts *>( tempents );
}

static inline bool ValidSlot( int nSlot )
{
	return nSlot >= 0 && nSlot < CTempEnts::MAX_TEMP_ENTITIES;
}

// CTempEnts's private pool and AddVisibleTempEntity, reached from outside;
// declared in c_te_legacytempents.h under #ifdef SOURCESHARP.
void CTempEnts::PoolFreeObject( C_LocalTempEntity *pTemp )
{
	m_TempEntsPool.Free( pTemp );
}

int CTempEnts::PoolAddVisible( C_LocalTempEntity *pTemp )
{
	return AddVisibleTempEntity( pTemp );
}

void SourceSharpTePool_Update( float flFrameTime )
{
	if ( IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient() )
		pTempEnts->OnTempEntPoolUpdate( flFrameTime );
}

// Frees every live tempent (CTempEnts::Clear). The sweep afterwards covers the
// managed side being down (a reload, or after ClientShutdown): a slot left bound
// would hold a tempent the memory pool believes is live across a level change.
void SourceSharpTePool_Clear()
{
	if ( IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient() )
		pTempEnts->OnTempEntPoolClear();

	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		if ( g_pTempEntSlots[i] != NULL )
			SourceSharp_TempEntPoolDestroy( i );
	}
}

// CTempEnts::FindTempEntByID over the slot table, which holds exactly what the
// managed active list holds.
C_LocalTempEntity *SourceSharpTePool_Find( int nID, int nSubID )
{
	// HACK HACK: We're using skin and hitsounds as a hacky way to store an ID
	// and sub-ID for later identification (stock).
	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		C_LocalTempEntity *p = g_pTempEntSlots[i];

		if ( p && p->m_nSkin == nID && p->hitSound == nSubID )
			return p;
	}

	return NULL;
}

int32_t SourceSharp_TempEntPoolCapacity()
{
	return (int32_t)CTempEnts::MAX_TEMP_ENTITIES;
}

// Gives one back: stock's TempEntFree in its order, minus the list removal,
// which is managed's.
void SourceSharp_TempEntPoolDestroy( int32_t nSlot )
{
	if ( !ValidSlot( nSlot ) )
		return;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return;

	// Emptied first: the teardown below could reach something that asks this
	// table what is live.
	g_pTempEntSlots[nSlot] = NULL;

	pTemp->RemoveFromLeafSystem();
	pTemp->OnRemoveTempEntity();

	ThePool()->PoolFreeObject( pTemp );
}

int32_t SourceSharp_TempEntPoolReadLife( int32_t nSlot, SourceSharpTempEntLife_t *pOutLife )
{
	if ( !ValidSlot( nSlot ) || !pOutLife )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return 0;

	pOutLife->Die = pTemp->die;
	pOutLife->Flags = pTemp->flags;
	pOutLife->FadeSpeed = pTemp->fadeSpeed;
	pOutLife->RenderAmt = pTemp->tempent_renderamt;
	pOutLife->Priority = pTemp->priority;
	pOutLife->ClientIndex = pTemp->clientIndex;
	pOutLife->RenderMode = pTemp->m_nRenderMode;
	pOutLife->Alpha = pTemp->m_clrRender.a;

	return 1;
}

int32_t SourceSharp_TempEntPoolWriteLife( int32_t nSlot, const SourceSharpTempEntLife_t *pLife )
{
	if ( !ValidSlot( nSlot ) || !pLife )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return 0;

	pTemp->die = pLife->Die;
	pTemp->flags = pLife->Flags;
	pTemp->fadeSpeed = pLife->FadeSpeed;
	pTemp->tempent_renderamt = pLife->RenderAmt;
	pTemp->priority = pLife->Priority;
	pTemp->clientIndex = (short)pLife->ClientIndex;

	// Through the setters stock's IsActive uses for the fade-out, not the members.
	pTemp->SetRenderMode( (RenderMode_t)pLife->RenderMode );
	pTemp->SetRenderColorA( (byte)pLife->Alpha );

	return 1;
}

// The transform managed owns: read once when the managed pool takes a tempent
// over, written once per frame after it integrates.
static void VecOut( float *pOut, const Vector &v )
{
	pOut[0] = v.x;
	pOut[1] = v.y;
	pOut[2] = v.z;
}

static void AngOut( float *pOut, const QAngle &a )
{
	pOut[0] = a.x;
	pOut[1] = a.y;
	pOut[2] = a.z;
}

int32_t SourceSharp_TempEntPoolReadTransform( int32_t nSlot, SourceSharpTempEntTransform_t *pOut )
{
	if ( !ValidSlot( nSlot ) || !pOut )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return 0;

	VecOut( pOut->OriginXYZ, pTemp->GetLocalOrigin() );
	AngOut( pOut->AnglesPYR, pTemp->GetLocalAngles() );
	VecOut( pOut->VelocityXYZ, pTemp->GetVelocity() );
	VecOut( pOut->AccelerationXYZ, pTemp->GetAcceleration() );
	AngOut( pOut->AngVelocityPYR, pTemp->m_vecTempEntAngVelocity );

	pOut->flSpriteScale = pTemp->m_flSpriteScale;
	pOut->flFrame = pTemp->m_flFrame;
	pOut->flSineX = pTemp->x;
	pOut->flSineY = pTemp->y;

	return 1;
}

int32_t SourceSharp_TempEntPoolWriteTransform( int32_t nSlot, const SourceSharpTempEntTransform_t *pIn )
{
	if ( !ValidSlot( nSlot ) || !pIn )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return 0;

	// SetLocal rather than SetAbs, as Frame itself calls; a tempent has no parent.
	pTemp->SetLocalOrigin( Vector( pIn->OriginXYZ[0], pIn->OriginXYZ[1], pIn->OriginXYZ[2] ) );
	pTemp->SetLocalAngles( QAngle( pIn->AnglesPYR[0], pIn->AnglesPYR[1], pIn->AnglesPYR[2] ) );

	pTemp->SetVelocity( Vector( pIn->VelocityXYZ[0], pIn->VelocityXYZ[1], pIn->VelocityXYZ[2] ) );
	pTemp->SetAcceleration( Vector( pIn->AccelerationXYZ[0], pIn->AccelerationXYZ[1], pIn->AccelerationXYZ[2] ) );
	pTemp->m_vecTempEntAngVelocity = QAngle( pIn->AngVelocityPYR[0], pIn->AngVelocityPYR[1], pIn->AngVelocityPYR[2] );

	pTemp->m_flSpriteScale = pIn->flSpriteScale;
	pTemp->m_flFrame = pIn->flFrame;
	pTemp->x = pIn->flSineX;
	pTemp->y = pIn->flSineY;

	return 1;
}

// The write-once state managed reads once when it takes a tempent over. Asked
// of the object because native spawn routes still exist that write no managed
// record.
int32_t SourceSharp_TempEntPoolReadStatics( int32_t nSlot, SourceSharpTempEntStatics_t *pOut )
{
	if ( !ValidSlot( nSlot ) || !pOut )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return 0;

	pOut->flBounceFactor = pTemp->bounceFactor;
	pOut->nHitSound = pTemp->hitSound;
	pOut->flFrameMax = pTemp->m_flFrameMax;
	pOut->flFrameRate = pTemp->m_flFrameRate;

	VecOut( pOut->TentOffsetXYZ, pTemp->tentOffset );

	pOut->nOwnerEntityIndex = pTemp->TempEntOwnerIndex();
	pOut->nFlickerFrame = pTemp->m_nFlickerFrame;

	// No code in this build gives a tempent an impact effect, so this is clear.
	pOut->bHasImpactEffect = 0;

	return 1;
}

// GetWindspeedAtTime, what FTENT_WINDBLOWN accelerates towards; zero with no wind.
void SourceSharp_ClientWindSpeed( float flTime, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	Vector vecWind;
	vecWind.Init();

	GetWindspeedAtTime( flTime, vecWind );

	VecOut( pOutXYZ, vecWind );
}

// A client entity's origin by index, for FTENT_PLYRATTACHMENT. Answers 0 and
// writes nothing when the index names no entity, as stock's branch did.
int32_t SourceSharp_ClientEntityOrigin( int32_t nEntityIndex, float *pOutXYZ )
{
	if ( !pOutXYZ || nEntityIndex < 0 )
		return 0;

	IClientEntity *pClient = SourceSharp_ClientEntityFromIndex( nEntityIndex );

	if ( !pClient )
		return 0;

	const Vector &vecOrigin = pClient->GetAbsOrigin();

	pOutXYZ[0] = vecOrigin.x;
	pOutXYZ[1] = vecOrigin.y;
	pOutXYZ[2] = vecOrigin.z;

	return 1;
}

// A client entity's model bounds and yaw by index, for CTempEnts::FizzEffect.
// GetModelBounds, not the render bounds or the collideable's box: for a brush
// model it is already world-space, which is what stock relies on. Resolved
// through GetClientEntity so both hierarchies answer. Answers 0 and writes
// nothing for no entity or no model.
int32_t SourceSharp_ClientEntityModelBounds( int32_t nEntityIndex, float *pOutMinsXYZ, float *pOutMaxsXYZ,
											 float *pOutAnglesPYR )
{
	if ( nEntityIndex < 0 )
		return 0;

	IClientEntity *pClient = SourceSharp_ClientEntityFromIndex( nEntityIndex );

	if ( !pClient )
		return 0;

	const model_t *pModel = pClient->GetModel();

	if ( !pModel )
		return 0;

	if ( pOutMinsXYZ || pOutMaxsXYZ )
	{
		Vector vecMins, vecMaxs;

		modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );

		if ( pOutMinsXYZ )
		{
			pOutMinsXYZ[0] = vecMins.x;
			pOutMinsXYZ[1] = vecMins.y;
			pOutMinsXYZ[2] = vecMins.z;
		}

		if ( pOutMaxsXYZ )
		{
			pOutMaxsXYZ[0] = vecMaxs.x;
			pOutMaxsXYZ[1] = vecMaxs.y;
			pOutMaxsXYZ[2] = vecMaxs.z;
		}
	}

	if ( pOutAnglesPYR )
	{
		// GetRenderAngles on C_StandaloneEntity is stock's GetLocalAngles(); a
		// fizzing volume is unparented, so the two agree.
		const QAngle &ang = pClient->GetRenderAngles();

		pOutAnglesPYR[0] = ang.x;
		pOutAnglesPYR[1] = ang.y;
		pOutAnglesPYR[2] = ang.z;
	}

	return 1;
}

int32_t SourceSharp_TempEntPoolAddVisible( int32_t nSlot )
{
	if ( !ValidSlot( nSlot ) )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return 0;

	return ThePool()->PoolAddVisible( pTemp ) ? 1 : 0;
}

// cs_cl_te_pool_shell: ejects shell casings through CTempEnts::EjectBrass in front of
// the view, exercising the pool end to end. A diagnostic; cheat-flagged.
static void TempEntPoolShell_f( const CCommand &args )
{
	const int nCount = ( args.ArgC() > 1 ) ? atoi( args[1] ) : 1;
	const int nType = ( args.ArgC() > 2 ) ? atoi( args[2] ) : 0;

	if ( nCount <= 0 || nType < 0 || nType > 2 )
	{
		Msg( "cs_cl_te_pool_shell [count] [type 0=pistol 1=rifle 2=shotgun]\n" );
		return;
	}

	// The view, not the player's eyes: no client entity here is a C_BasePlayer.
	const Vector vecOrigin = MainViewOrigin();
	const QAngle angView = MainViewAngles();

	for ( int i = 0; i < nCount; i++ )
	{
		tempents->EjectBrass( vecOrigin, angView, angView, nType );
	}

	Msg( "[SourceSharp] cs_cl_te_pool_shell: asked the pool for %d shell(s) of type %d "
		 "at (%.0f %.0f %.0f)\n",
		 nCount, nType, vecOrigin.x, vecOrigin.y, vecOrigin.z );
}

static ConCommand cs_cl_te_pool_shell(
	"cs_cl_te_pool_shell", TempEntPoolShell_f,
	"Eject brass through CTempEnts::EjectBrass -- the legacy temp entity pool's own entry "
	"point, for verifying the pool end to end. cs_cl_te_pool_shell [count] [type 0|1|2]",
	FCVAR_CHEAT );

// The high-priority, player-attached half: AttachTentToPlayer allocates with
// TempEntAllocHigh, FTENT_PLYRATTACHMENT and FTENT_PERSIST. It has no caller in
// this build, so this exercises it. The model is sprites/bubble.vmt, which the
// managed server precaches; a zero index is reported rather than passed on.
static void TempEntPoolAttach_f( const CCommand &args )
{
	const int nCount = ( args.ArgC() > 1 ) ? atoi( args[1] ) : 1;
	const int nPlayer = ( args.ArgC() > 2 ) ? atoi( args[2] ) : 1;

	if ( nCount <= 0 )
	{
		Msg( "cs_cl_te_pool_attach [count] [player]\n" );
		return;
	}

	const int nModelIndex = modelinfo->GetModelIndex( "sprites/bubble.vmt" );

	if ( nModelIndex <= 0 )
	{
		Warning( "cs_cl_te_pool_attach: sprites/bubble.vmt is not precached on this "
				 "client, so there is no model to attach\n" );
		return;
	}

	const model_t *pModel = modelinfo->GetModel( nModelIndex );

	if ( !pModel )
	{
		Warning( "cs_cl_te_pool_attach: model index %d does not resolve\n", nModelIndex );
		return;
	}

	for ( int i = 0; i < nCount; i++ )
	{
		tempents->AttachTentToPlayer( nPlayer, nModelIndex, 40.0f, 30.0f );
	}

	Msg( "[SourceSharp] cs_cl_te_pool_attach: asked the pool for %d high-priority "
		 "attachment(s) on player %d, model %d\n",
		 nCount, nPlayer, nModelIndex );
}

static ConCommand cs_cl_te_pool_attach( "cs_cl_te_pool_attach", TempEntPoolAttach_f,
										"Attach high-priority, FTENT_PLYRATTACHMENT tempents to a player through "
										"CTempEnts::AttachTentToPlayer -- what CTEKillPlayerAttachments kills. "
										"cs_cl_te_pool_attach [count] [player]",
										FCVAR_CHEAT );

// cs_cl_te_live: each live tempent's position and owner. With "bones", asks
// every live studio tempent for its bones, as the engine does, into a
// NaN-filled buffer, and counts matrices left untouched.
static bool MatrixIsUnwritten( const matrix3x4_t &m )
{
	// The sentinel is a NaN in every cell; a written matrix has none.
	for ( int r = 0; r < 3; r++ )
		for ( int c = 0; c < 4; c++ )
			if ( m[r][c] == m[r][c] )
				return false;
	return true;
}

static void TempEntPoolBones()
{
	static matrix3x4_t s_Bones[MAXSTUDIOBONES];

	int nTempEnts = 0, nModelBones = 0, nUnwrittenModel = 0, nUnwrittenAny = 0;

	for ( int nSlot = 0; nSlot < CTempEnts::MAX_TEMP_ENTITIES; nSlot++ )
	{
		C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];
		const model_t *pModel = pTemp ? pTemp->GetModel() : NULL;

		if ( !pModel || modelinfo->GetModelType( pModel ) != mod_studio )
			continue;

		MDLCACHE_CRITICAL_SECTION();
		studiohdr_t *pHdr = modelinfo->GetStudiomodel( pModel );
		if ( !pHdr )
			continue;

		const float flNaN = std::numeric_limits<float>::quiet_NaN();
		for ( int i = 0; i < MAXSTUDIOBONES; i++ )
			for ( int r = 0; r < 3; r++ )
				for ( int c = 0; c < 4; c++ )
					s_Bones[i][r][c] = flNaN;

		if ( !pTemp->SetupBones( s_Bones, MAXSTUDIOBONES, BONE_USED_BY_ANYTHING, gpGlobals->curtime ) )
			continue;

		nTempEnts++;
		nModelBones += pHdr->numbones;

		for ( int i = 0; i < MAXSTUDIOBONES; i++ )
		{
			if ( !MatrixIsUnwritten( s_Bones[i] ) )
				continue;
			nUnwrittenAny++;
			if ( i < pHdr->numbones )
				nUnwrittenModel++;
		}
	}

	Msg( "[SourceSharp] cs_cl_te_live bones: %d studio tempent(s), %d model bone(s), "
		 "%d model bone matrices unwritten, %d of %d slots unwritten\n",
		 nTempEnts, nModelBones, nUnwrittenModel, nUnwrittenAny, nTempEnts * MAXSTUDIOBONES );
}

static void TempEntPoolLive_f( const CCommand &args )
{
	if ( args.ArgC() > 1 && !V_stricmp( args.Arg( 1 ), "bones" ) )
	{
		TempEntPoolBones();
		return;
	}

	int nLive = 0;

	Msg( "[SourceSharp] cs_cl_te_live: slot  origin                    "
		 "client  owner  ownerOK  flags     die-in\n" );

	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		C_LocalTempEntity *pTemp = g_pTempEntSlots[i];

		if ( !pTemp )
			continue;

		nLive++;

		const Vector &vecOrigin = pTemp->GetAbsOrigin();

		Msg( "[SourceSharp]   %4d  (%8.1f %8.1f %8.1f)  %6d  %5d  %7s  %08x  %6.2f\n", i, vecOrigin.x, vecOrigin.y,
			 vecOrigin.z, (int)pTemp->clientIndex, pTemp->TempEntOwnerIndex(),
			 pTemp->TempEntOwnerHandle() ? "yes" : "no", (unsigned int)pTemp->flags, pTemp->die - gpGlobals->curtime );
	}

	Msg( "[SourceSharp] cs_cl_te_live: %d live of %d\n", nLive, (int)CTempEnts::MAX_TEMP_ENTITIES );

	// What an owner index resolves to: the both-hierarchies handle the spawn stores.
	if ( args.ArgC() > 1 )
	{
		const int nEntity = atoi( args[1] );

		const CBaseHandle hSeam = SourceSharp_ClientHandleFromIndex( nEntity );
		IClientUnknown *pSeamed = SourceSharp_ClientUnknownFromHandle( hSeam );

		Msg( "[SourceSharp] cs_cl_te_live: owner lookup for entity %d\n"
			 "  seam handle  (what the spawn stores)        : %s, entry %d\n"
			 "  seam handle resolves to an IClientUnknown   : %s\n",
			 nEntity, hSeam.IsValid() ? "VALID" : "INVALID", hSeam.IsValid() ? hSeam.GetEntryIndex() : -1,
			 pSeamed ? "yes" : "no" );
	}
}

static ConCommand cs_cl_te_live(
	"cs_cl_te_live", TempEntPoolLive_f,
	"Every live tempent's POSITION, its attachment client, its owner index and whether "
	"that owner still resolves -- the geometry a pool counter cannot show. "
	"cs_cl_te_live [entity], where the entity prints what an owner index resolves to "
	"through each of the two lookups. cs_cl_te_live bones counts the bone matrices the "
	"live studio tempents leave unwritten when asked as the engine's draw asks (0 is right).",
	FCVAR_CHEAT );

// What a managed spawn helper needs: construct into a slot, then write the
// whole spawn record, so the helpers' bodies live in C# and there is one native
// path for each field.

// Defined in c_te_legacytempents.cpp, where the break-model helpers are
// file-static. See SourceSharpTempEntSpawn_t::BreakHelper.
void SourceSharpTePool_JoinBreakGroup( C_LocalTempEntity *pTemp, bool bSlave );

C_LocalTempEntity *CTempEnts::PoolCreateObject()
{
	return TempEntAlloc();
}

// Constructs into a slot the managed pool has already reserved: stock's
// TempEntAlloc( org, model ) from the memory-pool allocation down, minus the
// list add. Prepare() runs before the origin is set (it zeroes it) and before
// AddToLeafSystem, as stock orders them.
int32_t SourceSharpTePool_CreateWithModel( int32_t nSlot, const float *pOriginXYZ, const model_t *pModel,
										   int32_t nPriority );

int32_t SourceSharp_TempEntPoolCreate( int32_t nSlot, const float *pOriginXYZ, int32_t nModelIndex, int32_t nPriority )
{
	const model_t *pModel = ( nModelIndex != 0 ) ? modelinfo->GetModel( nModelIndex ) : NULL;
	return SourceSharpTePool_CreateWithModel( nSlot, pOriginXYZ, pModel, nPriority );
}

int32_t SourceSharpTePool_CreateWithModel( int32_t nSlot, const float *pOriginXYZ, const model_t *pModel,
										   int32_t nPriority )
{
	if ( !ValidSlot( nSlot ) || !pOriginXYZ )
		return 0;

	// An occupied slot means the managed free list handed out one it had not
	// taken back. Refused: a second object on the row would leak the first.
	if ( g_pTempEntSlots[nSlot] != NULL )
	{
		Warning( "[SourceSharp] TempEntPoolCreate: slot %d is already occupied; "
				 "the managed pool handed out a slot it had not freed\n",
				 nSlot );
		return 0;
	}

	// Stock's `if ( !model )`. Managed helpers check the model index first, so
	// reaching here is a bug worth one line.
	if ( !pModel )
	{
		DevWarning( 1, "Can't create temporary entity with NULL model!\n" );
		return 0;
	}

	C_LocalTempEntity *pTemp = ThePool()->PoolCreateObject();

	if ( !pTemp )
	{
		// The memory pool refused. The slot is left empty, which the managed
		// pool drops on its next pass.
		DevWarning( 1, "Overflow %d temporary ents!\n", (int)CTempEnts::MAX_TEMP_ENTITIES );
		return 0;
	}

	g_pTempEntSlots[nSlot] = pTemp;

	pTemp->Prepare( pModel, gpGlobals->curtime );

	pTemp->priority = nPriority;
	pTemp->SetAbsOrigin( Vector( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] ) );

	pTemp->m_RenderGroup = RENDER_GROUP_OTHER;
	pTemp->AddToLeafSystem( pTemp->m_RenderGroup );

	return 1;
}

// A whole spawn record into the object, every field unconditionally, so a
// recycled slot inherits nothing. Through the setters where tempent.h has them.
int32_t SourceSharp_TempEntPoolWriteSpawn( int32_t nSlot, const SourceSharpTempEntSpawn_t *pSpawn )
{
	if ( !ValidSlot( nSlot ) || !pSpawn )
		return 0;

	C_LocalTempEntity *pTemp = g_pTempEntSlots[nSlot];

	if ( !pTemp )
		return 0;

	pTemp->flags = pSpawn->Flags;
	pTemp->die = pSpawn->Die;

	pTemp->m_flSpriteScale = pSpawn->SpriteScale;
	pTemp->m_flFrame = pSpawn->Frame;
	pTemp->m_flFrameMax = pSpawn->FrameMax;
	pTemp->m_flFrameRate = pSpawn->FrameRate;

	pTemp->m_nBody = pSpawn->Body;
	pTemp->m_nSkin = pSpawn->Skin;

	pTemp->SetRenderMode( (RenderMode_t)pSpawn->RenderMode );
	pTemp->m_nRenderFX = pSpawn->RenderFx;
	pTemp->tempent_renderamt = pSpawn->RenderAmt;
	pTemp->fadeSpeed = pSpawn->FadeSpeed;
	pTemp->SetRenderColor( (byte)pSpawn->ColorR, (byte)pSpawn->ColorG, (byte)pSpawn->ColorB, (byte)pSpawn->ColorA );

	pTemp->SetVelocity( Vector( pSpawn->VelocityXYZ[0], pSpawn->VelocityXYZ[1], pSpawn->VelocityXYZ[2] ) );
	pTemp->SetAcceleration(
		Vector( pSpawn->AccelerationXYZ[0], pSpawn->AccelerationXYZ[1], pSpawn->AccelerationXYZ[2] ) );
	pTemp->m_vecTempEntAngVelocity =
		QAngle( pSpawn->AngularVelocityPYR[0], pSpawn->AngularVelocityPYR[1], pSpawn->AngularVelocityPYR[2] );
	pTemp->SetAbsAngles( QAngle( pSpawn->AnglesPYR[0], pSpawn->AnglesPYR[1], pSpawn->AnglesPYR[2] ) );
	pTemp->bounceFactor = pSpawn->BounceFactor;

	pTemp->x = pSpawn->SineX;
	pTemp->y = pSpawn->SineY;

	pTemp->m_vecNormal = Vector( pSpawn->NormalXYZ[0], pSpawn->NormalXYZ[1], pSpawn->NormalXYZ[2] );

	pTemp->hitSound = pSpawn->HitSound;
	pTemp->clientIndex = (short)pSpawn->ClientIndex;
	pTemp->tentOffset = Vector( pSpawn->TentOffsetXYZ[0], pSpawn->TentOffsetXYZ[1], pSpawn->TentOffsetXYZ[2] );

	if ( pSpawn->BreakHelper != 0 )
		SourceSharpTePool_JoinBreakGroup( pTemp, pSpawn->BreakHelper == 2 );

	// The owner handle is what Frame()'s collision trace ignores. An index
	// arrives, resolved to a handle for either hierarchy. A negative index
	// stores the invalid handle, which means no owner, as stock passed NULL on.
	if ( pSpawn->OwnerEntityIndex >= 0 )
		pTemp->SetTempEntOwnerHandle( SourceSharp_ClientHandleFromIndex( pSpawn->OwnerEntityIndex ) );
	else
		pTemp->SetTempEntOwnerHandle( CBaseHandle() );

	return 1;
}

// The muzzle flashes' engine reads: an attachment, the entity index behind a
// handle, the elight, client.so's rand(), and whether the muzzle materials
// were cached. Every decision is managed's.
static CBaseHandle FxHandle( uint32_t hEntity )
{
	return CBaseHandle( (int)( hEntity & ENT_ENTRY_MASK ), (int)( hEntity >> NUM_SERIAL_NUM_SHIFT_BITS ) );
}

static void PutVectorAngles( float *pOut6, const Vector &v, const QAngle &a )
{
	pOut6[0] = v.x;
	pOut6[1] = v.y;
	pOut6[2] = v.z;
	pOut6[3] = a.x;
	pOut6[4] = a.y;
	pOut6[5] = a.z;
}

int32_t SourceSharp_FxRenderableAttachment( uint32_t hEntity, int32_t nAttachment, float *pOut6 )
{
	IClientRenderable *pRenderable = SourceSharp_ClientRenderableFromHandle( FxHandle( hEntity ) );
	if ( !pRenderable )
		return 0;
	Vector origin( 0, 0, 0 );
	QAngle angles( 0, 0, 0 );
	pRenderable->GetAttachment( nAttachment, origin, angles );
	PutVectorAngles( pOut6, origin, angles );
	return 1;
}

int32_t SourceSharp_FxHandleEntityIndex( uint32_t hEntity )
{
	IClientEntity *pEnt = SourceSharp_ClientEntityFromHandle( FxHandle( hEntity ) );
	return pEnt ? pEnt->entindex() : -1;
}

void SourceSharp_FxElight( int32_t nKey, const float *pOriginXYZ, int32_t r, int32_t g, int32_t b, int32_t nExponent,
						   float flRadius, float flDecay, float flDie )
{
	dlight_t *el = effects->CL_AllocElight( nKey );
	if ( !el )
		return;
	el->origin.Init( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	el->color.r = (byte)r;
	el->color.g = (byte)g;
	el->color.b = (byte)b;
	el->color.exponent = (signed char)nExponent;
	el->radius = flRadius;
	el->decay = flDecay;
	el->die = flDie;
}

void SourceSharp_FxVectorRandom( float flLo, float flHi, float *pOutXYZ )
{
	Vector v;
	v.Random( flLo, flHi );
	pOutXYZ[0] = v.x;
	pOutXYZ[1] = v.y;
	pOutXYZ[2] = v.z;
}

int32_t SourceSharp_FxMaterialsCached()
{
	return ( g_Mat_SMG_Muzzleflash[0] ? 1 : 0 ) | ( g_Mat_Combine_Muzzleflash[0] ? 2 : 0 );
}

// A tempent of a model the engine loads by name (the shell casings), made in a
// slot the managed pool reserved.
int32_t SourceSharp_FxTempEntCreateNamed( int32_t nSlot, const float *pOriginXYZ, const char *pModel,
										  int32_t nPriority )
{
	const model_t *pLoaded = pModel ? engine->LoadModel( pModel ) : NULL;
	if ( !pLoaded )
		return 0;
	return SourceSharpTePool_CreateWithModel( nSlot, pOriginXYZ, pLoaded, nPriority );
}

void SourceSharp_FxLoadModel( const char *pModel )
{
	if ( pModel )
		engine->LoadModel( pModel );
}

// modelinfo->GetModelFrameCount of engine->LoadModel( name ): what a named
// tempent's frame count is (CTempEnts::RocketFlare's), or -1 for no model.
int32_t SourceSharp_FxModelFrameCount( const char *pModel )
{
	const model_t *pLoaded = pModel ? engine->LoadModel( pModel ) : NULL;
	return pLoaded ? modelinfo->GetModelFrameCount( pLoaded ) : -1;
}

int SourceSharpTePool_SlotOf( const C_LocalTempEntity *pTemp )
{
	for ( int i = 0; i < CTempEnts::MAX_TEMP_ENTITIES; i++ )
	{
		if ( pTemp && g_pTempEntSlots[i] == pTemp )
			return i;
	}
	return -1;
}

C_LocalTempEntity *SourceSharpTePool_AtSlot( int nSlot )
{
	return ValidSlot( nSlot ) ? g_pTempEntSlots[nSlot] : NULL;
}
