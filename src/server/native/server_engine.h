// The engine interfaces the server acquires in DLLInit, held by the root's DLL tier
// (CServerEngineInterfaces in server_state.h). Valid from DLLInit to DLLShutdown.

#pragma once

class IEngineTrace;
class IFileSystem;
class IGameEventManager2;
class IUniformRandomStream;
class IVDebugOverlay;
class IMDLCache;
class INetworkStringTableContainer;
class ISpatialPartition;
class IStaticPropMgrServer;
class IUploadGameStats;
class IVModelInfo;

IFileSystem *ServerFileSystem();
ISpatialPartition *ServerPartition();
IUploadGameStats *ServerGameStats();
INetworkStringTableContainer *ServerStringTables();
IVModelInfo *ServerModelInfo();
IStaticPropMgrServer *ServerStaticProps();
IEngineTrace *ServerTrace();
IMDLCache *ServerMdlCache();
IGameEventManager2 *ServerEvents();
IVDebugOverlay *ServerOverlay();
IUniformRandomStream *ServerRandom();
