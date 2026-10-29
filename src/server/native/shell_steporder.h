// Records the set of edicts CheckTransmit marks for one client on one frame, and
// the skybox area it was given, for cs_sv_transmitset. Engine-free so a gtest can
// compile it; it lives as a field of CShellGameDll, never as file-scope state.

#pragma once

#include <string.h>

// One client, one frame. Arm() is one-shot: the first matching CheckTransmit after
// it is captured, so a reader gets a frame it chose, not the last one.
class CShellTransmitSet
{
public:
	// MAX_EDICTS; the capture site asserts the two agree.
	enum
	{
		kMaxEdicts = 2048,
		kWords = kMaxEdicts / 32
	};

	// Arms for the next CheckTransmit, or the next whose recipient is nOnlyRecipient.
	void Arm(int nOnlyRecipient = -1)
	{
		m_bArmed = true;
		m_nOnlyRecipient = nOnlyRecipient;
	}

	bool Armed() const { return m_bArmed; }

	bool HaveCapture() const { return m_bCaptured; }

	int Recipient() const { return m_nRecipient; }

	int Count() const { return m_nCount; }

	// The skybox area is recorded beside the set, since a wrong area often
	// leaves the set unchanged.
	int SkyboxArea() const { return m_nSkyboxArea; }

	void SetSkyboxArea(int nArea) { m_nSkyboxArea = nArea; }

	// Begins a capture if one is armed; false on every other frame.
	bool BeginCapture(int nRecipientEdict)
	{
		if (!m_bArmed || (m_nOnlyRecipient >= 0 && nRecipientEdict != m_nOnlyRecipient))
			return false;

		m_bArmed = false;
		m_bCaptured = true;
		m_nRecipient = nRecipientEdict;
		m_nCount = 0;
		m_nSkyboxArea = 0;
		memset(m_Words, 0, sizeof(m_Words));
		return true;
	}

	void Mark(int iEdict)
	{
		if (iEdict < 0 || iEdict >= kMaxEdicts)
			return;
		const int w = iEdict / 32;
		const unsigned int bit = 1u << (iEdict % 32);
		if ((m_Words[w] & bit) == 0)
		{
			m_Words[w] |= bit;
			m_nCount++;
		}
	}

	bool IsSet(int iEdict) const
	{
		if (iEdict < 0 || iEdict >= kMaxEdicts)
			return false;
		return (m_Words[iEdict / 32] & (1u << (iEdict % 32))) != 0;
	}

private:
	unsigned int m_Words[kWords] = {};
	int m_nRecipient = -1;
	int m_nCount = 0;
	int m_nSkyboxArea = 0;
	bool m_bArmed = false;
	int m_nOnlyRecipient = -1;
	bool m_bCaptured = false;
};
