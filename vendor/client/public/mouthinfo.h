//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

class CAudioSource;

#pragma pack(push, 4)

class CVoiceData
{
public:
	CVoiceData()
	{
		m_flElapsed = 0.0f;
		m_pAudioSource = NULL;
		m_bIgnorePhonemes = false;
	}

private:
	float m_flElapsed;
	CAudioSource* m_pAudioSource;
	bool m_bIgnorePhonemes;
};

// Purpose: Describes position of mouth for lip syncing
class CMouthInfo
{
public:
	// 0 = mouth closed, 255 = mouth agape
	byte mouthopen;
	// counter for running average
	byte sndcount;
	// running average
	int sndavg;

public:
	CMouthInfo()
	{
		m_nVoiceSources = 0;
		m_needsEnvelope = false;
	}

	virtual ~CMouthInfo() { ClearVoiceSources(); }

	int GetNumVoiceSources();

	void ClearVoiceSources();

	bool IsActive();

private:
	enum
	{
		MAX_VOICE_DATA = 4
	};

	short m_nVoiceSources;
	short m_needsEnvelope;
	CVoiceData m_VoiceSources[MAX_VOICE_DATA];
};

#pragma pack(pop)

inline bool CMouthInfo::IsActive()
{
	return (GetNumVoiceSources() > 0) ? true : false;
}

inline int CMouthInfo::GetNumVoiceSources()
{
	return m_nVoiceSources;
}

inline void CMouthInfo::ClearVoiceSources()
{
	m_nVoiceSources = 0;
}
