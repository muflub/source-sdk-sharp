// SS_REASON b  an interface resolved by name at load
// The tier libraries are not linked; this does their engine-factory lookups into
// globals of the same names and types, so every reader links as before.

// Valve's headers declare `cvar` DLL_EXPORT, and defining it here would export
// it; nothing imports it from a game DLL, so DLL_EXPORT is a plain declaration.
#include "tier0/platform.h"
#undef DLL_EXPORT
#define DLL_EXPORT extern "C"

#include "tier_connect.h"

#include "datacache/imdlcache.h"
#include "icvar.h"
#ifdef CLIENT_DLL
#include "particles_lib_client.h"
#include "vgui/IVGui.h"
#include "vgui/IInput.h"
#include "vgui/IPanel.h"
#include "vgui/ISurface.h"
#include "vgui/IScheme.h"
#include "vgui/ILocalize.h"
#include "VGuiMatSurface/IMatSystemSurface.h"
#include "tier3/tier3.h"
#include "tier2/tier2.h"
#include "filesystem.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"

// public/vgui/ISystem.h's version string; that header is not compiled here.
#define SS_VGUI_SYSTEM_INTERFACE_VERSION "VGUI_System010"
#endif

// tier1: the client's console globals, which its Valve code reads (the server's console
// is its root's CCVarRegistry). A second connect leaves the first answers alone.
#ifdef CLIENT_DLL
ICvar* cvar = NULL;
ICvar* g_pCVar = NULL;

static bool s_bTier1Connected = false;

void SourceSharp_ConnectTier1(CreateInterfaceFn engineFactory)
{
	if (s_bTier1Connected || !engineFactory)
		return;

	s_bTier1Connected = true;

	if (!g_pCVar)
		cvar = g_pCVar = (ICvar*)engineFactory(CVAR_INTERFACE_VERSION, NULL);
}

void SourceSharp_DisconnectTier1()
{
	if (!s_bTier1Connected)
		return;

	cvar = g_pCVar = NULL;
	s_bTier1Connected = false;
}

// tier1.h's names for the client's connect, so tier1.o stays off the link.
void ConnectTier1Libraries(CreateInterfaceFn* pFactoryList, int nFactoryCount)
{
	for (int i = 0; i < nFactoryCount; i++)
		SourceSharp_ConnectTier1(pFactoryList[i]);
}

void DisconnectTier1Libraries()
{
	SourceSharp_DisconnectTier1();
}
#endif

// tier2: the globals client.so's objects import from tier2.o.
#ifdef CLIENT_DLL
IFileSystem* g_pFullFileSystem = NULL;
IMaterialSystem* materials = NULL;
IMaterialSystem* g_pMaterialSystem = NULL;
IMaterialSystemHardwareConfig* g_pMaterialSystemHardwareConfig = NULL;

void SourceSharp_ConnectTier2(CreateInterfaceFn engineFactory)
{
	if (!engineFactory)
		return;

	g_pFullFileSystem = (IFileSystem*)engineFactory(FILESYSTEM_INTERFACE_VERSION, NULL);
	g_pMaterialSystem = materials = (IMaterialSystem*)engineFactory(MATERIAL_SYSTEM_INTERFACE_VERSION, NULL);
	g_pMaterialSystemHardwareConfig =
		(IMaterialSystemHardwareConfig*)engineFactory(MATERIALSYSTEM_HARDWARECONFIG_INTERFACE_VERSION, NULL);
	// libss_particles.so's own tier1/tier2 globals and ConVars, from the same factory.
	if (!ParticleLib_Connect(engineFactory))
		Error("[SourceSharp] libss_particles.so did not connect to the engine's interfaces\n");
}

void SourceSharp_DisconnectTier2()
{
	ParticleLib_Disconnect();
	g_pFullFileSystem = NULL;
	materials = g_pMaterialSystem = NULL;
	g_pMaterialSystemHardwareConfig = NULL;
}

// Named in a CTier2AppSystem vtable, which would otherwise pull tier2.o in.
void ConnectTier2Libraries(CreateInterfaceFn* pFactoryList, int nFactoryCount)
{
	for (int i = 0; i < nFactoryCount; i++)
		SourceSharp_ConnectTier2(pFactoryList[i]);
}

void DisconnectTier2Libraries()
{
	SourceSharp_DisconnectTier2();
}
#endif

#ifdef CLIENT_DLL
// tier3: the model cache; the server's is its DLL tier's (CServerEngineInterfaces).
IMDLCache* mdlcache = NULL;

// tier3: the globals client.so's objects import from tier3.o.
IMatSystemSurface* g_pMatSystemSurface = NULL;
vgui::ISurface* g_pVGuiSurface = NULL;
vgui::IInput* g_pVGuiInput = NULL;
vgui::IVGui* g_pVGui = NULL;
vgui::IPanel* g_pVGuiPanel = NULL;
vgui::ILocalize* g_pVGuiLocalize = NULL;
vgui::ISchemeManager* g_pVGuiSchemeManager = NULL;
vgui::ISystem* g_pVGuiSystem = NULL;

void SourceSharp_ConnectTier3(CreateInterfaceFn engineFactory)
{
	if (!engineFactory)
		return;

	mdlcache = (IMDLCache*)engineFactory(MDLCACHE_INTERFACE_VERSION, NULL);
	g_pVGui = (vgui::IVGui*)engineFactory(VGUI_IVGUI_INTERFACE_VERSION, NULL);
	g_pVGuiInput = (vgui::IInput*)engineFactory(VGUI_INPUT_INTERFACE_VERSION, NULL);
	g_pVGuiPanel = (vgui::IPanel*)engineFactory(VGUI_PANEL_INTERFACE_VERSION, NULL);
	g_pVGuiSurface = (vgui::ISurface*)engineFactory(VGUI_SURFACE_INTERFACE_VERSION, NULL);
	g_pVGuiSchemeManager = (vgui::ISchemeManager*)engineFactory(VGUI_SCHEME_INTERFACE_VERSION, NULL);
	g_pVGuiSystem = (vgui::ISystem*)engineFactory(SS_VGUI_SYSTEM_INTERFACE_VERSION, NULL);
	g_pVGuiLocalize = (vgui::ILocalize*)engineFactory(VGUI_LOCALIZE_INTERFACE_VERSION, NULL);
	g_pMatSystemSurface = (IMatSystemSurface*)engineFactory(MAT_SYSTEM_SURFACE_INTERFACE_VERSION, NULL);
}

void SourceSharp_DisconnectTier3()
{
	mdlcache = NULL;
	g_pVGui = NULL;
	g_pVGuiInput = NULL;
	g_pVGuiPanel = NULL;
	g_pVGuiSurface = NULL;
	g_pVGuiSchemeManager = NULL;
	g_pVGuiSystem = NULL;
	g_pVGuiLocalize = NULL;
	g_pMatSystemSurface = NULL;
}
#endif
