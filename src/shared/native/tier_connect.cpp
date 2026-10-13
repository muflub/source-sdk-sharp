//========= SourceSharp — the engine interfaces the tier libraries connected ==//
// SS_REASON b  an interface resolved by name at load
//
// plan_server_novalve.md P3. server.so does not link tier1, tier2 or tier3,
// and their Connect*Libraries functions did one thing each for this DLL: look
// an interface up in the engine's factory and keep it in a global the rest of
// the code reads. This file does those lookups itself, into globals of the
// same names and types, so every reader links as before.
//
// Added a tier at a time: each lookup lands in the commit that takes its
// library off the link group.
//
//=============================================================================//

// `cvar` is ours (defined below), but Valve's headers declare it DLL_EXPORT,
// and defining it under that declaration exports it: dllexport on Windows,
// default visibility past client.so's version script on Linux. Nothing imports
// it from a game DLL (the engine has its own, the particles library its copy),
// so this file reads those headers with DLL_EXPORT a plain C declaration and
// the game DLLs export CreateInterface alone (game_dll_exports_test).
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
#include "tier3/tier3.h"	// the globals' declarations, vgui::ISystem's among them
#include "tier2/tier2.h"
#include "filesystem.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"

// public/vgui/ISystem.h's version string. The header is not one client.so
// otherwise compiles, and the type is only forward-declared here (tier3.h).
#define SS_VGUI_SYSTEM_INTERFACE_VERSION "VGUI_System010"
#endif

//-----------------------------------------------------------------------------
// tier1: the console (both names tier1 gave it) and utlsortvector.h's qsort
// context, which tier1.cpp also defined. Declared by public/tier1/tier1.h and
// utlsortvector.h. The context must be defined here: a sort vector's template
// names it, and without this definition the linker takes tier1.cpp's,
// bringing its duplicates of the two above. Connected once; a second connect
// leaves the first answers alone, as tier1's did. (g_pProcessUtils went in
// C9's tier1 step: nm -u found no importer on either DLL's link, and both
// link with GC_SECTIONS=0 without it.)
//-----------------------------------------------------------------------------
ICvar *cvar = NULL;
ICvar *g_pCVar = NULL;
void *g_pUtlSortVectorQSortContext = NULL;

static bool s_bTier1Connected = false;

void SourceSharp_ConnectTier1( CreateInterfaceFn engineFactory )
{
	if ( s_bTier1Connected || !engineFactory )
		return;

	s_bTier1Connected = true;

	if ( !g_pCVar )
		cvar = g_pCVar = (ICvar *)engineFactory( CVAR_INTERFACE_VERSION, NULL );
}

void SourceSharp_DisconnectTier1()
{
	if ( !s_bTier1Connected )
		return;

	cvar = g_pCVar = NULL;
	s_bTier1Connected = false;
}

#ifdef CLIENT_DLL
// tier1.h's names for the client's connect (plan_client_novalve C9's tier1
// step): ours, so tier1.o stays off the link.
void ConnectTier1Libraries( CreateInterfaceFn *pFactoryList, int nFactoryCount )
{
	for ( int i = 0; i < nFactoryCount; i++ )
		SourceSharp_ConnectTier1( pFactoryList[ i ] );
}

void DisconnectTier1Libraries()
{
	SourceSharp_DisconnectTier1();
}
#endif

//-----------------------------------------------------------------------------
// tier2's one lookup, g_pFullFileSystem, went with its only reader, the model
// sounds cache (plan_server_novalve.md P3): nothing on the server reads it.
//
// THE CLIENT'S tier2 LOOKUPS (plan_client_novalve C9): the four globals
// client.so's objects import from tier2.o (nm over the link edge), by the
// names ConnectTier2Libraries asked the factory for. The other six tier2
// lookups (input, network, debug texture info, VB alloc tracker, p4, mdllib,
// queued loader, colour correction) have no client importer.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
IFileSystem *g_pFullFileSystem = NULL;
IMaterialSystem *materials = NULL;
IMaterialSystem *g_pMaterialSystem = NULL;
IMaterialSystemHardwareConfig *g_pMaterialSystemHardwareConfig = NULL;

void SourceSharp_ConnectTier2( CreateInterfaceFn engineFactory )
{
	if ( !engineFactory )
		return;

	g_pFullFileSystem = (IFileSystem *)engineFactory( FILESYSTEM_INTERFACE_VERSION, NULL );
	g_pMaterialSystem = materials = (IMaterialSystem *)engineFactory( MATERIAL_SYSTEM_INTERFACE_VERSION, NULL );
	g_pMaterialSystemHardwareConfig = (IMaterialSystemHardwareConfig *)engineFactory(
		MATERIALSYSTEM_HARDWARECONFIG_INTERFACE_VERSION, NULL );
	// plan_particles_so.md: libss_particles.so's own tier1/tier2 copies (the
	// same lookups, into its globals) and its ConVars, from the same factory.
	if ( !ParticleLib_Connect( engineFactory ) )
		Error( "[SourceSharp] libss_particles.so did not connect to the engine's interfaces\n" );
}

void SourceSharp_DisconnectTier2()
{
	ParticleLib_Disconnect();
	g_pFullFileSystem = NULL;
	materials = g_pMaterialSystem = NULL;
	g_pMaterialSystemHardwareConfig = NULL;
}

// tier2.h's two names, for the objects that still name them: the VR
// stand-in's CTier2AppSystem base (under its CTier3AppSystem) names both in
// its vtable, which alone would pull tier2.o off the archive.
void ConnectTier2Libraries( CreateInterfaceFn *pFactoryList, int nFactoryCount )
{
	for ( int i = 0; i < nFactoryCount; i++ )
		SourceSharp_ConnectTier2( pFactoryList[ i ] );
}

void DisconnectTier2Libraries()
{
	SourceSharp_DisconnectTier2();
}
#endif

//-----------------------------------------------------------------------------
// tier3: the one interface the server read from ConnectTier3Libraries.
// Declared by public/tier3/tier3.h, which this file does not need to include.
//-----------------------------------------------------------------------------
IMDLCache *mdlcache = NULL;

#ifdef CLIENT_DLL
//-----------------------------------------------------------------------------
// THE CLIENT'S tier3 LOOKUPS (plan_client_novalve C9): the eight more globals
// client.so's objects import from tier3.o (nm over the link edge), each by the
// interface name tier3's ConnectTier3Libraries asked the factory for. The
// other nine tier3 defines (studio render, data cache, video ...) have no
// client importer and are not defined.
//-----------------------------------------------------------------------------
IMatSystemSurface *g_pMatSystemSurface = NULL;
vgui::ISurface *g_pVGuiSurface = NULL;
vgui::IInput *g_pVGuiInput = NULL;
vgui::IVGui *g_pVGui = NULL;
vgui::IPanel *g_pVGuiPanel = NULL;
vgui::ILocalize *g_pVGuiLocalize = NULL;
vgui::ISchemeManager *g_pVGuiSchemeManager = NULL;
vgui::ISystem *g_pVGuiSystem = NULL;
#endif

void SourceSharp_ConnectTier3( CreateInterfaceFn engineFactory )
{
	if ( !engineFactory )
		return;

	mdlcache = (IMDLCache *)engineFactory( MDLCACHE_INTERFACE_VERSION, NULL );

#ifdef CLIENT_DLL
	g_pVGui = (vgui::IVGui *)engineFactory( VGUI_IVGUI_INTERFACE_VERSION, NULL );
	g_pVGuiInput = (vgui::IInput *)engineFactory( VGUI_INPUT_INTERFACE_VERSION, NULL );
	g_pVGuiPanel = (vgui::IPanel *)engineFactory( VGUI_PANEL_INTERFACE_VERSION, NULL );
	g_pVGuiSurface = (vgui::ISurface *)engineFactory( VGUI_SURFACE_INTERFACE_VERSION, NULL );
	g_pVGuiSchemeManager = (vgui::ISchemeManager *)engineFactory( VGUI_SCHEME_INTERFACE_VERSION, NULL );
	g_pVGuiSystem = (vgui::ISystem *)engineFactory( SS_VGUI_SYSTEM_INTERFACE_VERSION, NULL );
	g_pVGuiLocalize = (vgui::ILocalize *)engineFactory( VGUI_LOCALIZE_INTERFACE_VERSION, NULL );
	g_pMatSystemSurface = (IMatSystemSurface *)engineFactory( MAT_SYSTEM_SURFACE_INTERFACE_VERSION, NULL );
#endif
}

void SourceSharp_DisconnectTier3()
{
	mdlcache = NULL;
#ifdef CLIENT_DLL
	g_pVGui = NULL;
	g_pVGuiInput = NULL;
	g_pVGuiPanel = NULL;
	g_pVGuiSurface = NULL;
	g_pVGuiSchemeManager = NULL;
	g_pVGuiSystem = NULL;
	g_pVGuiLocalize = NULL;
	g_pMatSystemSurface = NULL;
#endif
}

#ifdef CLIENT_DLL
//-----------------------------------------------------------------------------
// tier3.h's two names, for the one object that still names them: the VR
// stand-in (shell_clientdll.cpp's g_ClientVirtualReality) derives from
// CTier3AppSystem, whose inline Connect/Disconnect call them, so its vtable
// does. The stand-in overrides both and nothing reaches the base's, but the
// references alone would pull tier3.o (and its duplicate globals) off the
// archive. They forward to this file's lookups.
//-----------------------------------------------------------------------------
void ConnectTier3Libraries( CreateInterfaceFn *pFactoryList, int nFactoryCount )
{
	for ( int i = 0; i < nFactoryCount; i++ )
		SourceSharp_ConnectTier3( pFactoryList[ i ] );
}

void DisconnectTier3Libraries()
{
	SourceSharp_DisconnectTier3();
}
#endif
