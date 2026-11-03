//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The material proxy trampoline's state as one object, a member of the client DLL object:
// a CreateInterface registration per proxy name, the live proxies by the handle managed
// knows them by, and the not-shipped names managed refused. IShimMaterialProxyClient is
// this class.

#pragma once

#include <stdint.h>

#include "interface.h"
#include "tier1/utlmap.h"
#include "tier1/utlvector.h"

#include "bound_callback.h"
#include "ishim_areas_client.h"
#include "shell_managed_areas_client.h"

class CManagedMaterialProxy;

class CMaterialProxiesClient final : public IShimMaterialProxyClient
{
public:
	explicit CMaterialProxiesClient(const CManagedAreasClient& areas);

	CMaterialProxiesClient(const CMaterialProxiesClient&) = delete;
	CMaterialProxiesClient& operator=(const CMaterialProxiesClient&) = delete;

	// NULL outside a managed load.
	IManagedMaterialProxyClient* Managed() const { return m_Areas.m_pMaterialProxyClient; }

	// Handles are never reused, so a stale one finds nothing.
	int32_t AddProxy(CManagedMaterialProxy* pProxy);

	void RemoveProxy(int32_t hProxy) { m_Proxies.Remove(hProxy); }

	CManagedMaterialProxy* FindProxy(int32_t hProxy) const;

	// IShimMaterialProxyClient.
	int32_t ProxyFindVar(int32_t hProxy, const char* pszName, int32_t bComplain, int32_t* pFound) override;
	int32_t ProxyFindTexture(int32_t hProxy, const char* pszName, const char* pszGroup, int32_t bComplain,
							 int32_t* pIsError) override;
	int32_t ProxyVarTexture(int32_t hProxy, int32_t nSlot, char* pOutName, int32_t nOutSize) override;
	int32_t ProxyBindEntity(int32_t hProxy, SourceSharpProxyEntity_t* pOut) override;
	int32_t ProxyReadView(int32_t nKind, float* pOut, int32_t nFloats) override;
	int32_t ProxyPlayerCustomFile(int32_t nPlayer, uint32_t* pCrc) override;
	void ProxyNotShipped(const char* pszCapability, const char* pszReason) override;

private:
	// One name's registration. CreateInterface's create functions take no context, so each
	// is a thunk bound to its row (bound_callback.h), which knows its proxy's name.
	class CProxyReg
	{
	public:
		typedef CBoundCallback<void*()> Fn_t;

		CProxyReg(CMaterialProxiesClient& proxies, int nName, const char* pszInterface)
			: m_Proxies(proxies)
			, m_nName(nName)
			, m_Create(this, &Fn_t::Trampoline<CProxyReg, &CProxyReg::Create>)
			, m_Reg(m_Create.Get(), pszInterface)
		{
		}

		CProxyReg(const CProxyReg&) = delete;
		CProxyReg& operator=(const CProxyReg&) = delete;

	private:
		void* Create();

		CMaterialProxiesClient& m_Proxies;
		const int m_nName;
		Fn_t m_Create;
		InterfaceReg m_Reg;
	};

	const CManagedAreasClient& m_Areas;

	CUtlMap<int32_t, CManagedMaterialProxy*> m_Proxies;
	int32_t m_nNextProxy = 0;

	// The not-shipped ledger keeps the name and reason pointers it is given, so a managed
	// refusal's text is interned here once per name for the process.
	CUtlVector<char*> m_RefusalNames;
	CUtlVector<char*> m_RefusalReasons;

	static const int kProxyNames = 81;
	CProxyReg m_Regs[kProxyNames];
};
