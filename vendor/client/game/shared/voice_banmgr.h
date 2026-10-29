//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// This class manages the (persistent) list of squelched players.
class CVoiceBanMgr
{
public:
	CVoiceBanMgr();
	~CVoiceBanMgr();

	// Init loads the list of squelched players from disk.
	bool Init(const char* pGameDir);

protected:
	class BannedPlayer
	{
	public:
		char m_PlayerID[SIGNED_GUID_LEN];
		BannedPlayer *m_pPrev, *m_pNext;
	};

	void Clear();

protected:
	BannedPlayer m_PlayerHash[256];
};
