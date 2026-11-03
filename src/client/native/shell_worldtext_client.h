//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The managed point_worldtext renderables as one object, a member of the client DLL object:
// one per owner, each kept at its owner's place every frame by the client systems row. Its
// IShimParticlesClient slots are the mesh's.

#pragma once

#include <stdint.h>

#include "utlvector.h"

#include "bound_callback.h"
#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CManagedWorldText;
class CShellRenderable;
class CStandaloneClients;

class CWorldTextClient
{
public:
	CWorldTextClient(const CManagedAreasClient& areas, CStandaloneClients& standalone)
		: m_Areas(areas)
		, m_Standalone(standalone)
	{
	}

	// Any text still live forgets its gone callback first: the callback is this object's
	// thunk, whose page goes with it.
	~CWorldTextClient();

	CWorldTextClient(const CWorldTextClient&) = delete;
	CWorldTextClient& operator=(const CWorldTextClient&) = delete;

	CClientSystem& SystemRow() { return m_SystemRow; }

	// IShimParticlesClient.
	int32_t WorldTextMeshUpdate(uint32_t hOwner, const char* pszMaterial, const float* pBounds, int32_t nFloats);
	void WorldTextMeshDestroy(uint32_t hOwner);

private:
	class CSystemRow final : public CClientSystemPerFrame
	{
	public:
		explicit CSystemRow(CWorldTextClient& text)
			: CClientSystemPerFrame("CSourceSharpWorldTextSystem")
			, m_Text(text)
		{
		}

		void PreRender() override;
		void LevelShutdownPostEntity() override;

	private:
		CWorldTextClient& m_Text;
	};

	// hOwner and pEffect are the members shell_effect_registry.h requires of a row.
	struct Row_t
	{
		uint32_t hOwner;
		CManagedWorldText* pEffect;
	};

	void Forget(CShellRenderable* pText);
	void Follow();

	const CManagedAreasClient& m_Areas;
	CStandaloneClients& m_Standalone;

	CUtlVector<Row_t> m_Rows;

	// A text released by any route is forgotten through its gone callback, which takes no
	// context: a thunk bound to this object (bound_callback.h).
	typedef CBoundCallback<void(CShellRenderable*)> ForgetHook_t;
	ForgetHook_t m_ForgetHook{this, &ForgetHook_t::Trampoline<CWorldTextClient, &CWorldTextClient::Forget>};

	CSystemRow m_SystemRow{*this};
};
