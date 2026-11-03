//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The view pass's questions about the local viewer, answered from the engine block or the
// managed player's published pose, and the fog and 3D skybox managed pushes: a member of
// the client DLL object, handed the standalone clients and the entity list it reads.
// Nothing here decides. view_facts.h's free functions answer through it for the stock
// TUs not yet handed it.

#pragma once

struct fogparams_t;
struct sky3dparams_t;
class CStandaloneClients;
class CClientEntityList;
class Vector;
class QAngle;
struct SourceSharpFogParams;
struct SourceSharpSkyboxParams;

class CClientEngineInterfaces;

class CViewerFacts final
{
public:
	CViewerFacts(const CClientEngineInterfaces& engines, CStandaloneClients& standalone, CClientEntityList& entityList);
	~CViewerFacts();

	CViewerFacts(const CViewerFacts&) = delete;
	CViewerFacts& operator=(const CViewerFacts&) = delete;

	// view_facts.h says what each answers.
	bool ViewerAbsPosition(Vector* pOrigin, QAngle* pAngles) const;
	bool ViewEntityPose(Vector* pOrigin, QAngle* pAngles) const;
	bool ViewerEffectActive(int nEffectBits) const;
	bool ViewerAudioIsUnderwater(const Vector& vecMainViewOrigin) const;
	void ViewerCameraModeChanged();

	// IShimViewClient's fog and skybox slots; the getters answer NULL until something is pushed.
	void SetViewerFogParams(const SourceSharpFogParams* pParams);
	fogparams_t* ViewerFogParams();
	void SetViewerSkyboxParams(const SourceSharpSkyboxParams* pParams);
	sky3dparams_t* ViewerSkyboxParams();

private:
	const CClientEngineInterfaces& m_Engines;
	CStandaloneClients& m_Standalone;
	CClientEntityList& m_EntityList;

	// What managed pushed, made on the first push and defined beside the bodies: the engine
	// structs need the networked variable headers.
	struct Pushed_t;
	static Pushed_t* NewPushed();
	Pushed_t* m_pPushed;
};
