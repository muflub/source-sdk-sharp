// A refused physics-bridge path, printed once and counted, so a refusal is never a
// silent no-op and a live run can say whether one was reached. Engine-free; the print
// stays at the call site because Warning() is tier0's.
#pragma once

// One refused path, held as a function-local static at its site. Always call Reached();
// it answers true exactly once so the caller prints once.
class CPhysicsBridgeRefusal
{
public:
	CPhysicsBridgeRefusal(const char* pszWhat, const char* pszRefusal)
		: m_pszWhat(pszWhat)
		, m_pszRefusal(pszRefusal)
		, m_nReached(0)
	{
	}

	bool Reached()
	{
		m_nReached++;
		return (m_nReached == 1);
	}

	// How many times the refused path was entered this session.
	int ReachCount() const { return m_nReached; }

	const char* What() const { return m_pszWhat ? m_pszWhat : ""; }

	const char* Refusal() const { return m_pszRefusal ? m_pszRefusal : ""; }

	// A refusal must name what and why.
	bool IsDeclared() const
	{
		return m_pszWhat != 0 && m_pszWhat[0] != '\0' && m_pszRefusal != 0 && m_pszRefusal[0] != '\0';
	}

private:
	const char* m_pszWhat;
	const char* m_pszRefusal;
	int m_nReached;
};

// The one print format, used as:
//     if ( s_Refuse.Reached() )
//         Warning( SOURCESHARP_PHYSICS_REFUSAL_FMT, s_Refuse.What(), s_Refuse.Refusal() );
#define SOURCESHARP_PHYSICS_REFUSAL_FMT "SourceSharp: %s is REFUSED in this build (plan_entity.md §7). %s\n"
