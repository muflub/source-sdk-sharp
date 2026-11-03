//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// mat_stub's swap: while it is on, everything that holds the client's material system slot
// draws through the stub. Header-only, and over what it calls, so a test can drive it.

#pragma once

template <class TMaterialSystem, class TStub, class TEngine>
class CMatStubSwap
{
public:
	// pppSlots: every place the material system is read from, none of them NULL.
	void Begin(TMaterialSystem** const* pppSlots, int nSlots, TStub* pStub, TEngine* pEngine)
	{
		m_pppSlots = pppSlots;
		m_nSlots = nSlots;
		m_pEngine = pEngine;
		m_pOld = *pppSlots[0];

		pStub->SetRealMaterialSystem(m_pOld);
		m_pOld->SetInStubMode(true);
		Point(pStub);
		pEngine->Mat_Stub(pStub);
	}

	void End()
	{
		if (!m_pOld)
			return;

		Point(m_pOld);
		m_pOld->SetInStubMode(false);
		m_pEngine->Mat_Stub(m_pOld);
		m_pOld->ClearBuffers(true, true);
		m_pOld = nullptr;
	}

private:
	void Point(TMaterialSystem* pSystem)
	{
		for (int i = 0; i < m_nSlots; i++)
			*m_pppSlots[i] = pSystem;
	}

	TMaterialSystem** const* m_pppSlots = nullptr;
	int m_nSlots = 0;
	TEngine* m_pEngine = nullptr;
	TMaterialSystem* m_pOld = nullptr;
};
