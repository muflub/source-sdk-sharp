//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — IServerGameClients, declared where it lives ========//
//
// plan_entity.md step 4g. The class the engine reaches for every question about
// one player, moved OUT of stock `game/server/gameinterface.h` and into the
// shim beside the file that implements it.
//
// WHY IT WAS STILL THERE, AND WHY THAT REASON IS SPENT. The bodies left
// gameinterface.cpp at plan_gamedll_after.md Phase A9 step 2 and the
// declaration deliberately did not, with the reason written down in
// shell_gameclients.cpp:
//
//     "three other translation units in the compiled set define methods ON
//      this class ... and four mod-specific gameinterface.cpp files out of the
//      build define their own GetPlayerLimits against the same declaration. A
//      per-mod override of one method is the SDK's arrangement here and moving
//      the declaration would not change what this file does; it would only
//      decide something this phase was not asked to decide."
//
// 4g IS asked to decide it, and the arithmetic has moved besides. Measured at
// this tip, `gameinterface.h` has EIGHT compiled readers and every one of them
// is a shim file:
//
//     ServerNetworkProperty.cpp  shell_engineboundary.cpp  shell_frame.cpp
//     shell_gameclients.cpp      shell_gamedll.cpp        shell_level.cpp
//     shell_server.cpp
//
// (`base_gameinterface.cpp`, `hl2mp_gameinterface.cpp`, `sdk_gameinterface.cpp`
// and `tf_gameinterface.cpp` — the four per-mod GetPlayerLimits overrides the
// note above was protecting — are none of them in server_csharp.vpc.) So the
// declaration is not shared with stock any more; it is a shim declaration that
// happened to be sitting in a stock header, which is exactly the residue 4g is
// for.
//
// WHAT DID NOT MOVE WITH IT. `CMapEntityRef`, `g_MapEntityRefs`,
// `CMapLoadEntityFilter` and the two networked string tables stay in
// gameinterface.h: they are the MAP LOADER's, read by shell_level.cpp and
// shell_engineboundary.cpp, and they belong to whichever step takes
// mapentities.cpp rather than to this one.
//
//=============================================================================//
#pragma once

#include "eiface.h"
#include "convar.h"

struct edict_t;

// Player / Client related functions
// Most of this is implemented in sourcesharp/game/server/shell_gameclients.cpp
// since plan_gamedll_after.md Phase A9, but some of it is per-mod in each game's
// gameinterface file.
class CServerGameClients : public IServerGameClients
{
public:
	virtual bool			ClientConnect( edict_t *pEntity, char const* pszName, char const* pszAddress, char *reject, int maxrejectlen ) OVERRIDE;
	virtual void			ClientActive( edict_t *pEntity, bool bLoadGame ) OVERRIDE;
	virtual void			ClientDisconnect( edict_t *pEntity ) OVERRIDE;
	virtual void			ClientPutInServer( edict_t *pEntity, const char *playername ) OVERRIDE;
	virtual void			ClientCommand( edict_t *pEntity, const CCommand &args ) OVERRIDE;
	virtual void			ClientSettingsChanged( edict_t *pEntity ) OVERRIDE;
	virtual void			ClientSetupVisibility( edict_t *pViewEntity, edict_t *pClient, unsigned char *pvs, int pvssize ) OVERRIDE;
	virtual float			ProcessUsercmds( edict_t *player, bf_read *buf, int numcmds, int totalcmds,
								int dropped_packets, bool ignore, bool paused ) OVERRIDE;
	// Player is running a command
	virtual void			PostClientMessagesSent_DEPRECIATED( void ) OVERRIDE;
	virtual void			SetCommandClient( int index ) OVERRIDE;
	virtual CPlayerState	*GetPlayerState( edict_t *player ) OVERRIDE;
	virtual void			ClientEarPosition( edict_t *pEntity, Vector *pEarOrigin ) OVERRIDE;

	virtual void			GetPlayerLimits( int& minplayers, int& maxplayers, int &defaultMaxPlayers ) const OVERRIDE;
	
	// returns number of delay ticks if player is in Replay mode (0 = no delay)
	virtual int				GetReplayDelay( edict_t *player, int& entity ) OVERRIDE;
	// Anything this game .dll wants to add to the bug reporter text (e.g., the entity/model under the picker crosshair)
	//  can be added here
	virtual void			GetBugReportInfo( char *buf, int buflen ) OVERRIDE;
	virtual void			NetworkIDValidated( const char *pszUserName, const char *pszNetworkID ) OVERRIDE;

	// The client has submitted a keyvalues command
	virtual void			ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) OVERRIDE;

	// Notify that the player is spawned
	virtual void			ClientSpawned( edict_t *pPlayer ) OVERRIDE;

	virtual void			ClientVoice( edict_t* pEdict ) OVERRIDE;

	// plan_gamedll_after.md Phase A9 step 2 made two file-scope statics of
	// gameinterface.cpp fields of this class. Both are gone.
	//
	// THE FIRST was the bot hook, m_pClientPutInServerOverride: a mod could
	// substitute its own player factory for the engine's ClientPutInServer. Its
	// setters, SetClientPutInServerOverride and the free
	// ClientPutInServerOverride, went in the server gc cull (2026-10-01) because
	// nothing called them, which left the field always NULL; it went in the
	// dead-state sweep after, with the typedef and the constructor that set it.
	//
	// THE SECOND FIELD IS GONE TOO. It was m_svMaxUsercmdMoveMagnitude, a native
	// ConVar declaration for sv_max_usercmd_move_magnitude, and plan_shim.md
	// §5 3a moved the declaration to SourceSharp.Server/ConVars.cs: its flags
	// were 0, so it is not replicated and not read by the engine, which makes
	// it policy rather than transmitted state. ProcessUsercmds still reads it,
	// by name through CManagedConVarRef.
};
