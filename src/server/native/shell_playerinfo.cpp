//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON b  an interface resolved by name at load
//
// THE PLAYER, AS THE ENGINE AND A SERVER PLUGIN SEE ONE -- step 3c.
//
// See shell_playerinfo.h for what lives here and why the three subjects are one
// file. This is the argument for the two decisions that are not obvious.
//
//
// WHY GetPlayerState MAY NOT ANSWER NULL
// ======================================
//
// Four of the engine's callers test the result:
//
//     the per-frame Steam user-data update   skips a NULL player
//     rcon `status`                          skips a NULL player
//     a savegame                             "Can't savegame without a player"
//     one more per-client path               asserts, then checks
//
// Two do not:
//
//     player->hltv   = true;                 the HLTV master client's setup
//     player->replay = true;                 the Replay master client's setup
//
// Those two write straight through the returned pointer: they are adjacent
// bools in CPlayerState (public/PlayerState.h), at +0x30 and +0x31. A NULL
// there is not a missing feature, it is a write through null inside the
// engine: a crash, not a flag quietly left unset.
//
// AND THE BLOCK ALONE CANNOT SERVE THEM, which is the part that is easy to get
// wrong. Both sites run when the server activates, on the master client's
// edict, and the master client on the first-start path is a fake client: the
// engine connects it and does nothing else with it before those two writes.
// ClientPutInServer has NOT run, so that edict has no entity, no shell and
// therefore no block. "Read it out of the entity's block" answers NULL for
// exactly the case that cannot take one.
//
// So the manager below keeps one CPlayerState per player slot as the answer for
// an edict no player claims. It is real, constructed storage; the engine's
// write lands in it and nothing reads it back, which is the same outcome the
// engine would have had if the fake client had had an entity and the game had
// not cared. THE LIVE GATE PROVES NONE OF THIS: step 0d states that it covers
// no HLTV, no Replay and no dedicated server. That is why the storage is
// unconditional rather than narrowed to what a green gate would have allowed.
//
// ONE BEHAVIOURAL CONSEQUENCE, STATED. The Steam user-data update's NULL skip
// is now unreachable, so a connected-but-not-yet-active client with a valid
// Steam ID reaches BUpdateUserData with frags 0 instead of being skipped. That
// is a Steam scoreboard write of the number the player actually has, one frame
// earlier than before; it is the price of never answering NULL and it is
// recorded rather than hidden.
//
//
// WHY THE VIEW ORIGIN IS CACHED IN THE BLOCK
// ==========================================
//
// §3.10 gives both ClientSetupVisibility and ClientEarPosition the same
// question -- where are this client's eyes -- and says to ask managed code. The
// two are not called at the same rate. ClientSetupVisibility is once per client
// per frame (while the engine sets up that client's snapshot);
// ClientEarPosition is once per PVS/PAS-filtered message, so every sound and
// every temp entity asks it again.
//
// Before this step, each ask was TWO crossings: out to
// ServerEarPositionForClient, and straight back in through
// ClientsStandalonePlayerEarPosition, which read CStandalonePlayer::EyePosition
// natively. A round trip to be told what the native side already knew.
//
// Now the answer is filled once per frame, into the block, and every later ask
// in the same frame is a load. The frame stamp is what makes "once" mean once.
//
// THE STALENESS THIS BUYS, SAID OUT LOUD. Within one frame every reader gets
// the view origin as of the FIRST ask in that frame, rather than re-reading it
// per message. The player's position only moves in RunAllQueuedCommands, which
// runs before entity thinks and long before the snapshot setup, so the two readers
// see the same post-movement value in the ordinary case; a sound emitted from
// inside the movement loop itself (a trigger touched mid-command) is the one
// case that can be up to one command old.
//
// Written down HERE and not in a divergences document: commit b6c429e1 deleted
// docs/ of the earlier plans, and a divergence recorded in a file that does not
// exist is a divergence nobody records. Several comments in SourceSharp.Server
// still cite that file and are pointing at nothing.
//
//===========================================================================//

#include "edict.h"
#include "eiface.h"

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;
#include "eiface.h"
#include "edict.h"
#include "PlayerState.h"
#include "game/server/iplayerinfo.h"

#include "shell_playerinfo.h"
#include "shell_standalone_player.h"
#include "shell_server.h"
#include "engine_block.h"

// 0e's engine-boundary probe. GetPlayerState's two surviving arms are counted
// here, where the arms are.
#include "shell_boundary_probe.h"
#include "native_command_body.h"
#include "shell_factory.h"


//-----------------------------------------------------------------------------
// The four methods this shim cannot answer, named once so the bodies and the
// tests agree about the spelling.
//-----------------------------------------------------------------------------
#define SS_PLAYERINFO_REFUSAL_CHANGE_TEAM   "IPlayerInfo::ChangeTeam"
#define SS_PLAYERINFO_REFUSAL_WEAPON_NAME   "IPlayerInfo::GetWeaponName"
#define SS_PLAYERINFO_REFUSAL_MAX_HEALTH    "IPlayerInfo::GetMaxHealth"
#define SS_PLAYERINFO_REFUSAL_LAST_COMMAND  "IPlayerInfo::GetLastUserCommand"

void ShellPlayerInfo_RefusalWarning( const char *pMethod, int nTotal )
{
	Warning( "[SourceSharp] REFUSED: %s was called and this shim cannot answer "
	         "it. plan_entity.md step 3c: IPlayerInfo reads the engine block, and "
	         "this method asks for something the block does not carry -- the "
	         "answer is managed policy or a managed object. Refusal #%d; "
	         "cs_sv_playerinfo for the tally.\n",
	         ( pMethod != NULL ) ? pMethod : "(unnamed)", nTotal );
}

//-----------------------------------------------------------------------------
// ONE PLAYER, AS A PLUGIN SEES IT.
//
// Every getter resolves the edict to a shell and reads the BLOCK. It holds an
// edict INDEX and not a pointer or a shell: a plugin is entitled to keep the
// IPlayerInfo * it was handed, and stock's own CPlayerInfo does exactly that
// (it is a member of CBasePlayer, so it dies with the player). An index cannot
// dangle -- it resolves to nothing once the player is gone, which is what every
// body below already has to handle.
//-----------------------------------------------------------------------------
class CShellPlayerInfo : public IPlayerInfo
{
public:
	CShellPlayerInfo() : m_nEdict( 0 ), m_pRefusals( NULL ) {}

	void Bind( int nEdict, CShellPlayerInfoRefusals *pRefusals )
	{
		m_nEdict    = nEdict;
		m_pRefusals = pRefusals;
	}

	// --- identity ------------------------------------------------------------
	virtual const char *GetName() OVERRIDE
	{
		const CPlayerState *pState = State();

		// The netname the shell pooled in SetPlayerName, which is the same
		// string the engine reads out of this struct for the scoreboard.
		return ( pState != NULL ) ? STRING( pState->netname ) : "";
	}

	virtual int GetUserID() OVERRIDE
	{
		edict_t *pEdict = Edict();
		return ( pEdict != NULL ) ? engine->GetPlayerUserId( pEdict ) : -1;
	}

	virtual const char *GetNetworkIDString() OVERRIDE
	{
		edict_t *pEdict = Edict();
		return ( pEdict != NULL ) ? engine->GetPlayerNetworkIDString( pEdict ) : "";
	}

	// NOT A REFUSAL, AND THE DIFFERENCE MATTERS. The player's team is real
	// storage now (CStandalonePlayer::m_iTeamNum, tf2 stage 0.5), so this
	// reads it; an edict with no player claims keeps answering
	// TEAM_UNASSIGNED -- the same answer stock gives a player who has not
	// picked a side. A refusal is for a question this shim cannot answer,
	// not for one whose answer is dull.
	virtual int GetTeamIndex() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->TeamNumber() : 0;
	}

	// A REFUSAL, because it is a mutator. "Put this player on team 2" that
	// silently does nothing is precisely §7's silent no-op: the caller has no
	// way to tell it from a team change that happened.
	virtual void ChangeTeam( int iTeamNum ) OVERRIDE
	{
		( void )iTeamNum;
		Refuse( SS_PLAYERINFO_REFUSAL_CHANGE_TEAM );
	}

	virtual int GetFragCount() OVERRIDE
	{
		const CPlayerState *pState = State();
		return ( pState != NULL ) ? pState->frags : 0;
	}

	virtual int GetDeathCount() OVERRIDE
	{
		const CPlayerState *pState = State();
		return ( pState != NULL ) ? pState->deaths : 0;
	}

	virtual bool IsConnected() OVERRIDE { return Player() != NULL; }

	// Armour does not exist on this hierarchy -- nothing grants it, nothing
	// spends it. Zero is the truth, for GetTeamIndex's reason.
	virtual int GetArmorValue() OVERRIDE { return 0; }

	// --- flags ---------------------------------------------------------------
	virtual bool IsHLTV() OVERRIDE
	{
		const CPlayerState *pState = State();
		return ( pState != NULL ) && pState->hltv;
	}

	virtual bool IsPlayer() OVERRIDE { return Player() != NULL; }

	virtual bool IsFakeClient() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL )
		    && ( pPlayer->Block().Head.Flags & FL_FAKECLIENT ) != 0;
	}

	virtual bool IsDead() OVERRIDE
	{
		const CPlayerState *pState = State();
		return ( pState != NULL ) && pState->deadflag;
	}

	// This build has no vehicles: CStandalonePlayer::MvIsInAVehicle is a flat
	// false and the movement loop's vehicle arms are unreachable. False is the
	// truth, not a stand-in.
	virtual bool IsInAVehicle() OVERRIDE { return false; }

	virtual bool IsObserver() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) && pPlayer->MvIsObserver();
	}

	virtual bool IsReplay() OVERRIDE
	{
		const CPlayerState *pState = State();
		return ( pState != NULL ) && pState->replay;
	}

	// --- placement, straight out of the block --------------------------------
	virtual const Vector GetAbsOrigin() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->Block().Head.Origin : vec3_origin;
	}

	virtual const QAngle GetAbsAngles() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->Block().Head.Angles : vec3_angle;
	}

	virtual const Vector GetPlayerMins() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->Block().Head.Mins : vec3_origin;
	}

	virtual const Vector GetPlayerMaxs() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->Block().Head.Maxs : vec3_origin;
	}

	// A REFUSAL. Which weapon a player is holding is the managed Arsenal's, and
	// NULL is what stock answers for a player holding nothing -- so a silent
	// NULL here would be indistinguishable from a real answer.
	virtual const char *GetWeaponName() OVERRIDE
	{
		Refuse( SS_PLAYERINFO_REFUSAL_WEAPON_NAME );
		return NULL;
	}

	virtual const char *GetModelName() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->GetModelNameString() : "";
	}

	virtual const int GetHealth() OVERRIDE
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->GetHealth() : 0;
	}

	// A REFUSAL. The maximum is SsPlayer.SpawnHealth, a managed rule, and the
	// block does not carry it. Zero is returned because there is no honest
	// number to return, and the warning is what says so.
	virtual const int GetMaxHealth() OVERRIDE
	{
		Refuse( SS_PLAYERINFO_REFUSAL_MAX_HEALTH );
		return 0;
	}

	// A REFUSAL. The usercmd queue is step 3d's and moves to managed code with
	// it; a reset CBotCmd is a command that says "no buttons, no movement",
	// which a caller would read as a player standing still.
	virtual CBotCmd GetLastUserCommand() OVERRIDE
	{
		Refuse( SS_PLAYERINFO_REFUSAL_LAST_COMMAND );

		CBotCmd empty;
		empty.Reset();
		return empty;
	}

private:
	edict_t *Edict() const
	{
		return ( m_nEdict >= 1 ) ? engine->PEntityOfEntIndex( m_nEdict ) : NULL;
	}

	CStandalonePlayer *Player() const { return CStandalonePlayer::FromIndex( m_nEdict ); }

	// THE BLOCK READ. Deliberately not SourceSharp_PlayerStateForEdict: this
	// view is about a player that EXISTS, and the orphan state that function
	// answers for an edict with no player would turn "no such player" into a
	// zeroed one -- dead, nameless, no frags -- which a plugin cannot tell from
	// a real player who has just spawned.
	const CPlayerState *State() const
	{
		const CStandalonePlayer *pPlayer = Player();
		return ( pPlayer != NULL ) ? pPlayer->PlayerStatePtr() : NULL;
	}

	void Refuse( const char *pMethod )
	{
		if ( m_pRefusals != NULL )
			m_pRefusals->Refused( pMethod );
	}

	int m_nEdict;
	CShellPlayerInfoRefusals *m_pRefusals;
};

//-----------------------------------------------------------------------------
// THE OBJECT THE FACTORY HANDS A PLUGIN, and the owner of everything above.
//
// NO FILE-SCOPE STATE: the per-slot views, the orphan player states and the
// refusal counter are all FIELDS of this one object, which is the project's
// rule and is also what makes the whole of it inspectable from one place.
//
// EXPOSE_SINGLE_INTERFACE_GLOBALVAR needs an object with external linkage, so
// there is one instance and it is named below. That is the same shape
// CShellGameDll has.
//-----------------------------------------------------------------------------
class CShellPlayerInfoManager : public IPlayerInfoManager
{
public:
	CShellPlayerInfoManager()
	{
		for ( int i = 0; i <= ABSOLUTE_PLAYER_LIMIT; i++ )
			m_Views[ i ].Bind( i, &m_Refusals );
	}

	virtual IPlayerInfo *GetPlayerInfo( edict_t *pEdict ) OVERRIDE
	{
		// NULL FOR AN EDICT THAT IS NO PLAYER, which is stock's answer and the
		// one a plugin's own code is written against -- serverplugin_sample
		// tests the pointer. This is the opposite of GetPlayerState's rule and
		// the difference is the caller: a plugin checks, and the two engine
		// sites that make GetPlayerState dangerous do not.
		const int nIndex = IndexOf( pEdict );

		if ( nIndex < 1 || nIndex > ABSOLUTE_PLAYER_LIMIT )
			return NULL;

		if ( CStandalonePlayer::FromIndex( nIndex ) == NULL )
			return NULL;

		return &m_Views[ nIndex ];
	}

	virtual CGlobalVars *GetGlobalVars() OVERRIDE { return gpGlobals; }

	//-------------------------------------------------------------------------
	// The player state for an edict no player claims. See the file header.
	//
	// Indexed by edict, so two orphan clients -- an HLTV master and a Replay
	// master, which is the case that exists -- do not write into each other's
	// bytes. One shared struct would have been enough for the two bools those
	// two sites write, and would have been wrong the first time anything read
	// frags back.
	//-------------------------------------------------------------------------
	CPlayerState *Orphan( int nIndex )
	{
		if ( nIndex < 0 || nIndex > ABSOLUTE_PLAYER_LIMIT )
			nIndex = 0;

		m_nOrphanAnswers++;
		return &m_Orphans[ nIndex ];
	}

	int OrphanAnswers() const { return m_nOrphanAnswers; }

	const CShellPlayerInfoRefusals &Refusals() const { return m_Refusals; }

private:
	static int IndexOf( edict_t *pEdict )
	{
		return ( pEdict != NULL ) ? engine->IndexOfEdict( pEdict ) : -1;
	}

	// One per slot INCLUDING slot 0, which is worldspawn's and can never be a
	// player: sized by edict index rather than by player number so the index
	// arithmetic is the identity and there is no off-by-one to get wrong.
	CShellPlayerInfo m_Views[ ABSOLUTE_PLAYER_LIMIT + 1 ];
	CPlayerState     m_Orphans[ ABSOLUTE_PLAYER_LIMIT + 1 ];

	int m_nOrphanAnswers = 0;

	CShellPlayerInfoRefusals m_Refusals;
};

static CShellPlayerInfoManager s_ShellPlayerInfoManager;

// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetPlayerInfoManager() { return (IPlayerInfoManager *)&s_ShellPlayerInfoManager; }
const SourceSharpExposed kExposePlayerInfoManager = { INTERFACEVERSION_PLAYERINFOMANAGER, SourceSharp_GetPlayerInfoManager };

//-----------------------------------------------------------------------------
// GetPlayerState's body. Never NULL -- see the file header for the six call
// sites and for why the two unguarded ones cannot be served from the block.
//-----------------------------------------------------------------------------
CPlayerState *SourceSharp_PlayerStateForEdict( edict_t *pEdict )
{
	CStandalonePlayer *pPlayer = CStandalonePlayer::FromEdict( pEdict );

	if ( pPlayer != NULL )
	{
		// 0e's PROBE ARM, and it is the step's own gate: the answer came out of
		// that player's engine block. Counted here rather than at the call site
		// so that the edict is resolved ONCE -- the engine asks this every tick
		// per client, and a second FromEdict to work out what to count would be
		// a lookup spent on the counting.
		SS_BOUNDARY_HIT( kBoundaryProbe_GetPlayerState_Standalone );
		return pPlayer->PlayerStatePtr();
	}

	// The counter that used to say NULL. Same question -- an edict this game
	// has no player for -- different answer.
	SS_BOUNDARY_HIT( kBoundaryProbe_GetPlayerState_Null );

	return s_ShellPlayerInfoManager.Orphan(
		( pEdict != NULL ) ? engine->IndexOfEdict( pEdict ) : 0 );
}

//-----------------------------------------------------------------------------
// Where this client's eyes are, asked of managed code at most once per frame.
//
// THE STAMP IS THE WHOLE MECHANISM. gpGlobals->framecount is what both readers
// share; a block whose stamp is this frame already holds a managed answer and
// there is nothing to ask.
//-----------------------------------------------------------------------------
bool SourceSharp_ClientViewOrigin( edict_t *pClient, Vector *pOut )
{
	if ( pOut == NULL )
		return false;

	CStandalonePlayer *pPlayer = CStandalonePlayer::FromEdict( pClient );

	if ( pPlayer == NULL )
		return false;

	EngineBlockServerTail_t &tail = pPlayer->Block().Server;

	if ( tail.ViewOriginFrame != gpGlobals->framecount )
	{
		float flEyes[ 3 ] = { 0.0f, 0.0f, 0.0f };

		// A MISS LEAVES THE CACHE ALONE AND ANSWERS NO. The managed side writes
		// the three floats only on a hit (SourceSharp_EarPositionForClient), so
		// a zeroed flEyes is not an answer -- and the world origin is a real
		// position, which is exactly why that contract exists.
		if ( !SourceSharp_EarPositionForClient( engine->IndexOfEdict( pClient ), flEyes ) )
			return false;

		tail.ViewOrigin.Init( flEyes[ 0 ], flEyes[ 1 ], flEyes[ 2 ] );
		tail.ViewOriginFrame = gpGlobals->framecount;
	}

	*pOut = tail.ViewOrigin;
	return true;
}

// cs_sv_playerinfo, the plugin-style query through the factory, is a
// diagnostic: it lives in shell_playerinfo_debug.cpp, which only a debug build
// compiles. It reads the manager's tallies through ShellPlayerInfo_Refusals and
// ShellPlayerInfo_OrphanAnswers below.

const CShellPlayerInfoRefusals &ShellPlayerInfo_Refusals()
{
	return s_ShellPlayerInfoManager.Refusals();
}

int ShellPlayerInfo_OrphanAnswers()
{
	return s_ShellPlayerInfoManager.OrphanAnswers();
}
