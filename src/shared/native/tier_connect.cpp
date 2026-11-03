// SS_REASON b  an interface resolved by name at load
// The tier libraries are not linked; this does their engine-factory lookups into
// globals of the same names and types, so every reader links as before.

#include "tier_connect.h"
#ifdef CLIENT_DLL
#include "shell_client_engine_interfaces.h"
#endif

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

// tier2: `materials` stays a global for CMaterialReference, whose source libss_particles.so
// also compiles against its own; the client's other tier globals are its interfaces.
#ifdef CLIENT_DLL
IMaterialSystem* materials = NULL;

void SourceSharp_ConnectTier2(CClientEngineInterfaces& engines, CreateInterfaceFn engineFactory)
{
	engines.m_pFileSystem = (IFileSystem*)engineFactory(FILESYSTEM_INTERFACE_VERSION, NULL);
	engines.m_pMaterials = materials = (IMaterialSystem*)engineFactory(MATERIAL_SYSTEM_INTERFACE_VERSION, NULL);
	engines.m_pHardwareConfig =
		(IMaterialSystemHardwareConfig*)engineFactory(MATERIALSYSTEM_HARDWARECONFIG_INTERFACE_VERSION, NULL);

	// libss_particles.so's own tier1/tier2 globals and ConVars, from the same factory.
	if (!ParticleLib_Connect(engines, engineFactory))
		Error("[SourceSharp] libss_particles.so did not connect to the engine's interfaces\n");
}

void SourceSharp_DisconnectTier2(CClientEngineInterfaces& engines)
{
	ParticleLib_Disconnect();
	engines.m_pMaterials = materials = NULL;
	engines.m_pHardwareConfig = NULL;
}
#endif

#ifdef CLIENT_DLL
// tier3: the model cache; the server's is its DLL tier's (CServerEngineInterfaces).

void SourceSharp_ConnectTier3(CClientEngineInterfaces& engines, CreateInterfaceFn engineFactory)
{
	if (!engineFactory)
		return;

	engines.m_pMdlCache = (IMDLCache*)engineFactory(MDLCACHE_INTERFACE_VERSION, NULL);
	engines.m_pVGuiCore = (vgui::IVGui*)engineFactory(VGUI_IVGUI_INTERFACE_VERSION, NULL);
	engines.m_pVGuiInput = (vgui::IInput*)engineFactory(VGUI_INPUT_INTERFACE_VERSION, NULL);
	engines.m_pVGuiPanel = (vgui::IPanel*)engineFactory(VGUI_PANEL_INTERFACE_VERSION, NULL);
	engines.m_pVGuiSurface = (vgui::ISurface*)engineFactory(VGUI_SURFACE_INTERFACE_VERSION, NULL);
	engines.m_pVGuiSchemes = (vgui::ISchemeManager*)engineFactory(VGUI_SCHEME_INTERFACE_VERSION, NULL);
	engines.m_pVGuiSystem = (vgui::ISystem*)engineFactory(SS_VGUI_SYSTEM_INTERFACE_VERSION, NULL);
	engines.m_pVGuiLocalize = (vgui::ILocalize*)engineFactory(VGUI_LOCALIZE_INTERFACE_VERSION, NULL);
	engines.m_pMatSystemSurface = (IMatSystemSurface*)engineFactory(MAT_SYSTEM_SURFACE_INTERFACE_VERSION, NULL);
}
#endif
