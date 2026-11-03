//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

#pragma once

#include "shell_clientsystem.h"
#include "shell_managed_clientsystems_client.h"
#include "shell_soundemitter_client.h"
#include "shell_soundscape_audio_client.h"

class IDetailObjectSystem;
class CStandaloneClients;
class CEffectsListClient;
class CManagedParticlesClient;
class CParticleSystemsClient;
class CShellDetailProps;

// The client's game systems as one named table, in the order they run, a member of the
// client DLL object. Stage passes walk the rows forwards; the shutdown stages walk them
// backwards. The rows are filled by Init, as the systems are first needed.
class CClientSystems
{
public:
	// The systems the client DLL object owns, and the managed areas the managed rows call;
	// the other rows are their files' objects.
	CClientSystems(const CManagedAreasClient& areas, CClientSystem& managedFrame, CClientSystem& physics,
				   CClientSystem& decals, CStandaloneClients& standalone, CEffectsListClient& effects,
				   CManagedParticlesClient& particles, CParticleSystemsClient& particleSystems,
				   CShellDetailProps& detailProps);
	~CClientSystems();

	CClientSystems(const CClientSystems&) = delete;
	CClientSystems& operator=(const CClientSystems&) = delete;

	// Init in the table's order, stopping on the first refusal, as stock did.
	bool Init();
	void PostInit();
	void Shutdown();

	void LevelInitPreEntity();
	void LevelInitPostEntity();
	void LevelShutdownPreEntity();
	void LevelShutdownPostEntity();

	void PreRender();
	void Update(float frametime);

private:
	typedef void (CClientSystem::*Stage_t)();
	typedef void (CClientSystemPerFrame::*FrameStage_t)();

	struct Row_t
	{
		const char* m_pszName;
		CClientSystem* m_pSystem;
	};

	void WalkForward(Stage_t fn);
	void WalkBackward(Stage_t fn);
	void WalkForwardPerFrame(FrameStage_t fn);

	CClientSystem& m_ManagedFrame;
	CClientSystem& m_Physics;
	CClientSystem& m_Decals;
	CStandaloneClients& m_Standalone;
	CEffectsListClient& m_Effects;
	CManagedParticlesClient& m_Particles;
	CParticleSystemsClient& m_ParticleSystems;
	CShellDetailProps& m_DetailProps;

	// Stock's accessors answer a more specific type; these rows keep the table's column uniform.
	CGameSystemRow<IDetailObjectSystem> m_DetailObjectRow{nullptr};

	// The rows whose system is managed.
	CManagedClientSystemRow m_ViewportRow;
	CManagedClientSystemRow m_GlowOverlayRow;
	CManagedClientSystemRow m_PixelVisibilityRow;

	// The rows that are only a forward to managed.
	CSoundEmitterSystemRow m_SoundEmitterRow;
	C_SoundscapeSystem m_SoundscapeRow;

	static const int kRowCount = 22;
	Row_t m_Rows[kRowCount] = {};
	int m_nRows = 0;
};
