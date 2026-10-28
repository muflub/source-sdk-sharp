//========= SourceSharp — WHAT THIS BUILD DOES NOT SHIP =======================//
//
// plan_entity.md step 7g. The step deletes a group of stock translation units
// on the grounds that this mod does not ship what they implement — achievements,
// replay, the HLTV camera, rich presence, the Sixense controller, the map
// overview, bonus maps. The DELETION is the easy half. The half that needs a
// mechanism is the sentence 7g's gate is written in:
//
//     the engine's interface probes show each interface answered or REFUSED BY
//     NAME
//
// and §7's rule that the same thing holds one level down:
//
//     a behaviour that is refused is a ContractBaseline entry with a reason,
//     never a silent no-op
//
// A capability that is simply gone leaves two indistinguishable outcomes: the
// engine asked and got nothing because we meant it, or the engine asked and got
// nothing because an export broke. This file is what tells them apart. Every
// name here is a DECLARATION that the absence is deliberate, carrying the reason
// in the same row, and every reach is COUNTED so "it was never asked for" is an
// answer somebody can produce after a session rather than an absence of
// evidence. It is CShellClientDllRefusals' argument applied to whole
// capabilities instead of to vtable slots, and it is deliberately the same shape.
//
// TWO KINDS OF ROW, ONE COUNTER:
//
//   * INTERFACES — a name the engine passes to client.so's CreateInterface.
//     The row is the written declaration that the absence is deliberate.
//     Nothing looks it up at run time any more: the lookup was a miss hook in
//     tier1/interface.cpp, and it went in Phase 6b, when the build moved to
//     Valve's prebuilt tier1.a. ShellNotShipped_FindInterface stays because
//     shell_notshipped_test.cpp pins the table through it.
//
//   * CAPABILITIES — a code path inside a still-compiled translation unit whose
//     only action was the deleted thing. SourceSharp_NotShipped() names it.
//
// ENGINE-FREE ON PURPOSE, for shell_clientdll_refusals.h's reason: the property
// worth checking is a property of the BODY — that a reach counts, that a second
// reach of the same name does not open a second row, that an undeclared name
// does not match a declared one — and a property of a body can only be checked
// by running it. shell_diagnostics.cpp includes cbase.h's whole world and can
// never link into a gtest, so the table and the counter live here and
// sourcesharp/test/shell_notshipped_test.cpp compiles THIS header. Nothing
// there is a stand-in; it is the shipped class.
//
// The WARNING is the one part that needs the engine, so it is DECLARED here and
// defined by whoever links this: tier0's Warning() in shell_diagnostics.cpp, a
// recorder in the test.
//
//=============================================================================//

#pragma once


#include <string.h>

//-----------------------------------------------------------------------------
// The console half. nTotal is the running total ACROSS all rows, so the line a
// reader sees carries the same number the readout will.
//-----------------------------------------------------------------------------
void ShellNotShipped_Warning( const char *pName, const char *pReason, int nTotal );

//-----------------------------------------------------------------------------
// One declared absence.
//-----------------------------------------------------------------------------
struct ShellNotShippedRow_t
{
	const char *pszName;
	const char *pszReason;
};

//-----------------------------------------------------------------------------
// THE DECLARED INTERFACE LIST.
//
// An interface name goes here when a step DELIBERATELY stops exporting it. The
// bar for a row is the lane brief's: the ENGINE was grepped for the name string
// and the call site read, because "nothing in this repo asks for it" is not the
// same question. `IServerTools` has no Linux caller and `PluginHelpersCheck`
// does, and only the engine could say so.
//
// A row is not a licence to be absent — it is a record of an argument. The
// argument for each is in .claude/entity-port/status/7g.md and summarised in the
// reason string, which is what a player actually sees in the console.
//-----------------------------------------------------------------------------
inline const ShellNotShippedRow_t *ShellNotShipped_Interfaces( int *pCount )
{
	static const ShellNotShippedRow_t s_Rows[] =
	{
		// game/client/replay/ — plan_entity.md 7g.
		//
		// The engine DOES compile the request: it asks the client factory
		// for CLIENT_REPLAY_INTERFACE_VERSION and Sys_Errors on NULL. It
		// asks only when replay is supported for the running mod, which it
		// decides by the mod directory against a two-entry list — "tf" and
		// "tf_beta". This mod's directory is
		// `mod_sharp`, so the request is never made. The row exists because
		// that is a MOD-NAME accident: rename the game directory to `tf` and
		// the engine will ask, and it should say why it got nothing rather
		// than read as a broken export.
		{ "ClientReplay001",
		  "replay is not shipped (plan_entity.md 7g); the engine only asks for "
		  "it when the mod directory is tf or tf_beta" },
	};

	if ( pCount != NULL )
		*pCount = (int)( sizeof( s_Rows ) / sizeof( s_Rows[ 0 ] ) );

	return s_Rows;
}

//-----------------------------------------------------------------------------
// The declared row for an interface name, or NULL if the absence is NOT
// declared — which is the whole point of the return type. A NULL here means
// "nobody said this was meant to be missing", and the caller says so louder.
//
// EXACT MATCH, not a prefix. `ClientReplay001` is a SUFFIX of
// `EngineClientReplay001` — the two really do collide, and the linker really
// does tail-merge the second into the first in the shipped engine.so, which is
// how a `strings | grep -x` reading of that binary can report the name absent
// while the code that asks for it is present. A prefix or substring match here
// would answer the wrong row for the wrong question.
//-----------------------------------------------------------------------------
inline const ShellNotShippedRow_t *ShellNotShipped_FindInterface( const char *pszName )
{
	if ( pszName == NULL )
		return NULL;

	int nCount = 0;
	const ShellNotShippedRow_t *pRows = ShellNotShipped_Interfaces( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		if ( strcmp( pRows[ i ].pszName, pszName ) == 0 )
			return &pRows[ i ];
	}

	return NULL;
}

//-----------------------------------------------------------------------------
// One row per name that was actually reached, plus a total.
//
// PER NAME AND NOT JUST A TOTAL, for CShellClientDllRefusals' reason: "did
// anything reach a not-shipped path" is the gate, "WHICH one" is the finding,
// and a finding that cannot name the capability is not one anybody can act on.
//
// Matched by strcmp rather than by pointer identity. The names are string
// literals in a handful of translation units today, so identity would work
// today; it would stop working silently the moment one moved, and the failure
// would be a second row for the same name rather than an error.
//-----------------------------------------------------------------------------
class CShellNotShipped
{
public:
	enum { kMaxNames = 32 };

	CShellNotShipped() : m_nTotal( 0 ), m_nNames( 0 ) {}

	//-------------------------------------------------------------------------
	// Something reached a path this build does not ship. Warn, and remember.
	//
	// WARNED ONCE PER NAME, and that is the difference from the client shell's
	// refusals, which warn every time. Those sit on vtable slots the engine
	// enters at most a handful of times; these sit on paths a stock translation
	// unit can take EVERY FRAME (view.cpp's HLTV branch is per view setup), so
	// an unlimited warning would be a per-frame console flood that buries the
	// first line — the one worth reading. The COUNT is unlimited, so nothing is
	// lost: the readout says how many times, the console says it happened.
	//-------------------------------------------------------------------------
	void Reached( const char *pName, const char *pReason )
	{
		m_nTotal++;

		bool bFirst = true;

		if ( pName != NULL )
		{
			int iRow = Find( pName );

			if ( iRow < 0 && m_nNames < kMaxNames )
			{
				iRow = m_nNames++;
				m_pNames[ iRow ] = pName;
				m_pReasons[ iRow ] = pReason;
				m_nCounts[ iRow ] = 0;
			}

			// A table that overflowed still counts the total, which is the
			// gate. Losing the row is better than losing the number, and an
			// overflowed name warns every time rather than never.
			if ( iRow >= 0 )
			{
				bFirst = ( m_nCounts[ iRow ] == 0 );
				m_nCounts[ iRow ]++;
			}
		}

		if ( bFirst )
			ShellNotShipped_Warning( pName, pReason, m_nTotal );
	}

	// THE GATE. Zero is the evidence; anything else is a finding, and
	// NameAt/CountAt say which.
	int Total() const { return m_nTotal; }

	int NameCount() const { return m_nNames; }

	const char *NameAt( int i ) const
	{
		return ( i >= 0 && i < m_nNames ) ? m_pNames[ i ] : NULL;
	}

	const char *ReasonAt( int i ) const
	{
		return ( i >= 0 && i < m_nNames ) ? m_pReasons[ i ] : NULL;
	}

	int CountAt( int i ) const
	{
		return ( i >= 0 && i < m_nNames ) ? m_nCounts[ i ] : 0;
	}

	int CountOf( const char *pName ) const
	{
		const int iRow = Find( pName );
		return ( iRow < 0 ) ? 0 : m_nCounts[ iRow ];
	}

private:
	int Find( const char *pName ) const
	{
		if ( pName == NULL )
			return -1;

		for ( int i = 0; i < m_nNames; i++ )
		{
			if ( strcmp( m_pNames[ i ], pName ) == 0 )
				return i;
		}

		return -1;
	}

	int          m_nTotal;
	int          m_nNames;
	const char  *m_pNames[ kMaxNames ];
	const char  *m_pReasons[ kMaxNames ];
	int          m_nCounts[ kMaxNames ];
};
