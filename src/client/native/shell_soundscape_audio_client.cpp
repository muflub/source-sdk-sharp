//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client soundscape system's native half =========//
// SS_REASON a  a vtable the engine calls through
//
// Sound cluster 17 (plan_client_novalve C2, lane c2-sims). Stock's
// C_SoundscapeSystem (game/client/c_soundscape.cpp, then
// shell_soundscape_client.cpp) is C#: SourceSharp.Client's ClientSoundscapes
// walks the scripts, keeps the loops and the random one-shots, crossfades,
// and takes the server's pushes, held bit for bit to the stock system by
// SoundscapeOracleTests (soundscape-stock.tsv).
//
// What is here:
//
//   C_SoundscapeSystem, a CClientSystemPerFrame in the client-systems walk at
//     the slot the stock system held, so its level init, its once-a-frame
//     Update (frametime from the walk) and its level shutdown run exactly when
//     stock's did. Each forwards to IManagedSoundscapesClient and decides
//     nothing. The level init matters most: the server's first push arrives
//     inside ClientActive, before the managed module's own level init, so the
//     scripts must be loaded here, at LevelInitPreEntity, as stock loaded them.
//
//   IShimSoundscapeClient's bodies, the executors: an ambient emit, a world
//     emit on CHAN_STATIC to the local player, a stop, the two DSP calls, the
//     main view, and the -makereslists touch. Each is one stock call.
//=============================================================================//

#include "cbase.h"
#include "c_recipientfilter.h"
#include "engine/IEngineSound.h"
#include "filesystem.h"
#include "soundchars.h"
#include "view.h"
#include "tier0/icommandline.h"
#include "imanaged_areas_client.h"
#include "shell_clientsystem.h"
#include "sourcesharp_entrypoints.h"
#include "ss_command_line.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

namespace
{

class C_SoundscapeSystem : public CClientSystemPerFrame
{
public:
	C_SoundscapeSystem() : CClientSystemPerFrame( "C_SoundScapeSystem" ) {}

	virtual void LevelInitPreEntity() OVERRIDE
	{
		if ( IManagedSoundscapesClient *pApi = ManagedSoundscapesClient() )
		{
			char szMapName[ MAX_PATH ];
			pApi->ClientSoundscapesLevelInit( SourceSharp_ClientMapName( szMapName, sizeof( szMapName ) ) );
		}
	}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		if ( IManagedSoundscapesClient *pApi = ManagedSoundscapesClient() )
			pApi->ClientSoundscapesLevelShutdown();
	}

	virtual void Update( float frametime ) OVERRIDE
	{
		if ( IManagedSoundscapesClient *pApi = ManagedSoundscapesClient() )
			pApi->ClientSoundscapesUpdate( frametime );
	}
};

} // namespace

CClientSystem *ClientSoundscapeSystem()
{
	static C_SoundscapeSystem s_System;
	return &s_System;
}

//-----------------------------------------------------------------------------
// The executors.
//-----------------------------------------------------------------------------

void SourceSharp_SoundscapeSetRoomType( int32_t nRoomType )
{
	CLocalPlayerFilter filter;
	enginesound->SetRoomType( filter, nRoomType );
}

void SourceSharp_SoundscapeSetPlayerDsp( int32_t nDspType )
{
	CLocalPlayerFilter filter;
	enginesound->SetPlayerDSP( filter, nDspType, false );
}

// MainView*, not CurrentView*: the latter asserts it is inside the render
// pass, and the soundscape runs from a client system's Update and from console
// commands (the old shell_soundscape_client.cpp's header has the crash).
void SourceSharp_SoundscapeMainView( float *pOut9 )
{
	if ( !pOut9 )
		return;

	const Vector &o = MainViewOrigin();
	const Vector &f = MainViewForward();
	const Vector &r = MainViewRight();
	const float v[ 9 ] = { o.x, o.y, o.z, f.x, f.y, f.z, r.x, r.y, r.z };
	for ( int i = 0; i < 9; ++i )
		pOut9[ i ] = v[ i ];
}

int32_t SourceSharp_SoundscapeMakingResLists()
{
	return ss::CommandLineHas( "-makereslists" ) ? 1 : 0;
}

void SourceSharp_SoundscapeTouchSoundFile( const char *pszWave )
{
	if ( pszWave )
		filesystem->GetFileTime( VarArgs( "sound/%s", PSkipSoundChars( pszWave ) ), "GAME" );
}
