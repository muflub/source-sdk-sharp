//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

// Sentence group setup from stock's sound.cpp.

#include "ss_util.h"
#include "ss_recipient_filter.h"
#include "server_state.h"
#include "server_engine.h"

// Called from world spawn; only the first call of the DLL does anything.
void SENTENCEG_Init()
{
	if ( ShellGameDll_Dll()->m_bSentencesInit )
		return;

	ServerEngine()->PrecacheSentenceFile( "scripts/sentences.txt" );
	ShellGameDll_Dll()->m_bSentencesInit = true;
}
