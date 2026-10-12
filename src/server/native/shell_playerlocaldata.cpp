//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the player's local data =============================//
// SS_REASON f  a bit-level wire codec
//
// MOVED HERE, WHOLE, from game/server/playerlocaldata.cpp by the Phase 4 exit
// lane. game/server/playerlocaldata.cpp is out of server_csharp.vpc; its header
// (game/server/playerlocaldata.h) stays where it is and nothing that includes
// it changed.
//
// WHY IT MOVED. tools/p4-exit-batches: the stock link graph of the server
// project has exactly one CYCLE, and the Phase 4 exit's atomic commit is that
// cycle. playerlocaldata.cpp was NOT in it -- it is a single-TU batch that can
// leave on its own -- and it was owed exactly three symbols, all of them by
// SHIM objects:
//
//     CPlayerLocalData::CPlayerLocalData()                shell_standalone_player.o
//     CPlayerLocalData::UpdateAreaBits(CBasePlayer*, ...) shell_gameclients.o
//     UpdateAllClientData()                               shell_frame.o
//
// (`tools/4a-owes playerlocaldata`, before the move.) Step 4g measured this
// file as UNOWNED by any step in plan_entity.md and as LIVE port code rather
// than residue -- "the shell_server.h include and the managed skybox arm in
// CPlayerLocalData's sky-camera update". Both statements are still true; what
// changed is which vpc row compiles it.
//
// THE MOVE IS A RELOCATION. Every body below is stock's, byte for byte, at the
// commit that moved it. The behaviour change that came NEXT is in its own
// commit and is written at its own site, in UpdateAllClientData.
//
//=============================================================================//

#include "edict.h"
#include "eiface.h"

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;

#include "dt_send.h"
#include "ss_shareddefs.h"   // kHideHudBitCount (shareddefs.h's HIDEHUD_BITCOUNT, oracle-checked)
// sendproxy.h's two property makers this table uses, and the proxy one of them
// defaults to; sendproxy.cpp defines all three. Stock's signatures.
void SendProxy_EHandleToInt( const SendProp *pProp, const void *pStruct, const void *pVarData,
                             DVariant *pOut, int iElement, int objectID );
SendProp SendPropBool( const char *pVarName, int offset, int sizeofVar );
SendProp SendPropEHandle( const char *pVarName, int offset, int sizeofVar = SIZEOF_IGNORE,
                          int flags = 0, SendVarProxyFn proxyFn = SendProxy_EHandleToInt );
#include "ss_playerlocaldata.h"
#include "mathlib/mathlib.h"
#include "shell_server.h"
#include "shell_standalone_player.h"


//=============================================================================

// server.so's own types (ss_playerlocaldata.h, plan_server_novalve.md P5): the
// send table, the save descriptors and the constructor are theirs. The network
// name DT_Local is the string the client's C_PlayerLocalData receives, unchanged.
namespace ss
{

BEGIN_SEND_TABLE_NOBASE( CPlayerLocalData, DT_Local )

	SendPropArray3  (SENDINFO_ARRAY3(m_chAreaBits), SendPropInt(SENDINFO_ARRAY(m_chAreaBits), 8, SPROP_UNSIGNED)),
	SendPropArray3  (SENDINFO_ARRAY3(m_chAreaPortalBits), SendPropInt(SENDINFO_ARRAY(m_chAreaPortalBits), 8, SPROP_UNSIGNED)),
	
	SendPropInt		(SENDINFO(m_iHideHUD), ss::kHideHudBitCount, SPROP_UNSIGNED),
	SendPropFloat	(SENDINFO(m_flFOVRate), 0, SPROP_NOSCALE ),
	SendPropInt		(SENDINFO(m_bDucked),	1, SPROP_UNSIGNED ),
	SendPropInt		(SENDINFO(m_bDucking),	1, SPROP_UNSIGNED ),
	SendPropInt		(SENDINFO(m_bInDuckJump),	1, SPROP_UNSIGNED ),
	SendPropFloat	(SENDINFO(m_flDucktime), 12, SPROP_ROUNDDOWN|SPROP_CHANGES_OFTEN, 0.0f, 2048.0f ),
	SendPropFloat	(SENDINFO(m_flDuckJumpTime), 12, SPROP_ROUNDDOWN, 0.0f, 2048.0f ),
	SendPropFloat	(SENDINFO(m_flJumpTime), 12, SPROP_ROUNDDOWN, 0.0f, 2048.0f ),
#if PREDICTION_ERROR_CHECK_LEVEL > 1 
	SendPropFloat	(SENDINFO(m_flFallVelocity), 32, SPROP_NOSCALE ),

	SendPropFloat		( SENDINFO_VECTORELEM(m_vecPunchAngle, 0), 32, SPROP_NOSCALE|SPROP_CHANGES_OFTEN ),
	SendPropFloat		( SENDINFO_VECTORELEM(m_vecPunchAngle, 1), 32, SPROP_NOSCALE|SPROP_CHANGES_OFTEN ),
	SendPropFloat		( SENDINFO_VECTORELEM(m_vecPunchAngle, 2), 32, SPROP_NOSCALE|SPROP_CHANGES_OFTEN ),

	SendPropFloat		( SENDINFO_VECTORELEM(m_vecPunchAngleVel, 0), 32, SPROP_NOSCALE|SPROP_CHANGES_OFTEN ),
	SendPropFloat		( SENDINFO_VECTORELEM(m_vecPunchAngleVel, 1), 32, SPROP_NOSCALE|SPROP_CHANGES_OFTEN ),
	SendPropFloat		( SENDINFO_VECTORELEM(m_vecPunchAngleVel, 2), 32, SPROP_NOSCALE|SPROP_CHANGES_OFTEN ),

#else
	// misyl: was causing some rare pred errors at 17 bits...
	SendPropFloat	(SENDINFO(m_flFallVelocity), 32, SPROP_NOSCALE | SPROP_CHANGES_OFTEN, -4096.0f, 4096.0f ),
	SendPropVector	(SENDINFO(m_vecPunchAngle),      -1,  SPROP_COORD|SPROP_CHANGES_OFTEN),
	SendPropVector	(SENDINFO(m_vecPunchAngleVel),   -1,  SPROP_COORD| SPROP_CHANGES_OFTEN ),
#endif
	SendPropInt		(SENDINFO(m_bDrawViewmodel), 1, SPROP_UNSIGNED ),
	SendPropInt		(SENDINFO(m_bWearingSuit), 1, SPROP_UNSIGNED ),
	SendPropBool	(SENDINFO(m_bPoisoned)),
	SendPropBool	(SENDINFO(m_bForceLocalPlayerDraw)),

	SendPropFloat	(SENDINFO(m_flStepSize), 16, SPROP_ROUNDUP, 0.0f, 128.0f ),
	SendPropInt		(SENDINFO(m_bAllowAutoMovement),1, SPROP_UNSIGNED ),

	// 3d skybox data
	SendPropInt(SENDINFO_STRUCTELEM(m_skybox3d.scale), 12),
	SendPropVector	(SENDINFO_STRUCTELEM(m_skybox3d.origin),      -1,  SPROP_COORD),
	SendPropInt	(SENDINFO_STRUCTELEM(m_skybox3d.area),	8, SPROP_UNSIGNED ),
	SendPropInt( SENDINFO_STRUCTELEM( m_skybox3d.fog.enable ), 1, SPROP_UNSIGNED ),
	SendPropInt( SENDINFO_STRUCTELEM( m_skybox3d.fog.blend ), 1, SPROP_UNSIGNED ),
	SendPropInt( SENDINFO_STRUCTELEM( m_skybox3d.fog.radial ), 1, SPROP_UNSIGNED ),
	SendPropVector( SENDINFO_STRUCTELEM(m_skybox3d.fog.dirPrimary), -1, SPROP_COORD),
	SendPropInt( SENDINFO_STRUCTELEM( m_skybox3d.fog.colorPrimary ), 32, SPROP_UNSIGNED ),
	SendPropInt( SENDINFO_STRUCTELEM( m_skybox3d.fog.colorSecondary ), 32, SPROP_UNSIGNED ),
	SendPropFloat( SENDINFO_STRUCTELEM( m_skybox3d.fog.start ), 0, SPROP_NOSCALE ),
	SendPropFloat( SENDINFO_STRUCTELEM( m_skybox3d.fog.end ), 0, SPROP_NOSCALE ),
	SendPropFloat( SENDINFO_STRUCTELEM( m_skybox3d.fog.maxdensity ), 0, SPROP_NOSCALE ),

	SendPropEHandle( SENDINFO_STRUCTELEM( m_PlayerFog.m_hCtrl ) ),

	// audio data
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 0 ), -1, SPROP_COORD),
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 1 ), -1, SPROP_COORD),
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 2 ), -1, SPROP_COORD),
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 3 ), -1, SPROP_COORD),
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 4 ), -1, SPROP_COORD),
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 5 ), -1, SPROP_COORD),
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 6 ), -1, SPROP_COORD),
	SendPropVector( SENDINFO_STRUCTARRAYELEM( m_audio.localSound, 7 ), -1, SPROP_COORD),
	SendPropInt( SENDINFO_STRUCTELEM( m_audio.soundscapeIndex ), 17, 0 ),
	SendPropInt( SENDINFO_STRUCTELEM( m_audio.localBits ), SS_NUM_AUDIO_LOCAL_SOUNDS, SPROP_UNSIGNED ),
	SendPropInt( SENDINFO_STRUCTELEM( m_audio.entIndex ) ),

	SendPropString( SENDINFO( m_szScriptOverlayMaterial ) ),
END_SEND_TABLE()

BEGIN_SIMPLE_DATADESC( fogplayerparams_t )
	DEFINE_FIELD( m_hCtrl, FIELD_EHANDLE ),
	DEFINE_FIELD( m_flTransitionTime, FIELD_FLOAT ),
	DEFINE_FIELD( m_OldColor, FIELD_COLOR32 ),
	DEFINE_FIELD( m_flOldStart, FIELD_FLOAT ),
	DEFINE_FIELD( m_flOldEnd, FIELD_FLOAT ),
	DEFINE_FIELD( m_NewColor, FIELD_COLOR32 ),
	DEFINE_FIELD( m_flNewStart, FIELD_FLOAT ),
	DEFINE_FIELD( m_flNewEnd, FIELD_FLOAT ),
END_DATADESC()

BEGIN_SIMPLE_DATADESC( fogparams_t )

	DEFINE_FIELD( enable, FIELD_BOOLEAN ),
	DEFINE_FIELD( blend, FIELD_BOOLEAN ),
	DEFINE_FIELD( dirPrimary, FIELD_VECTOR ),
	DEFINE_FIELD( colorPrimary, FIELD_COLOR32 ),
	DEFINE_FIELD( colorSecondary, FIELD_COLOR32 ),
	DEFINE_FIELD( start, FIELD_FLOAT ),
	DEFINE_FIELD( end, FIELD_FLOAT ),
	DEFINE_FIELD( farz, FIELD_FLOAT ),
	DEFINE_FIELD( maxdensity, FIELD_FLOAT ),
	DEFINE_FIELD( colorPrimaryLerpTo, FIELD_COLOR32 ),
	DEFINE_FIELD( colorSecondaryLerpTo, FIELD_COLOR32 ),
	DEFINE_FIELD( startLerpTo, FIELD_FLOAT ),
	DEFINE_FIELD( endLerpTo, FIELD_FLOAT ),
	DEFINE_FIELD( lerptime, FIELD_TIME ),
	DEFINE_FIELD( duration, FIELD_FLOAT ),
	DEFINE_FIELD( radial, FIELD_BOOLEAN ),
END_DATADESC()

BEGIN_SIMPLE_DATADESC( sky3dparams_t )

	DEFINE_FIELD( scale, FIELD_INTEGER ),
	DEFINE_FIELD( origin, FIELD_VECTOR ),
	DEFINE_FIELD( area, FIELD_INTEGER ),
	DEFINE_EMBEDDED( fog ),

END_DATADESC()

BEGIN_SIMPLE_DATADESC( audioparams_t )

	DEFINE_AUTO_ARRAY( localSound, FIELD_VECTOR ),
	DEFINE_FIELD( soundscapeIndex, FIELD_INTEGER ),
	DEFINE_FIELD( localBits, FIELD_INTEGER ),
	DEFINE_FIELD( entIndex, FIELD_INTEGER ),

END_DATADESC()

BEGIN_SIMPLE_DATADESC( CPlayerLocalData )
	DEFINE_AUTO_ARRAY( m_chAreaBits, FIELD_CHARACTER ),
	DEFINE_AUTO_ARRAY( m_chAreaPortalBits, FIELD_CHARACTER ),
	DEFINE_FIELD( m_iHideHUD, FIELD_INTEGER ),
	DEFINE_FIELD( m_flFOVRate, FIELD_FLOAT ),
	DEFINE_FIELD( m_vecOverViewpoint, FIELD_VECTOR ),
	DEFINE_FIELD( m_bDucked, FIELD_BOOLEAN ),
	DEFINE_FIELD( m_bDucking, FIELD_BOOLEAN ),
	DEFINE_FIELD( m_bInDuckJump, FIELD_BOOLEAN ),
	DEFINE_FIELD( m_flDucktime, FIELD_TIME ),
	DEFINE_FIELD( m_flDuckJumpTime, FIELD_TIME ),
	DEFINE_FIELD( m_flJumpTime, FIELD_TIME ),
	DEFINE_FIELD( m_nStepside, FIELD_INTEGER ),
	DEFINE_FIELD( m_flFallVelocity, FIELD_FLOAT ),
	DEFINE_FIELD( m_nOldButtons, FIELD_INTEGER ),
	DEFINE_FIELD( m_flOldForwardMove, FIELD_FLOAT ),
	DEFINE_FIELD( m_vecPunchAngle, FIELD_VECTOR ),
	DEFINE_FIELD( m_vecPunchAngleVel, FIELD_VECTOR ),
	DEFINE_FIELD( m_bDrawViewmodel, FIELD_BOOLEAN ),
	DEFINE_FIELD( m_bWearingSuit, FIELD_BOOLEAN ),
	DEFINE_FIELD( m_bPoisoned, FIELD_BOOLEAN ),
	DEFINE_FIELD( m_bForceLocalPlayerDraw, FIELD_BOOLEAN ),
	DEFINE_FIELD( m_flStepSize, FIELD_FLOAT ),
	DEFINE_FIELD( m_bAllowAutoMovement, FIELD_BOOLEAN ),
	DEFINE_EMBEDDED( m_skybox3d ),
	DEFINE_EMBEDDED( m_PlayerFog ),
	DEFINE_EMBEDDED( m_fog ),
	DEFINE_EMBEDDED( m_audio ),
	
	// "Why don't we save this field, grandpa?"
	//
	// "You see Billy, trigger_vphysics_motion uses vPhysics to touch the player,
	// so if the trigger is Disabled via an input while the player is inside it,
	// the trigger won't get its EndTouch until the player moves. Since touchlinks
	// aren't saved and restored, if the we save before EndTouch is called, it
	// will never be called after we load."
	//DEFINE_FIELD( m_bSlowMovement, FIELD_BOOLEAN ),
	
END_DATADESC()

//-----------------------------------------------------------------------------
// Purpose: Constructor.
//-----------------------------------------------------------------------------
CPlayerLocalData::CPlayerLocalData()
{
#ifdef _DEBUG
	m_vecOverViewpoint.Init();
	m_vecPunchAngle.Init();
#endif
	m_audio.soundscapeIndex = 0;
	m_audio.localBits = 0;
	m_audio.entIndex = 0;
	m_pOldSkyCamera = NULL;
	m_bDrawViewmodel = true;

	m_szScriptOverlayMaterial.GetForModify()[0] = '\0';
}

} // namespace ss


// CPlayerLocalData::UpdateAreaBits(CBasePlayer *, ...) IS GONE (plan_server_novalve.md
// P5 wave 8b): its one caller was shell_gameclients.cpp, which computes the area
// bits itself for the standalone player, and no CBasePlayer exists to pass it.

//-----------------------------------------------------------------------------
// ClientData_Update IS GONE -- plan_shim.md 5 3a, lane 3b-enginebind.
//
// Stock's "fill in CClientData values for local player just before sending over
// wire" (gameinterface.cpp), and it took a CBasePlayer *. Its ONLY caller in
// the tree was UpdateAllClientData below, from an arm guarded by a hardcoded
// NULL -- so it had zero reachable callers and every line of it was dead.
//
// WHAT IT DID, so that nobody has to read a merge commit to find out: it asked
// GetCurrentSkyCamera(), and on a NULL answer -- which is ALWAYS, because
// sky_camera is a C# class and is not in the list that walk walks -- copied the
// managed skybox parameters in through SourceSharp_FillSkyboxParams and
// returned. The two stock arms after it (adopt a changed native sky camera,
// else set area 255) were unreachable behind that early return even on a tree
// where the function itself was reachable.
//
// NONE OF THAT IS LOST. StandaloneClientData_Update below is the live twin,
// reached from UpdateAllClientData's CStandalonePlayer::FromIndex arm, and it
// is where SourceSharp_FillSkyboxParams is called from today.
//-----------------------------------------------------------------------------


//-----------------------------------------------------------------------------
// THE STANDALONE HIERARCHY'S ClientData_Update, AND THE BUG IT FIXES.
//
// ClientData_Update above takes a CBasePlayer *. UpdateAllClientData found one
// with UTIL_PlayerByIndex, and in this build UTIL_PlayerByIndex CANNOT answer:
// its helper UTIL_SDKPlayerFromEdict returns NULL unconditionally under
// SOURCESHARP, and says so in its own comment ("Output: NULL, always, in this
// build -- nothing here constructs a CBasePlayer"). So the loop below ran
// gpGlobals->maxClients times and its BODY never ran once.
//
// Two things went with it, and neither was visible to a compiler, to `make
// check`, or to any live gate this project has:
//
//   1. THE 3D SKYBOX NEVER REACHED A CLIENT. sky_camera is a C# class, so the
//      managed side pushes its parameters to SourceSharp_SetSkyboxParams and
//      SourceSharp_FillSkyboxParams hands them back -- and ClientData_Update,
//      guarded arm and all, is that function's ONLY caller in the tree. The
//      whole arm was dead at the reading end.
//
//   2. m_skybox3d.area STAYED 0 INSTEAD OF 255. Stock's `else if
//      ( !pSkyCamera )` sets 255, which is its "there is no 3d skybox" value;
//      the field is otherwise never written, so it held its constructed 0.
//      shell_transmitsteps.cpp READS it
//      (SourceSharp_EntitiesStandalonePlayerSkyboxArea -> CheckTransmit), and
//      that file's own comment says why the difference matters: "area 0 is a
//      real visleaf area ... CheckTransmit RETURNS EARLY for one of them and
//      walks 473 edicts for the other."
//
// This is the fifth or sixth instance of the recorded shape -- something a base
// class did FOR an entity, lost because the managed entity is not on that
// hierarchy. Here what the base did was BE FOUND BY THE WALK.
//
// dm_lockdown has no sky_camera, which is why no live gate could see (1);
// (2) is on every map.
//
// The stock arm is untouched and is still tried FIRST, so a native CBasePlayer
// -- were one ever linked again -- still wins, exactly as the guarded arm
// inside ClientData_Update is ordered.
//-----------------------------------------------------------------------------
static void StandaloneClientData_Update( CStandalonePlayer *pPlayer )
{
	MvLocalData_t &local = pPlayer->MvLocal();

	// GetCurrentSkyCamera() walks the SDK's own list, which a C# sky_camera is
	// not in, so it is NULL for this hierarchy by construction rather than by
	// accident. That is the same premise ClientData_Update's guarded arm states
	// and the reason it is only consulted when the list is empty.
	ss::sky3dparams_t managed;
	if ( SourceSharp_FillSkyboxParams( managed ) )
	{
		local.m_skybox3d.CopyFrom( managed );
		return;
	}

	// Stock's no-sky-camera answer, and NOT the constructed 0.
	local.m_skybox3d.area = 255;
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void UpdateAllClientData( void )
{

	// THE SDK ARM IS GONE, AND SO IS THE FUNCTION IT WAS THE ONLY CALLER OF --
	// plan_shim.md 5 3a, lane 3b-enginebind. It was
	//
	//     CBasePlayer *pl;
	//     ...
	//     pl = NULL;                       <-- a HARDCODED NULL, not a lookup
	//     if ( pl ) { ClientData_Update( pl ); continue; }
	//
	// standing in for stock's `pl = UTIL_PlayerByIndex( i );`, which util.h
	// says answers NULL by construction: server_csharp.vpc evicts player.cpp
	// and its whole hierarchy, so `nm -C server.so` finds no CBasePlayer
	// vtable at all. A literal NULL assigned and then tested is a guard that a
	// reader has to disprove before trusting the arm under it, and this one
	// had been unreachable long enough that the comment explaining it was
	// four times the length of the code.
	//
	// ClientData_Update WENT WITH IT. This was its only caller anywhere, so
	// with the arm gone the function had none -- see the note where it stood.
	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		// A slot holds one kind of player or the other. Asked of the slot table
		// rather than of the edict's entity, for the reason
		// UTIL_SDKPlayerFromEdict gives: FromIndex() is a flat array written at
		// ClientPutInServer and never consults GetBaseEntity().
		if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromIndex( i ) )
			StandaloneClientData_Update( pStandalone );
	}
}

