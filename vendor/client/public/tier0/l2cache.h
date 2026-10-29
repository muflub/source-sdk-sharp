//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

class P4Event_BSQ_cache_reference;

class CL2Cache
{
public:
	CL2Cache();
	~CL2Cache();

	void End();

	// GetL2CacheMisses
	int GetL2CacheMisses() { return m_iL2CacheMissCount; }

#ifdef DBGFLAG_VALIDATE
	void Validate(CValidator& validator, tchar* pchName); // Validate our internal structures
#endif // DBGFLAG_VALIDATE

private:
	alignas(8) unsigned char unused_m_nID[32];
	int m_iL2CacheMissCount;
};
