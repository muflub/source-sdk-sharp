//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON b  an interface resolved by name at load
//
// IServerGameClients -- EVERYTHING THE ENGINE ASKS THE GAME ABOUT ONE PLAYER.
//
// plan_gamedll_after.md Phase A9 step 2. Carried out of
// game/server/gameinterface.cpp:2281-3066, which this phase deletes: the whole
// of CServerGameClients' twenty bodies, its singleton, and its THREE EXPOSE_
// lines -- 003, 004 and the current version string, all three pointing at the
// same object, which is how a game DLL stays loadable by three engine
// vintages.
//
// THE CLASS DECLARATION IS IN shell_gameclients.h, beside this file, since
// plan_entity.md step 4g. It used to be in stock's gameinterface.h and A9 left
// it there on a named ground -- "four mod-specific gameinterface.cpp files out
// of the build define their own GetPlayerLimits against the same declaration...
// moving the declaration would only decide something this phase was not asked
// to decide". 4g is asked. The four (base_gameinterface.cpp,
// hl2mp_gameinterface.cpp, sdk_gameinterface.cpp, tf_gameinterface.cpp) are
// none of them in server_csharp.vpc, and every compiled reader of
// gameinterface.h is a shim file -- so what stayed behind was a shim
// declaration sitting in a stock header. shell_server.cpp still defines
// GetPlayerLimits and includes the shim header to reach the class.
//
// WHAT IS NOT A VERBATIM CARRY -- two file-scope statics, both now fields of
// the singleton, because tools/statics-audit gates that shape out of
// src/sourcesharp and the owner rule behind it says state goes in a class:
//
//   static ClientPutInServerOverrideFn g_pClientPutInServerOverride
//       -> m_pClientPutInServerOverride, the bot hook, read only by
//          ClientPutInServer below. GONE: its setters had no caller (server gc
//          cull, 2026-10-01), so it was always NULL, and the field, its typedef
//          and the branch that read it went in the dead-state sweep after.
//
//   static ConVar sv_max_usercmd_move_magnitude
//       -> became m_svMaxUsercmdMoveMagnitude here, and has since LEFT NATIVE
//          ALTOGETHER: SourceSharp.Server/ConVars.cs declares it and this file
//          reads it by name through CManagedConVarRef. plan_shim.md's rule is
//          that a natively declared convar must be engine-transmitted, and this
//          one's flags were 0 -- not replicated, not read by the engine, a
//          server-side clamp and therefore policy. The clamp itself is still
//          below, inside the usercmd codec, which is mechanism.
//
//===========================================================================//

// THE R5 SET (plan_server_novalve.md P5): cbase.h, gameinterface.h, client.h,
// entitylist.h, eventqueue.h, player_voice_listener.h, usermessages.h,
// in_buttons.h, the AI headers, querycache.h and soundenvelope.h went. What the
// file used of them is declared below, from server.so's own definitions.
#include "eiface.h"
#include "ss_entityhandle.h"      // ss::GameEntityFromEntityHandle
// plan_entity.md step 4g: the class declaration is the shim's own now, beside
// the file that implements it, rather than in stock's gameinterface.h.
#include "shell_gameclients.h"
#include "igamerules.h"
// plan_entity.md step 4g: the area-portal list is no longer typed to an
// entity, so this walk no longer reaches an SDK header for it. The record
// and the list are the shim's own.
#include "areaportal_list.h"
#include "ivoiceserver.h"
#include "vstdlib/random.h"
// hltvdirector.h was included here and nothing in this file named anything from
// it (CHLTVDirector, HLTVDirector(), HLTVDirectorSystem()). Removed at
// plan_shim.md step 5c -- HLTV is dropped and a dead include is the way a
// deleted feature quietly keeps a header compiling.
#include "engine/IEngineSound.h"
#include "ss_soundent.h"
#include "physicslink.h"
#include "datacache/imdlcache.h"

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;
extern IMDLCache *mdlcache;
void ClientActive( edict_t *pEdict, bool bLoadGame );                        // shell_server.cpp
void ClientPutInServer( edict_t *pEdict, const char *playername );           // shell_server.cpp
void UTIL_SetClientVisibilityPVS( edict_t *pClient, const unsigned char *pvs, int pvssize );   // server_util_library.cpp
void UTIL_SetCommandClientIndex( int index );                                // shell_commandclient.cpp

// sv_max_usercmd_move_magnitude is declared in managed code now; this is how
// ProcessUsercmds still reads it. See the note above the singleton's ctor.
#include "managed_convar_ref.h"

// SourceSharp_EarPositionForClient -- ClientEarPosition's hierarchy question,
// asked of C#. plan_server.md Phase 7, plan-audit C5.
#include "shell_server.h"

// SourceSharp_PlayerStateForEdict and SourceSharp_ClientViewOrigin -- step 3c.
// GetPlayerState's storage and the once-per-client-per-frame view origin that
// ClientSetupVisibility and ClientEarPosition share.
#include "shell_playerinfo.h"

// THE LAST OF THOSE FORWARD DECLARATIONS IS GONE -- plan_shim.md 4a item 11,
// lane 5a-physresidue. It was
//
//     void SceneManager_ClientActive( CBasePlayer *player );
//
// one of the declarations gameinterface.cpp made in its own file scope rather
// than in a header. Its call went from ClientActive below with the rest of
// stock's tail (lane 3b-enginebind); the DECLARATION outlived the call, which
// is exactly the shape entity_base_husks_test warns about -- a declaration
// survives the deletion of the call and makes a name-shaped absence check pass
// for the wrong reason. The definition in shell_absent.cpp went in the same
// commit as this line, on 0 importers over both link lines.

// TWO OF THESE ARE GONE AND THEY WENT AS A PAIR, plan_shim.md §4a item 7.
//
//   extern IToolFrameworkServer *g_pToolFrameworkServer;
//   #include "toolframework/iserverenginetools.h"
//
// PreSetupVisibility was the only thing in this file that ever named the
// pointer, and it stopped being called when Phase 1b deleted the shell that
// stood in for the framework (see ClientSetupVisibility, below, for why
// nothing changed when it did). The include existed only to give the extern a
// type, so neither has a reader and they are a closed pair -- removing one
// alone is what leaves a dangling declaration.
//
// AND THE EXTERN HAD NO DEFINITION ON THE LINK LINE EITHER. The symbol is
// defined in game/server/toolframework_server.cpp, which server_csharp.vpc
// does not list, so this declaration was a promise about a symbol that is not
// in the build. Removing it retires a latent link error rather than risking
// one.
//
//   IGameEntity *FindPickerEntity( CBasePlayer *pPlayer );
//
// The third is gone for a plainer reason: this file declared it and never
// called it. Every caller in the tree is in game/server/, and each of those
// re-declares it locally.

#ifndef NO_STEAM
#endif

// The standalone player (Phase 9a): several of the per-client asks below grow a
// small arm for a player whose edict resolves to no CBaseEntity.
#include "shell_standalone_player.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E17: the usercmd decode is IManagedPlayerCommands
#include "inetchannel.h"
#include "ss_client_message.h"  // a client's net message to the server

// ShellSave_LoadGameNeedsPlayer -- the gate ClientActive's savegame arm asks,
// kept in the engine-free header so a gtest can hold it. Stock's side of the
// argument is there too.
#include "shell_savegame.h"

// plan_entity.md Phase 0 step 0e -- the engine-boundary probe. Every virtual
// the engine calls on this interface is armed with a counter that records the
// caller's return address; ss_boundary_probe_report (shell_gameents.cpp)
// prints them. GetPlayerState is the one this step was written for.
#include "shell_boundary_probe.h"
#include "shell_factory.h"
#include "ss_string.h"


// EndRestoreEntities is still gameinterface.cpp's, with the rest of the restore
// trio and stock's CServerGameDLL. ClientActive below is its ONE caller in the
// whole tree, which is why `nm` reported it as having none: caller and callee
// were in one translation unit and the reference never left the object.
void EndRestoreEntities();

// The two fields that used to be file-scope statics beside these bodies. See
// the file header for why they are fields; gameinterface.h declares them.
//-----------------------------------------------------------------------------
// sv_max_usercmd_move_magnitude is C#'s (SourceSharp.Server/ConVars.cs) and,
// since plan_server_novalve.md P2.2, so is the clamp: it sits inside the
// usercmd decode, which moved to C# whole. Nothing here reads it any more.
//-----------------------------------------------------------------------------

namespace
{
	CServerGameClients s_ServerGameClients;
}
// INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_3 is compatible with the latest since we're only adding things to the end, so expose that as well.
// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetServerGameClients003() { return (IServerGameClients003 *)&s_ServerGameClients; }
const SourceSharpExposed kExposeServerGameClients003 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_3, SourceSharp_GetServerGameClients003 };
static void *SourceSharp_GetServerGameClients004() { return (IServerGameClients004 *)&s_ServerGameClients; }
const SourceSharpExposed kExposeServerGameClients004 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_4, SourceSharp_GetServerGameClients004 };
static void *SourceSharp_GetServerGameClients() { return (IServerGameClients *)&s_ServerGameClients; }
const SourceSharpExposed kExposeServerGameClients = { INTERFACEVERSION_SERVERGAMECLIENTS, SourceSharp_GetServerGameClients };

//-----------------------------------------------------------------------------
// Purpose: called when a player tries to connect to the server
// Input  : *pEdict - the new player
//			char *pszName - the players name
//			char *pszAddress - the IP address of the player
//			reject - output - fill in with the reason why
//			maxrejectlen -- sizeof output buffer
//			the player was not allowed to connect.
// Output : Returns TRUE if player is allowed to join, FALSE if connection is denied.
//-----------------------------------------------------------------------------
bool CServerGameClients::ClientConnect( edict_t *pEdict, const char *pszName, const char *pszAddress, char *reject, int maxrejectlen )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientConnect );
	
	if ( !g_pGameRules )
		return false;
	
	return g_pGameRules->ClientConnected( pEdict, pszName, pszAddress, reject, maxrejectlen );
}

//-----------------------------------------------------------------------------
// A CLIENT'S NET MESSAGE TO THE SERVER (ss_client_message.h): one
// CSourceSharpClientMessage registered on each client's net channel when the
// client goes active, and what arrives handed to managed code
// (IManagedClients::OnClientNetMessage), which decides what it means. A
// channel lives for the connection, so a level change finds the message
// registered and keeps it. Here, not in a TU of its own: bitbuf.h brings
// mathlib's static vectors, and this TU already has them.
//-----------------------------------------------------------------------------
namespace
{

// Process's answer is the engine's: false drops the client. A message nobody
// claims, or no managed side, is not the client's fault.
bool HandClientMessageToManaged( CSourceSharpClientMessage &msg )
{
	if ( IManagedClients *pApi = ManagedClients() )
		pApi->OnClientNetMessage( msg.Owner(), msg.Key(), msg.Data(), msg.DataLength() );
	return true;
}

void RegisterClientMessage( int nEntIndex )
{
	// A bot has no channel.
	INetChannel *pChannel = nEntIndex > 0 ? static_cast< INetChannel * >( engine->GetPlayerNetInfo( nEntIndex ) ) : NULL;
	if ( !pChannel )
		return;

	// The channel keeps it (RegisterMessage stores the pointer), so it is on
	// the heap; a channel that has one already refuses a second.
	CSourceSharpClientMessage *pMsg = new CSourceSharpClientMessage( HandClientMessageToManaged );
	pMsg->SetOwner( nEntIndex );
	if ( !pChannel->RegisterMessage( pMsg ) )
		delete pMsg;
}

}

//-----------------------------------------------------------------------------
// Purpose: Called when a player is fully active (i.e. ready to receive messages)
// Input  : *pEntity - the player
//-----------------------------------------------------------------------------
void CServerGameClients::ClientActive( edict_t *pEdict, bool bLoadGame )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientActive );

	MDLCACHE_CRITICAL_SECTION();

	// THE PLAYER THE ENGINE NEVER ASKED FOR.
	//
	// On a savegame load the engine does not call ClientPutInServer at all --
	// it calls only ClientActive, with bLoadGame true. shell_savegame.h
	// carries stock's side of it; the short version is that stock's player
	// comes out of the save's entity table inside Restore, and this port
	// writes no entity table, so
	// nothing has created one by the time this is reached.
	//
	// WHAT THAT COST BEFORE THIS ARM, measured on dm_lockdown: a load left the
	// world standing with no player in it, the client had no viewpoint so the
	// client-side registry came back with nothing to compare (section 7f), and
	// a SECOND save failed with the engine's own "Can't savegame without a
	// player!" -- GetPlayerState answering NULL for an edict with no player.
	//
	// THE NAME comes from the client's own "name" convar, which is where the
	// engine got the string it would have passed to ClientPutInServer. An
	// empty answer is passed through rather than substituted: SetPlayerName is
	// the one consumer and a wrong name is worse than a blank one.
	//
	// NOT FATAL HERE, unlike ::ClientPutInServer's Error(). That call site is
	// a fresh connect, where a missing player class is a load-time invariant
	// gone wrong and the server is better dead than half up. This one is a
	// recovery from an engine call that did not happen, and the failure it has
	// to survive is a save whose player cannot be rebuilt -- in which case the
	// map is still standing and playable, which is the same choice
	// MapLoadSaveGame makes when the restore itself fails.
	if ( ShellSave_LoadGameNeedsPlayer( bLoadGame, CStandalonePlayer::FromEdict( pEdict ) != NULL ) )
	{
		const int nIndex = pEdict ? engine->IndexOfEdict( pEdict ) : 0;
		const char *pszName = nIndex > 0 ? engine->GetClientConVarValue( nIndex, "name" ) : NULL;

		Msg( "[SourceSharp][save] ClientActive( bLoadGame=1 ) -- the engine skipped "
		     "ClientPutInServer, so no player exists on edict %d. Creating it here.\n",
		     nIndex );

		if ( !StandalonePlayer_Create( pEdict, pszName ? pszName : "" ) )
		{
			Warning( "[SourceSharp][save] the standalone player could NOT be created on "
			         "edict %d. This load has no player: the map is standing, but nothing "
			         "can be saved from it and the client has no viewpoint.\n", nIndex );
		}
	}

	// Before the managed side hears of the client: from here on it may ask the
	// client something, and the answer comes back as this message.
	RegisterClientMessage( pEdict ? engine->IndexOfEdict( pEdict ) : 0 );

	::ClientActive( pEdict, bLoadGame );

	// If we just loaded from a save file, call OnRestore on valid entities
	EndRestoreEntities();

	if ( gpGlobals->eLoadType != MapLoad_LoadGame )
	{
		// notify all entities that the player is now in the game
	}

	// STOCK'S TAIL IS GONE, AND IT IS THE ONE PLACE IN THIS PASS WHERE THE
	// RULING SAID "DEAD" AND THE MEASUREMENT SAID "EXECUTES" -- plan_shim.md
	// \u00a75 3a, lane 3b-enginebind. It was:
	//
	//     CBasePlayer *pPlayer = UTIL_SDKPlayerFromEdict( pEdict );
	//     CSoundEnvelopeController::GetController().CheckLoopingSoundsForPlayer( pPlayer );
	//     SceneManager_ClientActive( pPlayer );
	//     #if defined( TF_DLL ) ... #endif
	//
	// pPlayer is NULL on every path -- UTIL_SDKPlayerFromEdict's every return
	// statement is `return NULL` -- but the two calls were NOT unreachable.
	// They RAN, once per client, with a NULL argument, which is a different
	// claim and needs a different proof. Both were checked at the callee:
	//
	//   CheckLoopingSoundsForPlayer  shell_soundenvelope.cpp, first line is
	//                                `if ( !pPlayer ) return;`
	//   SceneManager_ClientActive    shell_ai_absent.cpp, an empty stub --
	//                                there are no scenes in this build
	//
	// So both are provable no-ops for the only argument they can be given, and
	// deleting them is observably identical rather than merely unreachable.
	// The standalone player's own looping sounds are the managed side's, not
	// this list's.
	//
	// The TF_DLL block went for a third reason again: TF_DLL is defined in no
	// vpc script this build reads, so the preprocessor already removed it and
	// what is deleted here is source, not behaviour.
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *pPlayer - the player
//-----------------------------------------------------------------------------
void CServerGameClients::ClientSpawned( edict_t *pPlayer )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientSpawned );

	if ( g_pGameRules )
	{
		g_pGameRules->ClientSpawned( pPlayer );
	}
}

//-----------------------------------------------------------------------------
// Purpose: called when a player disconnects from a server
// Input  : *pEdict - the player
//-----------------------------------------------------------------------------
void CServerGameClients::ClientDisconnect( edict_t *pEdict )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientDisconnect );

	// The whole CBasePlayer body below is guarded on the resolve, so a
	// standalone player used to leave SILENTLY: entity standing, edict still
	// hooked, managed side never told. Destroying the shell is the rest of the
	// cleanup here — its destructor unhooks the edict without removing it (the
	// engine owns a client's edict).
	//
	// AND THE ANNOUNCEMENT IS BACK, EXPLICITLY, because the sentence above used
	// to end "and tells managed code it is gone" and that stopped being true.
	// plan_entity.md step 2c moved the removal announcement out of
	// ~CStandaloneEntity and into EntityDirectory's free pass, which tells the
	// entity, writes the liveness record and unbinds the row before asking for
	// the shell. That pass runs for every removal MANAGED starts. This is the
	// one removal NATIVE starts, so it reached none of it, and the comment went
	// on claiming otherwise.
	//
	// WHAT IT COST, measured on two live runs of tools/live-gate on dm_lockdown
	// after a second client joined and left:
	//
	//   reload: ss_player '' (281477122) could not be rebuilt (PlayerNetState
	//     needs 692 byte(s) and entity 2 ('ss_player') offers 0)
	//   reload: 480 of 481 entit(ies) restored; 1 FAILED to rebuild, first
	//     'ss_player'
	//
	// -- the departed player's managed instance still in the registry, carried
	// into the hot reload's records, and unrebuildable because the entity it
	// names is gone. Also: LivenessMirror answering "alive" about a freed
	// entity, which is the direction that record must never fail in, and
	// SsPlayer.OnRemoved never running, so PlayerCommandQueue kept the leaver's
	// queue until its 256-batch staleness sweep found it.
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict ) )
	{
		if ( g_pGameRules )
			g_pGameRules->ClientDisconnected( pEdict );

		// SINCE THIS CLIENT ISN'T AROUND TO THINK ANY MORE, RESET THEIR SOUND
		// -- stock's comment, and THIS IS THE FIRST BUILD IN WHICH IT ACTUALLY
		// RUNS. The call used to sit in the SDK arm below, inside
		// `if ( player )` where player came from UTIL_SDKPlayerFromEdict and is
		// NULL on every path -- so no departing player's sound has ever been
		// reset on the shell path, and the dead arm was what made that look
		// handled.
		//
		// IT TAKES AN EDICT, NOT A PLAYER, which is why it can move here
		// unchanged: ss::SoundEnt_ClientSoundIndex( pEdict ) indexes the pool by
		// client slot and never touches a CBasePlayer. The index rather than a
		// CSound * is plan_gamedll_after.md Phase A3's shape -- the pool is
		// SourceSharp.Server's SoundEnt now and handing a raw pointer into a
		// managed array is the thing a port must not do. An index outside the
		// pool is silently nothing on the other side, which is exactly what the
		// NULL test on the old pSound meant.
		ss::SoundEnt_ResetClientSound( ss::SoundEnt_ClientSoundIndex( pEdict ) );

		// BEFORE the delete: the announcement carries the handle and the blob,
		// and both are memory the next line frees.
		SourceSharp_AnnounceStandaloneRemoved( pStandalone );

		delete pStandalone;
		return;
	}

	// STOCK'S SDK ARM IS GONE -- plan_shim.md \u00a75 3a, lane 3b-enginebind. It was
	// `CBasePlayer *player = UTIL_SDKPlayerFromEdict( pEdict ); if ( player )`
	// wrapping some seventy lines: SetMaxSpeed( 0 ), CSoundEnt::ResetClientSound,
	// three MvRemoveFlag/MvAddFlag calls, g_pGameRules->ClientDisconnected,
	// PhysicsLink_RemoveTouchedList and _RemoveGroundList, and a TF_DLL Steam
	// block.
	//
	// ONE OF THOSE WAS A LIVE OBLIGATION AND IT MOVED UP RATHER THAN OUT:
	// CSoundEnt::ResetClientSound is now the third statement of the standalone
	// arm above. It is keyed by edict, so it was never the SDK player's to
	// begin with -- the dead arm was simply where stock had put it, and being
	// unreachable is what made "the disconnect path resets the sound" read as
	// true while being false of every client that has ever left this server.
	//
	// TWO ARE LEFT DELIBERATELY UNANSWERED AND ARE FLAGGED RATHER THAN GUESSED:
	// PhysicsLink_RemoveTouchedList and PhysicsLink_RemoveGroundList. Both take
	// the entity, not the edict, so neither moves across unchanged, and whether
	// ~CStandalonePlayer already unlinks the touch and ground lists is a
	// question about that destructor rather than about this function. It is
	// recorded in the lane report; the wrong fix here is a second unlink.
	//
	// EVERY ONE OF THOSE WAS UNREACHABLE, and the proof is exhaustive rather
	// than sampled: UTIL_SDKPlayerFromEdict has exactly one definition on the
	// link line (shell_playerslots.o) and all three of its return statements
	// are `return NULL`. There is no path to a non-NULL CBasePlayer, so the
	// guard can never be taken. A live counter would have measured only the
	// paths a run happened to take; this covers all of them.
	//
	// WHAT THE ENGINE STILL GETS IS THE ARM ABOVE, which is the live one:
	// CStandalonePlayer::FromEdict claims the edict, tells the rules, announces
	// the removal and deletes the shell, then returns. g_pGameRules->
	// ClientDisconnected is called from THERE, so the rules are not losing the
	// notification -- only the second, unreachable copy of it is gone.
}

void CServerGameClients::ClientPutInServer( edict_t *pEntity, const char *playername )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientPutInServer );

	::ClientPutInServer( pEntity, playername );
}

void CServerGameClients::ClientCommand( edict_t *pEntity, const CCommand &args )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientCommand );

	// A standalone client's console commands used to land on a NULL player
	// and be swallowed one line below. The handled set lives with the player.
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEntity ) )
	{
		StandalonePlayer_ClientCommand( pStandalone, args );
		return;
	}

	// THE SDK TAIL HAS NO SUBJECT LEFT -- plan_entity.md §3.16 family 5, step 3f
	// (identified and proved by step 3g; requests/3g.md §2a).
	//
	// It read
	//     CBasePlayer *pPlayer = <the edict's entity, downcast to a player>;
	//     ::ClientCommand( pPlayer, args );
	// and the downcast answered NULL on EVERY PATH THAT GOT HERE, from both
	// bodies: the arm above returns for a CStandalonePlayer and every player
	// edict in this build is one, so what reaches this point is an edict that is
	// not a player at all. ::ClientCommand( NULL, args ) then drops it on its
	// first line (game/server/client.cpp:1783, "Is the client spawned yet?").
	//
	// SPELLED AS PROSE, NOT AS CODE: tools/entity-census counts spellings over
	// raw text and does not strip comments, so quoting the call here would leave
	// this file reading one cast site after the site was deleted. Step 3g's own
	// first replacement comment did exactly that and put two deleted sites back
	// into the census.
}

//-----------------------------------------------------------------------------
// Purpose: called after the player changes userinfo - gives dll a chance to modify 
//			it before it gets sent into the rest of the engine->
// Input  : *pEdict - the player
//			*infobuffer - their infobuffer
//-----------------------------------------------------------------------------
void CServerGameClients::ClientSettingsChanged( edict_t *pEdict )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientSettingsChanged );

	// Is the client spawned yet?
	if ( !pEdict->GetUnknown() )
		return;

	// CTeamplayRules::ClientSettingsChanged's name half is managed
	// (PlayerNames): it fires player_changename and answers whether the
	// player takes the new name, which this carries out (SetPlayerName).
	CStandalonePlayer *pPlayer = CStandalonePlayer::FromEdict( pEdict );
	IManagedClients *pApi = ManagedClients();
	if ( !pPlayer || !pApi )
		return;

	const int nIndex = engine->IndexOfEdict( pEdict );
	if ( pApi->OnClientSettingsChanged( nIndex ) )
		pPlayer->SetPlayerName( engine->GetClientConVarValue( nIndex, "name" ) );
}


#ifdef PORTAL
//-----------------------------------------------------------------------------
// Purpose: Runs CFuncAreaPortalBase::UpdateVisibility on each portal
// Input  : pAreaPortal - The Area portal to test for visibility from portals
// Output : int - 1 if any portal needs this area portal open, 0 otherwise.
//-----------------------------------------------------------------------------
int TestAreaPortalVisibilityThroughPortals ( CFuncAreaPortalBase* pAreaPortal, edict_t *pViewEntity, unsigned char *pvs, int pvssize  )
{
	int iPortalCount = CProp_Portal_Shared::AllPortals.Count();
	if( iPortalCount == 0 )
		return 0;

	CProp_Portal **pPortals = CProp_Portal_Shared::AllPortals.Base();

	for ( int i = 0; i != iPortalCount; ++i )
	{
		CProp_Portal* pLocalPortal = pPortals[ i ];
		if ( pLocalPortal && pLocalPortal->m_bActivated )
		{
			CProp_Portal* pRemotePortal = pLocalPortal->m_hLinkedPortal.Get();

			// Make sure this portal's linked portal is in the PVS before we add what it can see
			if ( pRemotePortal && pRemotePortal->m_bActivated && pRemotePortal->NetworkProp() && 
				pRemotePortal->NetworkProp()->IsInPVS( pViewEntity, pvs, pvssize ) )
			{
				bool bIsOpenOnClient = true;
				float fovDistanceAdjustFactor = 1.0f;
				Vector portalOrg = pLocalPortal->GetAbsOrigin();
				int iPortalNeedsThisPortalOpen = pAreaPortal->UpdateVisibility( portalOrg, fovDistanceAdjustFactor, bIsOpenOnClient );

				// Stop checking on success, this portal needs to be open
				if ( iPortalNeedsThisPortalOpen )
				{
					return iPortalNeedsThisPortalOpen;
				}
			}
		}
	}
	
	return 0;
}
#endif

//-----------------------------------------------------------------------------
// Purpose: A client can have a separate "view entity" indicating that his/her view should depend on the origin of that
//  view entity.  If that's the case, then pViewEntity will be non-NULL and will be used.  Otherwise, the current
//  entity's origin is used.  Either is offset by the m_vecViewOffset to get the eye position.
// From the eye position, we set up the PAS and PVS to use for filtering network messages to the client.  At this point, we could
//  override the actual PAS or PVS values, or use a different origin.
// NOTE:  Do not cache the values of pas and pvs, as they depend on reusable memory in the engine, they are only good for this one frame
// Input  : *pViewEntity - 
//			*pClient - 
//			**pvs - 
//			**pas - 
//-----------------------------------------------------------------------------
void CServerGameClients::ClientSetupVisibility( edict_t *pViewEntity, edict_t *pClient, unsigned char *pvs, int pvssize )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientSetupVisibility );

	// INITIALISED, AND THAT IS A FIX (step 3c). `Vector org;` is stack garbage
	// until one of the arms below writes it, and NEITHER arm fires for a client
	// with no player entity -- which is exactly what the HLTV and Replay master
	// clients are: SetupPackInfo calls this for them like any other client. The
	// garbage was then handed to CFuncAreaPortalBase::UpdateVisibility for
	// every areaportal on the map. Nothing reported it because a Vector of
	// stack noise is a well-formed position.
	Vector org = vec3_origin;

	// Reset the PVS!!!
	engine->ResetPVS( pvs, pvssize );

	// PreSetupVisibility is not called any more, and nothing changed when it
	// stopped being: CToolFrameworkServer::Init has no kServerSystems[] row, so
	// m_pTools was NULL for the life of every process this build ever ran and
	// every forwarding stage took its early return. Phase 1b deleted the shell
	// that stood in for it (shell_toolframework_absent.cpp); this call was its
	// only consumer, and toolframework_absent_test said in as many words that
	// the two should be deleted together.

	// Find the client's PVS
	if ( pViewEntity )
	{
		// TWO LOCALS STOOD HERE AND NEITHER WAS EVER READ FOR ITS VALUE --
		// plan_shim.md \u00a75 3a, lane 3b-enginebind:
		//
		//   IGameEntity *pVE = NULL;  ... pVE = NULL;
		//
		//     Written twice, read never. It held GetContainingEntity's answer,
		//     which was a constant NULL in this build -- that function is
		//     SourceSharp_SDKEntityFromUnknown< IServerEntity >, bottoming out
		//     in IServerUnknown::GetBaseEntity(), and every implementation of
		//     that in server.so returns NULL (the close read all three out of
		//     the binary). The function is gone and so is the variable that
		//     held its NULL.
		//
		//   bool bHaveViewOrigin = false;
		//
		//     A flag that made the SDK arm and the parallel arm independent
		//     without reordering them, because the SDK arm was the HEAD of an
		//     else-if chain and guarding it in place would have left the `else
		//     if` dangling. With the SDK arm gone there is no chain, nothing
		//     assigns the flag true, and `!bHaveViewOrigin` is a constant that
		//     a reader has to disprove before trusting the condition under it.
		//
		// EyePosition is a CBaseEntity member and GetContainingEntity already
		// answered NULL for a managed view entity, which is why the parallel
		// arm exists at all -- so armed it is simply the only arm, already
		// written and already correct.

		// If we have a viewentity, it overrides the player's origin
		// The same again for an entity on the parallel hierarchy, which has an
		// edict and a position and no CBaseEntity for the line above to find.
		//
		// Without this a managed point_viewcontrol is looked THROUGH into a room
		// whose entities were never sent, because the only origin in the PVS is
		// the player's — and the symptom is a camera that shows an empty version
		// of the right place. Nothing else on this hierarchy asks to be looked
		// through, so nothing else is affected.
		//
		// CBasePlayer::SetupVisibility below is still handed NULL and still adds
		// the player's own origin, so the result is the union of the two rather
		// than stock's substitution. That is more than stock sends, never less.
		if ( ss::GameEntityFromEntityHandle( pViewEntity->GetUnknown() ) )
		{
			// The collision origin, which is the same field the old
			// IGameEntity::GetAbsOrigin read — ICollideable is the face that
			// always had it, and the unknown is already in hand.
			org = pViewEntity->GetUnknown()->GetCollideable()->GetCollisionOrigin();
			engine->AddOriginToPVS( org );
		}
	}

	float fovDistanceAdjustFactor = 1;

	// 13c: the containing entity is never a CBasePlayer any more, so the SDK
	// arm that used to head this else-if chain -- `CBasePlayer *pPlayer = NULL;
	// if ( false ) {}` -- is gone and the standalone arm below is the head as
	// well as the live path.
	// The standalone client (Phase 9a). Without this arm `org` stays
	// UNINITIALISED — stack garbage handed to every areaportal below — and
	// the client's own eye position never enters its PVS. The same three
	// steps as stock's, over the standalone player's state; the FOV factor
	// stays 1 because a standalone player has no zoom yet.
	// THE VIEW ORIGIN IS ASKED OF MANAGED CODE (plan_entity.md §3.10, step 3c),
	// once per client per frame, and the block carries the answer. It used to
	// be CStandalonePlayer::EyePosition -- native state, computed natively --
	// which is the arrangement §3.2 exists to remove: where a player's eyes are
	// follows from the view offset the movement loop maintains, and that loop is
	// managed as of step 3e.
	//
	// SourceSharp_ClientViewOrigin leaves `org` alone and answers false for a
	// client this game has no player for, which is what keeps the HLTV and
	// Replay masters out of the arm below rather than into it with a made-up
	// position.
	if ( SourceSharp_ClientViewOrigin( pClient, &org ) )
	{
		// CBasePlayer::SetupVisibility adds the eye only when no view entity
		// overrode the view; the same rule, stated inline.
		if ( !pViewEntity )
			engine->AddOriginToPVS( org );

		UTIL_SetClientVisibilityPVS( pClient, pvs, pvssize );
	}

	unsigned char portalBits[MAX_AREA_PORTAL_STATE_BYTES];
	memset( portalBits, 0, sizeof( portalBits ) );
	
	int portalNums[512];
	int isOpen[512];
	int iOutPortal = 0;

	for ( CAreaPortal *pCur : g_AreaPortals )
	{

		bool bIsOpenOnClient = true;
		
		// Update our array of which portals are open and flush it if necessary.		
		portalNums[iOutPortal] = pCur->m_portalNumber;
		isOpen[iOutPortal] = pCur->UpdateVisibility( org, fovDistanceAdjustFactor, bIsOpenOnClient );

#ifdef PORTAL
		// If the client doesn't need this open, test if portals might need this area portal open
		if ( isOpen[iOutPortal] == 0 )
		{
			isOpen[iOutPortal] = TestAreaPortalVisibilityThroughPortals( pCur, pViewEntity, pvs, pvssize );
		}
#endif

		++iOutPortal;
		if ( iOutPortal >= ARRAYSIZE( portalNums ) )
		{
			engine->SetAreaPortalStates( portalNums, isOpen, iOutPortal );
			iOutPortal = 0;
		}

		// Version 0 portals (ie: shipping Half-Life 2 era) are always treated as open
		// for purposes of the m_chAreaPortalBits array on the client.
		if ( pCur->m_iPortalVersion == 0 )
			bIsOpenOnClient = true;

		if ( bIsOpenOnClient )
		{
			if ( pCur->m_portalNumber < 0 )
				continue;
			else if ( pCur->m_portalNumber >= sizeof( portalBits ) * 8 )
				Error( "ClientSetupVisibility: portal number (%d) too large", pCur->m_portalNumber );
			else
				portalBits[pCur->m_portalNumber >> 3] |= (1 << (pCur->m_portalNumber & 7));
		}	
	}

	// Flush the remaining areaportal states.
	engine->SetAreaPortalStates( portalNums, isOpen, iOutPortal );

	// The standalone half of the same update: which areas this client may
	// see, recomputed from its own eyes. Held on its CPlayerLocalData —
	// the block the local-exclusive table reads from.
	Vector vecEye;

	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pClient ) )
	{
		// THE SAME CACHED ANSWER, not a second EyePosition() (step 3c). Asking
		// twice in one method would have been two crossings on the first frame
		// and two different eye positions on any frame where they disagreed.
		if ( !SourceSharp_ClientViewOrigin( pClient, &vecEye ) )
			vecEye = pStandalone->Block().Head.Origin;

		auto &local = pStandalone->MvLocal();

		unsigned char tempBits[ 32 ];
		COMPILE_TIME_ASSERT( sizeof( tempBits ) >= sizeof( local.m_chAreaBits ) );

		int nArea = engine->GetArea( vecEye );
		engine->GetAreaBits( nArea, tempBits, sizeof( tempBits ) );

		for ( int i = 0; i < local.m_chAreaBits.Count(); i++ )
		{
			if ( tempBits[ i ] != local.m_chAreaBits[ i ] )
				local.m_chAreaBits.Set( i, tempBits[ i ] );
		}

		for ( int i = 0; i < MAX_AREA_PORTAL_STATE_BYTES; i++ )
		{
			if ( portalBits[ i ] != local.m_chAreaPortalBits[ i ] )
				local.m_chAreaPortalBits.Set( i, portalBits[ i ] );
		}
	}

	// THE SDK PLAYER'S AREA-BITS UPDATE STOOD HERE, guarded on a `CBasePlayer *`
	// that was assigned NULL twelve lines after it was declared and never
	// assigned again -- so the block could not run, and the
	// `dynamic_cast<CPortal_Player*>` inside its `#ifdef PORTAL` was a cast of
	// NULL inside dead text inside a configuration this mod does not build.
	// The standalone player's areaportal bits are written by the arm above.
}




//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *player - 
//			*buf - 
//			numcmds - 
//			totalcmds - 
//			dropped_packets - 
//			ignore - 
//			paused - 
// Output : float
//-----------------------------------------------------------------------------
// The most commands one packet can queue: PlayerCommandQueue.MaxQueuedPerPacket
// (every dropped packet covered, plus the packet's own backup), which
// UserCmdCodecTests holds equal to this.
#define SOURCESHARP_MAX_QUEUED_USERCMDS ( 64 + 24 )

float CServerGameClients::ProcessUsercmds( edict_t *player, bf_read *buf, int numcmds, int totalcmds,
	int dropped_packets, bool ignore, bool paused )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ProcessUsercmds );

	// THE WHOLE PACKET IN ONE CALL -- plan_server_novalve.md P2.2, as the user
	// amended it. What this function did in a loop -- ReadUsercmd per command,
	// the move-magnitude clamp, then a second crossing for the plan -- is C#'s
	// (PlayerCommandQueue.DecodeAndQueue, UserCmdCodec): it is handed the
	// engine buffer's bytes once, and answers once with the commands to queue,
	// in run order, replays resolved, and where the commands end. Native Seeks
	// past them and turns each into the CUserCmd its run loop reads.
	(void)paused;

	// E17: the server's managed-built area.
	IManagedPlayerCommands *pCommands = ManagedPlayerCommands();
	if ( !pCommands )
	{
		// No managed side means no decoder: refuse the packet, which is what an
		// undecodable one gets. There is no game without the managed half.
		Warning( "[SourceSharp] usercmds arrived with no managed decoder; packet refused\n" );
		buf->SetOverflowFlag();
		return 0.0f;
	}

	CStandalonePlayer *pStandalone = ignore ? NULL : CStandalonePlayer::FromEdict( player );

	SourceSharpDecodedUserCmd queued[ SOURCESHARP_MAX_QUEUED_USERCMDS ];
	int32_t nBitsRead = 0;
	const int nStart = buf->GetNumBitsRead();

	const int nQueued = pCommands->PlayerDecodeCommands(
		pStandalone ? pStandalone->ManagedHandle() : (uint32_t)INVALID_EHANDLE_INDEX,
		buf->GetBasePointer(), nStart, nStart + buf->GetNumBitsLeft(),
		numcmds, totalcmds, dropped_packets, pStandalone != NULL ? 1 : 0,
		queued, (int32_t)ARRAYSIZE( queued ), &nBitsRead );

	// A refusal: counts out of range, a truncated stream, or a plan that did
	// not fit. Stock flagged the buffer overflowed for the first and the bit
	// reader did it for the second; the engine drops the packet either way.
	if ( nQueued < 0 )
	{
		buf->SetOverflowFlag();
		return 0.0f;
	}

	buf->Seek( nBitsRead );

	if ( !pStandalone )
		return 0.0f;

	pStandalone->QueueDecodedUsercmds( queued, nQueued );
	return gpGlobals->interval_per_tick;
}


void CServerGameClients::PostClientMessagesSent_DEPRECIATED( void )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_PostClientMessagesSent );

}

// Sets the client index for the client who typed the command into his/her console
void CServerGameClients::SetCommandClient( int index )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_SetCommandClient );

	UTIL_SetCommandClientIndex( index );
}

int	CServerGameClients::GetReplayDelay( edict_t *pEdict, int &entity )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_GetReplayDelay );

	// No replay and no HLTV in this mod, so zero delay is the true answer as
	// well as the only one available.
	//
	// IT IS WRITTEN AS THE CONSTANT IT IS. The body used to ask
	// UTIL_SDKPlayerFromEdict, return 0 when it answered NULL -- which is
	// always -- and otherwise read GetReplayEntity() and GetDelayTicks(). Those
	// two reads were unreachable and the out-param `entity` was never written
	// on any path, which is what this shape now says plainly.
	//
	// THE METHOD ITSELF STAYS, AND plan_shim.md STEP 5c DID NOT DELETE IT EVEN
	// THOUGH 5c IS THE STEP THAT DROPS REPLAY. Read this before "tidying" it.
	//
	// It is a virtual of IServerGameClients (public/eiface.h). THE ENGINE CALLS
	// IT BY VTABLE POSITION, not by name: the game DLL hands the engine a
	// pointer to this object and the engine indexes the vtable at the offset
	// its own copy of the header computed. Delete this method and every slot
	// AFTER it shifts up by one -- GetBugReportInfo, NetworkIDValidated,
	// ClientCommandKeyValues and the rest -- so the engine's call to one of
	// those lands in the body of its neighbour.
	//
	// THE FAILURE WOULD NOT LOOK LIKE A MISSING REPLAY FEATURE. There is no
	// link error, no missing symbol and no refusal in the log; what happens is
	// that some unrelated method starts misbehaving, with arguments meant for
	// a different signature. That is why the slot is unconditional refusal
	// rather than absence, and why a 0-importer census over the link line is
	// necessary and nowhere near sufficient for anything on this class: a
	// virtual call emits no symbol reference for `nm` to find.
	(void)pEdict;
	(void)entity;

	return 0;
}


//-----------------------------------------------------------------------------
// The client's userinfo data lump has changed
//-----------------------------------------------------------------------------
void CServerGameClients::ClientEarPosition( edict_t *pEdict, Vector *pEarOrigin )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientEarPosition );

	// plan_server.md Phase 7, plan-audit C5. WHICH HIERARCHY ANSWERS FOR THIS
	// CLIENT'S EARS, AND IN WHICH ORDER, IS A DECISION, and it has left this
	// file. What used to be here was an `#ifdef SOURCESHARP` reaching for
	// CStandalonePlayer::FromEdict and then falling through to
	// UTIL_SDKPlayerFromEdict; both reads are now steps in
	// shell_clientsteps.cpp that answer yes-or-no about ONE hierarchy each,
	// and ServerClients.EarPositionForClient asks them in that order. The
	// behaviour is unchanged; the place the order is written down is not.
	//
	// THE SS_CBASEENTITY_ALIAS ARM WENT WITH THE READ, not with the method:
	// the guard existed because CBasePlayer::EarPosition is defined in the
	// struck baseplayer_shared.cpp, so it belongs beside the call and it is
	// beside the call now. One region, relocated -- see the ledger.
	//
	// THE MISS PATH IS STOCK'S AND STAYS HERE. Assert(0) then vec3_origin is
	// what the CALLER does with a "no" -- a diagnostic about the ENGINE having
	// named a client this game cannot describe -- and managed code that owned
	// it would be deciding when an engine assert fires.
	//
	// EDICT 0 FOR A NULL pEdict reproduces what the old body did with one: the
	// standalone lookup and UTIL_SDKPlayerFromEdict both answered NULL for it
	// and the Assert fired. Index 0 is worldspawn's, which claims neither
	// hierarchy, so the step pair answers 0 and the Assert still fires.
	//
	// STEP 3c: THE CROSSING IS NOW SHARED WITH ClientSetupVisibility AND HAPPENS
	// AT MOST ONCE PER CLIENT PER FRAME. The engine calls this
	// once per PVS/PAS-filtered message, so
	// once per sound and once per temp entity, not once a frame -- and every one
	// of those used to be a round trip out to managed code and straight back in
	// through ClientsStandalonePlayerEarPosition to read a native eye position.
	// SourceSharp_ClientViewOrigin keeps the answer in the block with the frame
	// it was given on. The managed step order behind it is unchanged.
	if ( SourceSharp_ClientViewOrigin( pEdict, pEarOrigin ) )
		return;

	// The SDK hierarchy is still asked, through the same two steps and in the
	// same order, for an edict the standalone lookup above did not claim. It
	// answers nothing in this build (UTIL_SDKPlayerFromEdict is NULL for every
	// edict here) and stays because the order is the observable part.
	float flEars[ 3 ] = { 0.0f, 0.0f, 0.0f };
	if ( SourceSharp_EarPositionForClient( pEdict ? engine->IndexOfEdict( pEdict ) : 0, flEars ) )
	{
		pEarOrigin->Init( flEars[ 0 ], flEars[ 1 ], flEars[ 2 ] );
		return;
	}

	// Shouldn't happen
	Assert(0);
	*pEarOrigin = vec3_origin;
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *player - 
// Output : CPlayerState
//-----------------------------------------------------------------------------
CPlayerState *CServerGameClients::GetPlayerState( edict_t *player )
{
	// 0e: THE ANSWERS ARE COUNTED SEPARATELY. "The engine called this 40,000
	// times" and "the block answered every one of them" are different facts,
	// and this step's gate is the second one. The call itself is counted once,
	// at the top, so every exit is a call like any other.
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_GetPlayerState );

	// THIS METHOD MAY NOT ANSWER NULL, AND THE `if ( !player->GetUnknown() )`
	// THAT USED TO OPEN IT IS WHY IT COULD (step 3c).
	//
	// Two engine sites write through the result with no null test --
	// `player->hltv = true` and `player->replay = true` for the HLTV and
	// Replay master clients -- and BOTH are reached on an edict that has no
	// entity: server activation makes the master client as a fake client,
	// which the engine connects and stops, so
	// ClientPutInServer has not run and GetUnknown() is NULL at exactly that
	// moment. The guard was not a safety check, it was the crash.
	//
	// The three arms and the SDK resolve are all gone; the body is one call
	// into shell_playerinfo.cpp, which owns the storage. See that file for the
	// engine's callers and for what the orphan state is.
	// WHICH ARM ANSWERED IS COUNTED WHERE THE ARM IS, in shell_playerinfo.cpp.
	// Counting it here would mean resolving the edict twice per call -- once to
	// answer and once to say how it was answered -- on a path the engine takes
	// every tick per client.
	return SourceSharp_PlayerStateForEdict( player );
}

//-----------------------------------------------------------------------------
// Purpose: Anything this game .dll wants to add to the bug reporter text (e.g., the entity/model under the picker crosshair)
//  can be added here
// Input  : *buf - 
//			buflen - 
//-----------------------------------------------------------------------------
void CServerGameClients::GetBugReportInfo( char *buf, int buflen )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_GetBugReportInfo );

	// Stock appended the last few lines of NPC speech in single player. With no
	// NPCs there is none: the answer (GetRecentNPCSpeech, deleted in P5) was 0
	// on every call, so the report is the empty string it always was.
	buf[ 0 ] = 0;
}

//-----------------------------------------------------------------------------
// Purpose: A player sent a voice packet
//-----------------------------------------------------------------------------
void CServerGameClients::ClientVoice( edict_t *pEdict )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientVoice );

	// SOURCESHARP: the entity at a client's edict is ss_player, not a
	// CBasePlayer — the C-cast above would virtual-call through the wrong
	// vtable on the first voice packet. The listener it fed
	// (player_voice_listener.cpp) is out of the build with the player.
	NOTE_UNUSED( pEdict );
}

//-----------------------------------------------------------------------------
// Purpose: A user has had their network id setup and validated 
//-----------------------------------------------------------------------------
void CServerGameClients::NetworkIDValidated( const char *pszUserName, const char *pszNetworkID )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_NetworkIDValidated );

}

// The client has submitted a keyvalues command
void CServerGameClients::ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues )
{
	SS_BOUNDARY_HIT( kBoundaryProbe_Clients_ClientCommandKeyValues );

	if ( !pKeyValues )
		return;

	if ( g_pGameRules )
	{
		g_pGameRules->ClientCommandKeyValues( pEntity, pKeyValues );
	}
}

//-----------------------------------------------------------------------------
// GetSteamIDForPlayerIndex (gameinterface.cpp:3084-3092) IS GONE (P5, user
// ruling 2026-10-01): no object of either DLL called it, and the managed side
// asks the engine itself -- EngineServer.ClientSteamId reads
// GetClientSteamIDByPlayerIndex, which HostingServices.SteamId64 and the
// descent mod's HostLink use.
//-----------------------------------------------------------------------------
