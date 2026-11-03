//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// worldspawn's ClientClass, declared from C#. The two sides share the
// declaration (SourceSharp.Shared.WorldNetState) and agree on word indices,
// not offsets: each resolves the props against its own class's sizeof.

#pragma once

#include "bound_callback.h"
#include "basehandle.h"

class C_World;
class CClientEngineInterfaces;
struct CManagedHoldersClient;
class CManagedSide;
class ClientClass;
class IClientNetworkable;
class IClientUnknown;
class IHandleEntity;
class RecvProp;
class RecvTable;

// Where the world's blob sits inside a C_World, defined beside the class in world_adapter_client.cpp.
int SourceSharp_WorldBlobOffset();

// The client's world: the one C_World, reused for every map and freed at DLL shutdown, and its
// ClientClass, whose create callback is bound to this object.
class CWorldClient final
{
public:
	CWorldClient(const CManagedHoldersClient& managed, const CClientEngineInterfaces& engines,
				 const CManagedSide& managedSide);

	CWorldClient(const CWorldClient&) = delete;
	CWorldClient& operator=(const CWorldClient&) = delete;

	void CreateEntity();
	void DestroyEntity();

	// The blob's length: C#'s, and zero until WorldNetDeclare has run.
	int BlobBytes() const { return m_nBlobBytes; }

	// C# declares worldspawn's blob size; this builds the RecvTable and ClientClass around it.
	// Runs before the C_World is allocated, so its operator new knows the blob length. Returns
	// the payload word count, or -1 on refusal.
	int32_t WorldNetDeclare(int32_t nBlobBytes);

	// The class C_World::GetClientClass() hands the engine. NULL until C# has declared, which
	// drops the world's updates.
	ClientClass* Class();

	C_World* Entity() const { return m_pWorld; }

	// The world as a material-proxy binding; the material system takes it as `void *`.
	void* Renderable() const;

	// Unbinds the world from the level; guarded on the binding, so a second call is a no-op.
	bool LevelShutdown();

	// Invalid between maps: worldspawn is not in the slot table.
	CBaseHandle Handle() const;
	IClientUnknown* FromIndex(int nEntry) const;
	IClientUnknown* FromHandle(const CBaseHandle& hEnt) const;
	bool IsTheWorld(const IHandleEntity* pHandleEntity) const;

	// The map's detail-sprite material, read from the blob. NULL when there is no world, no
	// declaration, or the map left `detailmaterial` empty; the caller then uses its own default.
	const char* DetailSpriteMaterial() const;

private:
	typedef CBoundCallback<IClientNetworkable*(int, int)> CreateFn_t;

	// The ClientClass's factory. It returns the object itself: the pointer the engine keeps must
	// be the one trace_t::m_pEnt holds.
	IClientNetworkable* Create(int nEntNum, int nSerialNum);

	const CManagedHoldersClient& m_Managed;
	const CClientEngineInterfaces& m_Engines;
	const CManagedSide& m_ManagedSide;
	CreateFn_t m_CreateFn;
	C_World* m_pWorld = nullptr;
	int m_nBlobBytes = 0;
	int m_nWords = 0;
	RecvProp* m_pProps = nullptr;
	RecvTable* m_pTable = nullptr;
	ClientClass* m_pClass = nullptr;
	bool m_bWarnedUndeclared = false;
};
