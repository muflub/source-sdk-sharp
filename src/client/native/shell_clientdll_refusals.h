//=============================================================================//
// THE CLIENT'S REFUSAL COUNTER -- plan_entity.md 5b.
//
// THE SERVER'S SHAPE, AND THE SERVER'S COPY IS GONE.
// sourcesharp/game/server/shell_gamedll_refusals.h was this file's original and
// CShellGameDllRefusals was the class it mirrors method for method. That header
// and that class have since been DELETED: the server's counter had no reader --
// nothing in tools/, docs/, the managed tree or game/mod_sharp/cfg/ ever ran
// `cs_sv_refusals`, and the table's own two readouts were the only things that
// read the table. sourcesharp/test/shell_gamedll_refusals_test.cpp records the
// removal and is the arrangement half only now.
//
// THIS SIDE'S COUNTER IS NOT IN THAT POSITION AND STAYS. It has a live reader:
// `cs_cl_refusals` (shell_clientdll.cpp:2872) and the DLL's own shutdown
// readout, and the client's refusal tally is what plan_entity.md 5b's gate
// sentence is about -- "no row reads refused=N with N above zero".
//
// The shape the user asked for is unchanged: IBaseClientDLL is implemented the
// way IServerGameDLL is, and the refusals mechanism is the half of that shape
// which is about BEHAVIOUR rather than about where a class is declared.
//
// The rule both sides answer is plan_server.md Part 5's:
//
//     a stub that is never called and a stub that is called constantly look
//     identical from outside
//
// so every refusal warns and counts. The WARNING is what a person sees the
// first time; the COUNT is what makes "it was never called" an answer somebody
// can produce after a map has been played, rather than an absence of evidence.
// plan_entity.md §7 says the same thing in its own words -- "a behaviour that
// is refused is a ContractBaseline entry with a reason, never a silent no-op".
//
// THE "PROMOTE IT TO game/shared" ITEM IS CLOSED, AND NOT BY DOING IT.
// plan_shim.md §6 lists "shell_clientdll_refusals.h + the server twin -> one
// shared header". Re-measured by lane 4a-clientshell: THERE IS NO TWIN. The
// server's header was deleted with its unread counter, so there is one copy of
// this class in the tree and a shared header would have exactly one includer.
// A one-includer header in game/shared is a worse end state than a
// one-includer header beside its includer, so this file stays where it is.
//
// The original reason for the second copy, kept because it is still why a
// promotion would cost anything: sourcesharp/game/server/ is not on the
// client's include path -- client_csharp.vpc lists host, game/shared and
// game/client and no more.
//
// WHY A CLASS AND NOT A `static int` AT FILE SCOPE. The project rule: no
// file-scope statics or globals in SourceSharp's C++. The counter is a FIELD,
// the one instance is a member of CShellClientDLL -- the object that owns the
// refusals and of which the engine holds exactly one -- and the call sites read
// as the server's do, because CShellClientDLL keeps a one-line private
// ShellClientDll_Unreachable that forwards to its field.
//
// ENGINE-FREE, for the server header's reason rather than by taste. The
// property that matters is a property of the BODY -- that a call increments,
// that a second call to the same method does not open a second row -- and a
// property of a body can only be checked by running it. shell_clientdll.cpp
// includes cbase.h's whole world and can never be linked into a gtest, so the
// counter lives here and sourcesharp/test/shell_clientdll_refusals_test.cpp
// compiles THIS header. Nothing there is a stand-in: it is the shipped class.
//
// The warning itself is the one thing that needs the engine, so it is DECLARED
// here and defined by whoever links this: tier0's Warning() in
// shell_clientdll.cpp, a recorder in the test.
//=============================================================================//

#pragma once


#include <string.h>

//-----------------------------------------------------------------------------
// The console half of a refusal. Declared, not defined -- see the header
// comment. nTotal is the running total ACROSS all refusals, so the line a
// reader sees carries the same number the counter readout will.
//-----------------------------------------------------------------------------
void ShellClientDll_RefusalWarning( const char *pMethod, int nTotal );

//-----------------------------------------------------------------------------
// A refused method of an interface this DLL implements OUTSIDE
// CShellClientDLL -- the particles shell's IParticleSystemQuery (lane
// c5-particles) -- counted on the same tally, so cs_cl_refusals and
// /client/{slot}/absences see it. Defined in shell_clientdll.cpp.
//-----------------------------------------------------------------------------
void ShellClientDll_RefuseExternal( const char *pMethod );

//-----------------------------------------------------------------------------
// One row per method that actually fired, plus a total.
//
// PER METHOD AND NOT JUST A TOTAL, because the two questions are different
// ones. "Did any refusal fire" is the gate; "WHICH one" is the finding, and a
// finding that cannot name the method is not one anybody can act on -- each
// refusal is its own argument and only the wrong one has to be retracted.
//
// Matched by strcmp rather than by pointer identity, as the server's is. The
// names are string literals in one translation unit today, so identity would
// work today; it would stop working silently the moment one moved, and the
// failure would be a second row for the same method rather than an error.
//-----------------------------------------------------------------------------
class CShellClientDllRefusals
{
public:
	enum { kMaxMethods = 24 };

	//-------------------------------------------------------------------------
	// A refusal fired. Warn, and remember that it did.
	//
	// The warning is NOT rate limited, and that is deliberate: the claim about
	// a refused method is that it is unreachable in this build, so the expected
	// number of lines is zero, and a flood is that claim being wrong in the
	// loudest available way. None of the client's seven refusals is on a
	// per-frame path -- two are TF-only chat methods and five are the console-style
	// matchmaking and rich presence methods step 7g refused when it deleted
	// basepresence.cpp -- so a flood is not a thing this can produce without
	// something being badly wrong, which is what a refusal is for.
	//-------------------------------------------------------------------------
	void Unreachable( const char *pMethod )
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

			// A table that overflowed still counts the total, which is the
			// gate. Losing the row is better than losing the number.
			if ( iRow >= 0 )
				m_nCounts[ iRow ]++;
		}

		ShellClientDll_RefusalWarning( pMethod, m_nTotal );
	}

	// THE GATE. Zero is the evidence; anything else is a finding, and
	// MethodAt/CountAt say which.
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
