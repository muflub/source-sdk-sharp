//========= SourceSharp — a refused physics-bridge path, said once and counted ==//
//
// WHY THIS EXISTS. plan_entity.md §7 ends with the rule this header serves:
//
//     "Deletion instead of porting. The temptation on every batch is a stub.
//      The stub files must be empty at the phase exit, and a behaviour that is
//      refused is a `ContractBaseline` entry with a reason, never a silent
//      no-op."
//
// `game/client/physics.cpp` and `game/server/physics.cpp` are §6 KEEPS on both
// sides, and each still calls into the hierarchy that leaves. Some of those
// calls have a native answer and are rewritten to use it — the watcher test
// asks `EntityDataObject_Has`, the move-tree root walks `move_root.h`. The rest
// are the SDK's OWN bookkeeping reached from the bridge: the contact touchlink
// list, the timed debug overlay, the entity-list walk behind a developer
// console command. Those have no subject once the hierarchy goes, and the honest
// edit is a refusal.
//
// THE FAILURE THIS PREVENTS, WHICH THIS PORT HAS ALREADY MADE ONCE. A refused
// path written as `(void)pEntity;` and a path that simply found nothing to do
// are byte-identical from outside. `util_shared.cpp:461` states it for a trace:
// "a trace that was not shortened and a trace that could not be shortened are
// the same `tr` and different facts." A mod that starts relying on one of these
// gets silence instead of an answer, and no gate in this tree can see it.
//
// AND THE SECOND FAILURE, WHICH IS THIS ONE'S MIRROR. Every refusal in this
// lane is argued to be UNREACHABLE after the flip — no entity on the surviving
// hierarchy can reach it. An argument is not a measurement, and
// `a-few-green-runs-are-not-absence` is the recorded way that reasoning goes
// wrong. So this type COUNTS the reaches as well as printing the first one:
// `ReachCount()` is a number a live run can be asked for, and a refusal argued
// vacuous whose count is not zero is a wrong argument rather than an
// inconvenience. That is what turns "nothing can reach this" from a claim into
// a gate.
//
// WHY IT IS ENGINE-FREE. `Warning()` is variadic, tier0's, and cannot be
// captured by a gtest. So the RULE — the once-only latch, the count, and the
// two strings — lives here and is unit-tested in
// `sourcesharp/test/physics_bridge_refusal_test.cpp`; the PRINT stays at the
// call site, where the message can name the site's own locals. Same split, and
// the same reason, as `move_root.h` and `said_once_per_id.h`.
//
//=============================================================================//
#ifndef SOURCESHARP_PHYSICS_BRIDGE_REFUSAL_H
#define SOURCESHARP_PHYSICS_BRIDGE_REFUSAL_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// One refused path. Constructed as a function-local static at the site, so
// there is exactly one per path and no file-scope global (plan.md §1a).
//
//   What()     the capability, in the words a reader of the log would use.
//   Refusal()  why it cannot cross, and what would have to exist to change the
//              answer. §7's "with a reason" is this string, and a refusal
//              constructed with an empty one is a no-op wearing a type.
//   Reached()  records that the refused path was entered, and answers true
//              EXACTLY ONCE so the caller prints once. Always call it; never
//              guard it behind the print.
//-----------------------------------------------------------------------------
class CPhysicsBridgeRefusal
{
public:
	CPhysicsBridgeRefusal( const char *pszWhat, const char *pszRefusal )
		: m_pszWhat( pszWhat ), m_pszRefusal( pszRefusal ), m_nReached( 0 )
	{
	}

	// True the first time only. The count keeps rising afterwards, which is the
	// half that can be measured on a live run.
	bool Reached()
	{
		m_nReached++;
		return ( m_nReached == 1 );
	}

	// How many times the refused path was entered this session. Zero is the
	// number every refusal in this lane predicts after the flip.
	int ReachCount() const { return m_nReached; }

	const char *What() const { return m_pszWhat ? m_pszWhat : ""; }
	const char *Refusal() const { return m_pszRefusal ? m_pszRefusal : ""; }

	// A refusal has to say WHY. This is what a test asks so that a future edit
	// cannot quietly turn one back into a stub by emptying the string.
	bool IsDeclared() const
	{
		return m_pszWhat != 0 && m_pszWhat[ 0 ] != '\0'
		    && m_pszRefusal != 0 && m_pszRefusal[ 0 ] != '\0';
	}

private:
	const char *m_pszWhat;
	const char *m_pszRefusal;
	int m_nReached;
};

//-----------------------------------------------------------------------------
// The one spelling of the print, so every refusal in the two bridge files reads
// the same way in a log and a grep for one finds them all.
//
// Used as:
//
//     static CPhysicsBridgeRefusal s_Refuse( "<what>", "<why, and what would change it>" );
//     if ( s_Refuse.Reached() )
//         Warning( SOURCESHARP_PHYSICS_REFUSAL_FMT, s_Refuse.What(), s_Refuse.Refusal() );
//
// A macro rather than a function because `Warning` is tier0's variadic and this
// header does not include tier0.
//-----------------------------------------------------------------------------
#define SOURCESHARP_PHYSICS_REFUSAL_FMT \
	"SourceSharp: %s is REFUSED in this build (plan_entity.md §7). %s\n"

#endif // SOURCESHARP_PHYSICS_BRIDGE_REFUSAL_H
