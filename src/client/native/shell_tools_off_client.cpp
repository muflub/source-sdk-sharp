//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the tool framework, off =============================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// SFM IS DEAD (user ruling 2026-10-19): Valve's tool framework and the Source
// Filmmaker recording hooks are deleted, not ported. toolframework_client.cpp
// and entity_client_tools.cpp left client.so with that ruling.
//
// These are the names the Valve TUs still compiled into client.so link
// against, and each answers "tools are off":
//
//   clientshadowmgr.cpp    ToolsEnabled, ToolFramework_RecordMaterialParams
//   flashlighteffect.cpp   clienttools (IsInRecordingMode),
//                          ToolFramework_PostToolMessage
//
// ToolsEnabled() is false and `clienttools` is never in recording mode, so
// both callers skip their tool arms and the two message functions are never
// reached. Nothing here decides anything.
//
// VCLIENTTOOLS001 IS NOT EXPORTED. Only a tool asks for it
// (tools/toolutils/BaseToolSystem.cpp in the 2018 engine source), and no
// binary of the install names it (`strings -a` over Source SDK Base 2013
// Multiplayer's and the dedicated server's bin/linux64/*.so, 2026-10-19).
//
// Exit: clientshadowmgr.cpp's and flashlighteffect.cpp's evictions.
//=============================================================================//

class IMaterial;
class KeyValues;

#include "toolframework_client.h"

bool ToolsEnabled()
{
	return false;
}

void ToolFramework_RecordMaterialParams( IMaterial * )
{
}

void ToolFramework_PostToolMessage( HTOOLHANDLE, KeyValues * )
{
}

namespace
{

//-----------------------------------------------------------------------------
// IClientTools with no tool behind it. Its one reachable method is
// IsInRecordingMode, and recording never starts: EnableRecordingMode does not
// change the answer. The rest answer the type's empty value.
//-----------------------------------------------------------------------------
class CClientToolsOff final : public IClientTools
{
public:
	HTOOLHANDLE AttachToEntity( EntitySearchResult ) override { return HTOOLHANDLE_INVALID; }
	void DetachFromEntity( EntitySearchResult ) override {}
	bool IsValidHandle( HTOOLHANDLE ) override { return false; }
	int GetNumRecordables() override { return 0; }
	HTOOLHANDLE GetRecordable( int ) override { return HTOOLHANDLE_INVALID; }
	EntitySearchResult NextEntity( EntitySearchResult ) override { return nullptr; }
	void SetEnabled( HTOOLHANDLE, bool ) override {}
	void SetRecording( HTOOLHANDLE, bool ) override {}
	bool ShouldRecord( HTOOLHANDLE ) override { return false; }
	HTOOLHANDLE GetToolHandleForEntityByIndex( int ) override { return HTOOLHANDLE_INVALID; }
	int GetModelIndex( HTOOLHANDLE ) override { return 0; }
	const char *GetModelName( HTOOLHANDLE ) override { return nullptr; }
	const char *GetClassname( HTOOLHANDLE ) override { return nullptr; }
	void AddClientRenderable( IClientRenderable *, int ) override {}
	void RemoveClientRenderable( IClientRenderable *pRenderable ) override {}
	void SetRenderGroup( IClientRenderable *, int ) override {}
	void MarkClientRenderableDirty( IClientRenderable *pRenderable ) override {}
	void UpdateProjectedTexture( ClientShadowHandle_t, bool ) override {}
	bool DrawSprite( IClientRenderable *, float, float, int, int, const Color &, float, int * ) override { return false; }
	EntitySearchResult GetLocalPlayer() override { return nullptr; }
	bool GetLocalPlayerEyePosition( Vector &org, QAngle &ang, float &fov ) override
	{
		org.Init();
		ang.Init();
		fov = 0.0f;
		return false;
	}
	ClientShadowHandle_t CreateShadow( CBaseHandle, int ) override { return CLIENTSHADOW_INVALID_HANDLE; }
	void DestroyShadow( ClientShadowHandle_t ) override {}
	ClientShadowHandle_t CreateFlashlight( const FlashlightState_t & ) override { return CLIENTSHADOW_INVALID_HANDLE; }
	void DestroyFlashlight( ClientShadowHandle_t ) override {}
	void UpdateFlashlightState( ClientShadowHandle_t, const FlashlightState_t & ) override {}
	void AddToDirtyShadowList( ClientShadowHandle_t, bool ) override {}
	void MarkRenderToTextureShadowDirty( ClientShadowHandle_t ) override {}
	void EnableRecordingMode( bool ) override {}
	bool IsInRecordingMode() const override { return false; }
	void TriggerTempEntity( KeyValues * ) override {}
	int GetOwningWeaponEntIndex( int ) override { return -1; }
	int GetEntIndex( EntitySearchResult ) override { return -1; }
	int FindGlobalFlexcontroller( char const * ) override { return -1; }
	char const *GetGlobalFlexControllerName( int ) override { return ""; }
	EntitySearchResult GetOwnerEntity( EntitySearchResult ) override { return nullptr; }
	bool IsPlayer( EntitySearchResult ) override { return false; }
	bool IsBaseCombatCharacter( EntitySearchResult ) override { return false; }
	bool IsNPC( EntitySearchResult ) override { return false; }
	Vector GetAbsOrigin( HTOOLHANDLE ) override { return Vector( 0.0f, 0.0f, 0.0f ); }
	QAngle GetAbsAngles( HTOOLHANDLE ) override { return QAngle( 0.0f, 0.0f, 0.0f ); }
	void ReloadParticleDefintions( const char *, const void *, int ) override {}
	void PostToolMessage( KeyValues * ) override {}
	void EnableParticleSystems( bool ) override {}
	bool IsRenderingThirdPerson() const override { return false; }
};

CClientToolsOff s_ClientToolsOff;

} // namespace

IClientTools *clienttools = &s_ClientToolsOff;
