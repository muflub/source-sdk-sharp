// What the probe recorders reach from their console commands (SS_CLIENT_PROBES only):
// the client DLL object's members, defined in shell_clientdll.cpp under that guard.
#pragma once

class CClientEngineInterfaces;
class CClientEntityList;
class CEffectsListClient;
class CNewParticlesClient;
class CSoundPatchesClient;
class CTempEntsSystemClient;

const CClientEngineInterfaces& ShellClientDll_Engines();
CClientEntityList& ShellClientDll_EntityList();
CEffectsListClient& ShellClientDll_EffectsList();
CNewParticlesClient& ShellClientDll_NewParticles();
CSoundPatchesClient& ShellClientDll_SoundPatches();
CTempEntsSystemClient& ShellClientDll_TempEntsSystem();
