// The client DLL's refusal counter: every refused method warns and counts, per
// method and in total. Engine-free so a gtest can compile the shipped class;
// the warning is declared here and defined by whoever links it.

#pragma once

#include <string.h>

// The console half of a refusal. nTotal is the running total across all refusals.
void ShellClientDll_RefusalWarning(const char* pMethod, int nTotal);

// A refused method of an interface implemented outside CShellClientDLL,
// counted on the same tally. Defined in shell_clientdll.cpp.
void ShellClientDll_RefuseExternal(const char* pMethod);

// One row per method that fired, plus a total. Rows match by strcmp, not
// pointer identity.
class CShellClientDllRefusals
{
public:
	enum
	{
		kMaxMethods = 24
	};

	// Warns and counts. Not rate limited: a refused method is expected never to run.
	void Unreachable(const char* pMethod)
	{
		m_nTotal++;

		if (pMethod != NULL)
		{
			int iRow = Find(pMethod);

			if (iRow < 0 && m_nMethods < kMaxMethods)
			{
				iRow = m_nMethods++;
				m_pMethods[iRow] = pMethod;
				m_nCounts[iRow] = 0;
			}

			// An overflowed table still counts the total.
			if (iRow >= 0)
				m_nCounts[iRow]++;
		}

		ShellClientDll_RefusalWarning(pMethod, m_nTotal);
	}

	int Total() const { return m_nTotal; }

	int MethodCount() const { return m_nMethods; }

	const char* MethodAt(int i) const { return (i >= 0 && i < m_nMethods) ? m_pMethods[i] : NULL; }

	int CountAt(int i) const { return (i >= 0 && i < m_nMethods) ? m_nCounts[i] : 0; }

	int CountOf(const char* pMethod) const
	{
		const int iRow = Find(pMethod);
		return (iRow < 0) ? 0 : m_nCounts[iRow];
	}

private:
	int Find(const char* pMethod) const
	{
		if (pMethod == NULL)
			return -1;

		for (int i = 0; i < m_nMethods; i++)
		{
			if (m_pMethods[i] != NULL && strcmp(m_pMethods[i], pMethod) == 0)
				return i;
		}

		return -1;
	}

	int m_nTotal = 0;
	int m_nMethods = 0;
	const char* m_pMethods[kMaxMethods] = {};
	int m_nCounts[kMaxMethods] = {};
};
