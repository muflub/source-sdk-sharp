// Second-camera views drawn into render targets, from the client mode's PreRender, and the
// engine's three named render targets. A member of the client DLL object.
#pragma once

#include <stdint.h>

#include "managed_convar_ref.h"
#include "tier1/interface.h"

#include "sourcesharp.h"

class CClientEngineInterfaces;
class CSourceSharpClientMode;
class IMaterial;
class IMaterialSystem;
class IMaterialSystemHardwareConfig;
class ITexture;

// The engine's interface, restated as it calls it: two slots, no base, no
// destructor. The engine passes no sizes.
abstract_class IClientRenderTargets
{
public:
	virtual void InitClientRenderTargets(IMaterialSystem * pMaterialSystem,
										 IMaterialSystemHardwareConfig * pHardwareConfig) = 0;
	virtual void ShutdownClientRenderTargets() = 0;
};

#define CLIENTRENDERTARGETS_INTERFACE_VERSION "ClientRenderTargets001"

class CViewTargetsClient final : public IClientRenderTargets
{
public:
	explicit CViewTargetsClient(const CClientEngineInterfaces& engines)
		: m_Engines(engines)
	{
	}

	CViewTargetsClient(const CViewTargetsClient&) = delete;
	CViewTargetsClient& operator=(const CViewTargetsClient&) = delete;

	void InitClientRenderTargets(IMaterialSystem* pMaterialSystem,
								 IMaterialSystemHardwareConfig* pHardwareConfig) override;
	void ShutdownClientRenderTargets() override;

	// IShimViewClient.
	int32_t RenderViewToTarget(int32_t nTarget, const SourceSharpViewTargetSetup* pSetup);
	int32_t ViewTargetInfo(int32_t nTarget, int32_t* pOut4);

	// Draws every view queued by RenderViewToTarget this frame.
	void RenderQueued(CSourceSharpClientMode& clientMode);

	// A render target's texture, and a note that one more view was drawn into it.
	ITexture* Texture(int nTarget);
	void NoteDrawn(int nTarget);

private:
	enum
	{
		kViewTargets = 1
	};

	struct ViewTarget
	{
		ITexture* pTexture;
		IMaterial* pMaterial; // UnlitGeneric over the texture, for vgui
		int nVguiTexture; // 0 until the UI first asks
		bool bPending;
		SourceSharpViewTargetSetup Setup;
		int nRenders;
	};

	const CClientEngineInterfaces& m_Engines;
	ViewTarget m_Targets[kViewTargets] = {};
	ITexture* m_pWaterReflection = nullptr;
	ITexture* m_pWaterRefraction = nullptr;
	ITexture* m_pCamera = nullptr;
	bool m_bRendering = false;
	CManagedConVarRef m_PixelVisibility{"r_dopixelvisibility"};
};
