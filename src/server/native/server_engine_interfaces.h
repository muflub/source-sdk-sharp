// The engine interfaces the server acquires in DLLInit (SourceSharp_AcquireEngineInterfaces),
// held by the root's DLL tier; valid until DLLShutdown.

#pragma once

#include "ss_init_factory.h"

class CSharedEdictChangeInfo;
class IEngineTrace;
class IBaseFileSystem;
class IFileSystem;
class IGameEventManager2;
class IMDLCache;
class INetworkStringTableContainer;
class ISpatialPartition;
class IStaticPropMgrServer;
class IUniformRandomStream;
class IUploadGameStats;
class IVDebugOverlay;
class IVEngineServer;
class IVModelInfo;

class CServerEngineInterfaces
{
public:
	IVEngineServer *m_pEngine = nullptr;
	IFileSystem *m_pFileSystem = nullptr;
	IBaseFileSystem *m_pBaseFileSystem = nullptr; // m_pFileSystem's base, for the open/read/close callers
	ISpatialPartition *m_pPartition = nullptr;
	IUploadGameStats *m_pGameStats = nullptr;
	INetworkStringTableContainer *m_pStringTables = nullptr;
	IVModelInfo *m_pModelInfo = nullptr;
	IStaticPropMgrServer *m_pStaticProps = nullptr;
	IEngineTrace *m_pTrace = nullptr;
	IMDLCache *m_pMdlCache = nullptr;
	IGameEventManager2 *m_pEvents = nullptr;
	IVDebugOverlay *m_pOverlay = nullptr;
	IUniformRandomStream *m_pRandom = nullptr;
	CSharedEdictChangeInfo *m_pChangeInfo = nullptr;

	// The factories the engine handed DLLInit, kept for physics and for managed's acquisitions.
	ss::factorylist_t m_Factories{};
};
