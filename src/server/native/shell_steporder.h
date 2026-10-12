//=============================================================================//
// THE TRANSMIT-SET RECORDER -- plan_server.md's "Gate 5 -- Step order and
// transmit-set equivalence", and docs/plan-audit.md item C2.
//
//     For CheckTransmit, record the set of edict indices marked for transmit
//     for one client on one frame, before and after, and diff that too.
//
// WHAT USED TO BE HERE TOO. Gate 5 had a second recorder, CShellStepOrder: a
// ring buffer every frame and level step appended its name to, so that the
// native and managed step orders could be diffed. It had no reader. The
// `tools/gate5` that was to diff it and the `docs/gate5/*` baselines it was to
// diff against were never written, and nothing else in the tree -- no tool, no
// managed caller, no config -- ever ran `cs_sv_steporder`. It is gone, along
// with its 41 call sites.
//
// WHY THIS HALF STAYS. It has a reader. tools/p4-exit-probe-skybox drives
// `cs_sv_transmitset arm` and reads the `skyarea=%d` this records -- it is the
// A/B for the 3D-skybox area bug, run with `--expect 0` against a reverted
// build to prove the probe can fail. sourcesharp/test/client_data_walk_test.cpp
// pins that coupling.
//
// SO THIS IS AN INSTRUMENT, NOT AN ASSERTION. It records; the probe reads. A
// gtest over this header checks the recorder, which is the part that can be
// wrong without anybody noticing.
//
// ENGINE-FREE, for shell_gamedll_refusals.h's reason and not by taste. What
// matters here are properties of BODIES -- that a one-shot capture is one-shot,
// that out-of-range marks are dropped rather than corrupting -- and a property
// of a body can only be checked by running it. shell_gamedll.cpp drags in
// cbase.h and gameinterface.h and can never be linked into a gtest, so the
// recorder lives here and sourcesharp/test/shell_steporder_test.cpp compiles
// THIS header. Nothing there is a stand-in: it is the shipped class.
//
// NO FILE-SCOPE STATE. The class is a FIELD of CShellGameDll, the object the
// engine holds exactly one of -- the arrangement the refusal counter already
// uses, and the one tools/statics-audit exists to keep.
//=============================================================================//

#pragma once

#include <string.h>

//-----------------------------------------------------------------------------
// THE TRANSMIT SET -- one client, one frame.
//
// ONE SHOT, and that is the design rather than an economy. CheckTransmit runs
// once per client per frame; a recorder that stayed armed would record the LAST
// frame before the dump rather than a frame anybody chose, and two runs of the
// same gate would compare two different frames and call the difference a
// regression. Arm() sets a flag that the first capture clears, so what is read
// back is the first CheckTransmit after the arm and nothing else.
//
// THE RECIPIENT IS RECORDED WITH THE SET. "The transmit set for one client" is
// not a set until you know which client, and on a listen server with one player
// there are still two edicts that reach here (the player, and edict 0's
// fallback recipient before he spawns). A baseline that did not name its
// recipient would be compared against a different client's answer without a
// word.
//
// A BITSET AND NOT A LIST because the source is one: pInfo->m_pTransmitEdict is
// a CBitVec over MAX_EDICTS. Copying set bits into a list would impose an order
// the source does not have and make the gate sensitive to the walk order rather
// than to the SET, which is the noun in Gate 5's own sentence.
//-----------------------------------------------------------------------------
class CShellTransmitSet
{
public:
	// MAX_EDICTS is 2048 and is engine-side; naming it here would drag const.h
	// into a header whose whole point is that it needs nothing. The capture
	// site asserts the two agree.
	enum { kMaxEdicts = 2048, kWords = kMaxEdicts / 32 };

	// Arm for the next CheckTransmit, or for the next one whose recipient is
	// nOnlyRecipient (a two-client gate asks about the second client).
	void Arm( int nOnlyRecipient = -1 ) { m_bArmed = true; m_nOnlyRecipient = nOnlyRecipient; }
	bool Armed() const { return m_bArmed; }
	bool HaveCapture() const { return m_bCaptured; }
	int  Recipient() const { return m_nRecipient; }
	int  Count() const { return m_nCount; }

	//-------------------------------------------------------------------------
	// THE SKYBOX AREA THE WALK WAS GIVEN, and it is recorded because a mutation
	// proved the set alone does not cover it.
	//
	// plan-audit C4 moved "which hierarchy answers for this client, and what its
	// 3D-skybox area is" into C#. The obvious gate for that is this transmit
	// set: skyBoxArea decides `bSameAreaAsSky`, which SetTransmits an entity
	// unconditionally. So the crossing was landed and then mutated -- the
	// managed answer perturbed by +1 -- and tools/gate5 STAYED GREEN. On
	// dm_lockdown at the recorded vantage, no entity's AreaNum() equals either
	// the real area or the wrong one, so the set is identical and the gate could
	// not see a wrong answer at all.
	//
	// That is a property of the MAP, not of the crossing, and the next map would
	// have hidden a different thing. So the input is recorded beside the output:
	// a wrong area now fails on the area, whether or not it moved a single bit.
	//-------------------------------------------------------------------------
	int  SkyboxArea() const { return m_nSkyboxArea; }
	void SetSkyboxArea( int nArea ) { m_nSkyboxArea = nArea; }

	//-------------------------------------------------------------------------
	// Begin a capture if one is wanted. Returns false when the caller should do
	// nothing -- which is every frame but one, so the call site's cost is a
	// bool test.
	//-------------------------------------------------------------------------
	bool BeginCapture( int nRecipientEdict )
	{
		if ( !m_bArmed || ( m_nOnlyRecipient >= 0 && nRecipientEdict != m_nOnlyRecipient ) )
			return false;

		m_bArmed = false;
		m_bCaptured = true;
		m_nRecipient = nRecipientEdict;
		m_nCount = 0;
		m_nSkyboxArea = 0;
		memset( m_Words, 0, sizeof( m_Words ) );
		return true;
	}

	void Mark( int iEdict )
	{
		if ( iEdict < 0 || iEdict >= kMaxEdicts )
			return;
		const int w = iEdict / 32;
		const unsigned int bit = 1u << ( iEdict % 32 );
		if ( ( m_Words[ w ] & bit ) == 0 )
		{
			m_Words[ w ] |= bit;
			m_nCount++;
		}
	}

	bool IsSet( int iEdict ) const
	{
		if ( iEdict < 0 || iEdict >= kMaxEdicts )
			return false;
		return ( m_Words[ iEdict / 32 ] & ( 1u << ( iEdict % 32 ) ) ) != 0;
	}

private:
	unsigned int m_Words[ kWords ] = {};
	int          m_nRecipient = -1;
	int          m_nCount = 0;
	int          m_nSkyboxArea = 0;
	bool         m_bArmed = false;
	int          m_nOnlyRecipient = -1;
	bool         m_bCaptured = false;
};

//-----------------------------------------------------------------------------
// THE SEAM. CheckTransmit is a free function in shell_gameents.cpp; the
// recorder is a FIELD of CShellGameDll in shell_gamedll.cpp. These three are
// declared here and defined by whoever links the header -- the shell in the
// game, a recorder in the gtest -- which is the same arrangement
// ShellGameDll_RefusalWarning uses and for the same reason.
//
// A FREE FUNCTION PER CAPTURE SITE rather than reaching for the singleton,
// because shell_gameents.cpp must not know that CShellGameDll exists: a step
// that can see the game-DLL object is one edit away from becoming a decision.
//-----------------------------------------------------------------------------
bool ShellGameDll_TransmitCaptureBegin( int nRecipientEdict );
void ShellGameDll_TransmitCaptureMark( int iEdict );
void ShellGameDll_TransmitCaptureSkyboxArea( int nArea );
