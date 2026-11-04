//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The managed particle executors as one object, a member of the client DLL object: the
// live executors (one per owner and channel, each a CManagedParticleEffect drawn by the
// leaf system), their frame read and commit, the singleton draws, and the client systems
// row that keeps each at its owner and releases them at a level's end. Its IShimParticlesClient
// slots are the executors' own; the area's other slots are free answers.

#pragma once

#include <stdint.h>

#include "utlvector.h"

#include "bound_callback.h"
#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"
#include "shell_member_command.h"

class CClientEngineInterfaces;
class CViewCameras;
class CManagedParticleEffect;
class CShellParticleEffect;
class CStandaloneClients;

class CManagedParticlesClient
{
public:
	CManagedParticlesClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
							const CViewCameras& cameras, CStandaloneClients& standalone)
		: m_Areas(areas)
		, m_Engines(engines)
		, m_Cameras(cameras)
		, m_Standalone(standalone)
	{
	}

	// Any executor still live outside a level forgets its gone callback first: the callback
	// is this object's thunk, whose page goes with it.
	~CManagedParticlesClient();

	CManagedParticlesClient(const CManagedParticlesClient&) = delete;
	CManagedParticlesClient& operator=(const CManagedParticlesClient&) = delete;

	// Shim slots: what a managed effect asks of the engine.
	int32_t ParticlesTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, float* pOut, int32_t nOut);
	int32_t ParticleSystemNameFromIndex(int32_t nIndex, char* pszOut, int32_t nOutMax);
	int32_t ParticlesWorldTrace(const float* pStartXYZ, const float* pEndXYZ, float* pOut, int32_t nOut);
	int32_t ParticlesTraceMask(const float* pStartXYZ, const float* pEndXYZ, uint32_t nMask, float* pOut, int32_t nOut);

	// Shim slot: the model-view rows, the view id, and the view's origin and forward.
	void ParticlesView(float* pModelView12, int32_t* pViewId, float* pOriginForward6);

	// NULL outside a managed load.
	IManagedParticlesClient* Managed() const { return m_Areas.m_pParticlesClient; }

	// Each live executor's frame read goes to managed in one buffer and each binding's state
	// comes back in another (the client DLL object's frame); the old-style effects' frame
	// code (the render plan) and post-render (the client mode) are managed's.
	void Frame(float flFrameTime);
	void FrameCode();
	void PostRender();

	// At its place in the translucent passes: the executors managed put in the singleton
	// space (stock's DrawParticleSingletons).
	void DrawSingletons(bool bInSkybox);

	// cs_cl_effect_kinds: every live executor, where it draws and how many it simulates.
	void PrintKinds() const;

	CClientSystem& SystemRow() { return m_SystemRow; }

	// IShimParticlesClient.
	int32_t ParticlesCreate(uint32_t hOwner, int32_t nChannel, const char* pszMaterial, float flCullRadius,
							int32_t bAlwaysSimulate);
	int32_t ParticlesCreateAt(int32_t nId, const char* pszMaterial, const float* pOriginXYZ, float flCullRadius,
							  int32_t bAlwaysSimulate);
	int32_t ParticlesSetSpace(uint32_t hOwner, int32_t nChannel, uint32_t hEntity, int32_t nAttachment, int32_t nFlags,
							  SourceSharpParticleFollow_t* pFollow);
	void ParticlesConfigure(uint32_t hOwner, int32_t nChannel, float flCullRadius);
	void ParticlesSetLights(uint32_t hOwner, int32_t nChannel, const float* pLights, int32_t nFloats);
	int32_t ParticlesMaterialVarInt(uint32_t hOwner, int32_t nChannel, const char* pszVar, int32_t* pValue);
	void ParticlesDestroy(uint32_t hOwner, int32_t nChannel);

private:
	void EffectKindsCommand(const CCommand& args) { PrintKinds(); }

	CMemberCommand<CManagedParticlesClient, &CManagedParticlesClient::EffectKindsCommand> m_EffectKindsCommand{
		*this, "cs_cl_effect_kinds",
		"every live managed particle executor (trails, steam jets, smoke stacks, smoke volumes): "
		"where it is drawn this frame and how many particles its binding is simulating",
		0};

	// Keeps each executor at its owner every frame, and releases them all at a level's end.
	class CSystemRow final : public CClientSystemPerFrame
	{
	public:
		explicit CSystemRow(CManagedParticlesClient& particles)
			: CClientSystemPerFrame("CSourceSharpManagedParticlesSystem")
			, m_Particles(particles)
		{
		}

		void PreRender() override;
		void LevelShutdownPostEntity() override;

	private:
		CManagedParticlesClient& m_Particles;
	};

	int Find(uint32_t hOwner, int32_t nChannel) const;
	void Forget(CShellParticleEffect* pEffect);
	void Follow();
	void ReleaseAll();

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	const CViewCameras& m_Cameras;
	CStandaloneClients& m_Standalone;

	CUtlVector<CManagedParticleEffect*> m_Effects;

	// An executor released by any route is forgotten through its gone callback, which takes
	// no context: a thunk bound to this object (bound_callback.h).
	typedef CBoundCallback<void(CShellParticleEffect*)> ForgetHook_t;
	ForgetHook_t m_ForgetHook{this,
							  &ForgetHook_t::Trampoline<CManagedParticlesClient, &CManagedParticlesClient::Forget>};

	CSystemRow m_SystemRow{*this};
};
