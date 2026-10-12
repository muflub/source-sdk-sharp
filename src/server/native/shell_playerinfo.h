//========= SourceSharp ======================================================//
//
// THE PLAYER, AS THE ENGINE AND A SERVER PLUGIN SEE ONE -- step 3c of
// plan_entity.md (§3.10).
//
// Three things live behind this header, and they are one file because they are
// one subject: what the server can say about a player without asking managed
// code, and where the storage for it is.
//
//   1. GetPlayerState's whole body. IServerGameClients::GetPlayerState hands
//      the engine a CPlayerState *, and the storage is the sixty-four bytes the
//      engine block's SERVER TAIL reserves (§3.2). See the .cpp for why it may
//      never answer NULL.
//
//   2. The view origin. §3.10: "ClientSetupVisibility asks managed for the view
//      origin once and adds it to the PVS natively; ClientEarPosition
//      likewise." ONCE means once per client per FRAME -- the engine calls
//      ClientEarPosition once per PVS-filtered message, so "once per call"
//      would be a crossing per sound.
//      The block carries the answer and the frame it was answered on.
//
//   3. IPlayerInfo and IPlayerInfoManager, which are the SERVER PLUGIN's view
//      of a player and read the same block. Step 0e established that the
//      engine never asks for IPlayerInfoManager: its consumer in the public SDK
//      is utils/serverplugin_sample, which acquires it through
//      gameServerFactory. So it has no in-process caller at
//      all, and `cs_sv_playerinfo` exists to ask it the way a plugin would --
//      through CreateInterface, by name -- rather than by calling the object.
//
// THE REFUSAL COUNTER IS HERE AND IS ENGINE-FREE, for shell_gamedll_refusals.h's
// reason: the property worth checking is a property of the BODY (that a call
// counts, that a second call to the same method does not open a second row) and
// only running it can check that. shell_playerinfo.cpp includes cbase.h and can
// never be linked into a gtest; this can.
//
//===========================================================================//
#ifndef SOURCESHARP_SHELL_PLAYERINFO_H
#define SOURCESHARP_SHELL_PLAYERINFO_H
#ifdef _WIN32
#pragma once
#endif

#include <string.h>

struct edict_t;  // a struct in public/edict.h; the tag must match (clang -Wmismatched-tags)
class CPlayerState;
class Vector;

//-----------------------------------------------------------------------------
// GetPlayerState's answer, and it is NEVER NULL.
//
// The reasoning is at the definition. In one line: two engine sites write
// through the result with no null test, and one of them is reached on an edict
// that has no entity at all, so "read it out of the entity's block" cannot be
// the whole answer.
//-----------------------------------------------------------------------------
CPlayerState *SourceSharp_PlayerStateForEdict( edict_t *pEdict );

//-----------------------------------------------------------------------------
// Where this client's eyes are this frame: true with *pOut written, false for
// an edict that is no player of ours, leaving *pOut UNTOUCHED.
//
// At most ONE crossing per client per frame -- the block remembers both the
// answer and the frame it was given on.
//-----------------------------------------------------------------------------
bool SourceSharp_ClientViewOrigin( edict_t *pClient, Vector *pOut );

//-----------------------------------------------------------------------------
// The console half of a refusal. Declared, not defined -- tier0's Warning() in
// shell_playerinfo.cpp, a recorder in the gtest. nTotal is the running total
// across all of them, so the line a reader sees carries the same number the
// readout will.
//-----------------------------------------------------------------------------
void ShellPlayerInfo_RefusalWarning( const char *pMethod, int nTotal );

//-----------------------------------------------------------------------------
// One row per IPlayerInfo method that was called and could not be answered.
//
// PER METHOD AND NOT JUST A TOTAL. "A plugin asked for something this shim
// cannot give" is the gate; WHICH method is the finding, and four of the
// TWENTY-FIVE methods refuse for four different reasons -- only the wrong one
// has to be revisited. (Twenty-five, counted off iplayerinfo.h: GetPlayerInfo
// and GetGlobalVars are IPlayerInfoManager's two, not IPlayerInfo's.)
//
// Matched by strcmp and not by pointer identity, for shell_gamedll_refusals.h's
// reason: the names are literals in one translation unit today and would stop
// being so silently, and the failure would be a duplicate row rather than an
// error.
//-----------------------------------------------------------------------------
class CShellPlayerInfoRefusals
{
public:
	enum { kMaxMethods = 8 };

	void Refused( const char *pMethod )
	{
		m_nTotal++;

		if ( pMethod != NULL )
		{
			int iRow = Find( pMethod );

			if ( iRow < 0 && m_nMethods < kMaxMethods )
			{
				iRow = m_nMethods++;
				m_pMethods[ iRow ] = pMethod;
				m_nCounts[ iRow ] = 0;
			}

			// A table that overflowed still counts the total, which is what
			// the readout leads with. Losing a row is better than losing the
			// number.
			if ( iRow >= 0 )
				m_nCounts[ iRow ]++;
		}

		// WARNED ONCE PER METHOD, and this is where it differs from the game
		// DLL's counter deliberately. Those sixteen are argued to be
		// UNREACHABLE, so a flood is the argument failing in the loudest
		// available way. These four are reachable by construction -- a plugin
		// walking the player list every frame would call GetWeaponName every
		// frame -- so a flood would say nothing the first line did not, and
		// would bury the rest of the console.
		if ( pMethod == NULL || CountOf( pMethod ) <= 1 )
			ShellPlayerInfo_RefusalWarning( pMethod, m_nTotal );
	}

	int Total() const { return m_nTotal; }
	int MethodCount() const { return m_nMethods; }

	const char *MethodAt( int i ) const
	{
		return ( i >= 0 && i < m_nMethods ) ? m_pMethods[ i ] : NULL;
	}

	int CountAt( int i ) const
	{
		return ( i >= 0 && i < m_nMethods ) ? m_nCounts[ i ] : 0;
	}

	int CountOf( const char *pMethod ) const
	{
		const int iRow = Find( pMethod );
		return ( iRow < 0 ) ? 0 : m_nCounts[ iRow ];
	}

private:
	int Find( const char *pMethod ) const
	{
		if ( pMethod == NULL )
			return -1;

		for ( int i = 0; i < m_nMethods; i++ )
		{
			if ( m_pMethods[ i ] != NULL && strcmp( m_pMethods[ i ], pMethod ) == 0 )
				return i;
		}

		return -1;
	}

	int m_nTotal = 0;
	int m_nMethods = 0;
	const char *m_pMethods[ kMaxMethods ] = {};
	int m_nCounts[ kMaxMethods ] = {};
};

//-----------------------------------------------------------------------------
// The manager's two tallies, read-only, for cs_sv_playerinfo
// (shell_playerinfo_debug.cpp, debug builds only). The manager itself stays
// file-static in shell_playerinfo.cpp: these hand out a view, not the object.
//-----------------------------------------------------------------------------
const CShellPlayerInfoRefusals &ShellPlayerInfo_Refusals();
int ShellPlayerInfo_OrphanAnswers();

#endif // SOURCESHARP_SHELL_PLAYERINFO_H
