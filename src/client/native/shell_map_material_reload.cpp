//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the spawn's material reload, wired in =============//
// SS_REASON a  a vtable the engine calls through
//
// plan_map_load P1. map_material_reload.h says what and why; this installs it.
// Four slots of two engine singletons are pointed at the functions below:
// IFileSystem's AddSearchPath, BeginMapAccess and EndMapAccess, and
// IMaterialSystem's ReloadMaterials. Each slot is the one the SDK header the
// mod compiled against names (VTableHook::MemberOf, adjust 0, all four are
// declared by the interface itself), and each original is called first or
// instead, so with no managed answer every call behaves exactly as stock.
//
// CLIENT SIDE. The material system is the renderer's, and client.so is loaded
// exactly where there is one: a listen server's process and a client's, never
// a dedicated server. Only a listen server's spawn reaches the slot armed; a
// remote client never mounts a pak inside a spawn.
//
// THE PAK IS PASSED AS A FILE ON DISK. The spawn mounts "maps/<map>.bsp"; the
// filesystem's own RelativePathToFullPath turns it into the file managed reads,
// because the client has no read slot on the engine filesystem.
//
// UNDONE AT Shutdown, because the functions live in client.so: a slot left
// pointing here would outlive the code it names.
//
//=============================================================================//
#include "cbase.h"
#include "filesystem.h"
#include "materialsystem/imaterialsystem.h"
#include "imanaged_runtime_client.h"
#include "shell_managed_areas_client.h"
#include "vtable_hook.h"
#include "map_material_reload.h"

#include <sys/mman.h>
#include <unistd.h>

namespace
{
// The hooks are called with the engine's object as `this`, so this is how they
// reach their state. One per process; set by Install, cleared by Uninstall.
struct MapMaterialReloadHooks
{
	CMapMaterialReload core;
	void **pFileSystemVtbl = nullptr;
	void **pMaterialsVtbl = nullptr;
	int nAddSearchPath = -1, nBeginMapAccess = -1, nEndMapAccess = -1, nReloadMaterials = -1;
	void *pAddSearchPath = nullptr, *pBeginMapAccess = nullptr, *pEndMapAccess = nullptr, *pReloadMaterials = nullptr;
	// Managed's answer, newline-joined names. Here rather than on the stack
	// (the names of two paks) or as a static (one more global for nothing).
	char szNames[ 256 * 1024 ];
};
MapMaterialReloadHooks *s_pHooks = nullptr;

typedef void ( *AddSearchPathFn )( IFileSystem *, const char *, const char *, SearchPathAdd_t );
typedef void ( *MapAccessFn )( IFileSystem * );
typedef void ( *ReloadMaterialsFn )( IMaterialSystem *, const char * );

void HookAddSearchPath( IFileSystem *pThis, const char *pPath, const char *pPathID, SearchPathAdd_t addType )
{
	( (AddSearchPathFn)s_pHooks->pAddSearchPath )( pThis, pPath, pPathID, addType );
	char szFull[ CMapMaterialReload::kPathMax ];
	const char *pFull = ( pPath && pPathID && !V_stricmp( pPathID, "GAME" ) &&
	                      pThis->RelativePathToFullPath( pPath, "GAME", szFull, sizeof( szFull ) ) ) ? szFull : pPath;
	s_pHooks->core.OnAddSearchPath( pFull, pPathID, addType == PATH_ADD_TO_HEAD );
}

void HookBeginMapAccess( IFileSystem *pThis )
{
	( (MapAccessFn)s_pHooks->pBeginMapAccess )( pThis );
	s_pHooks->core.OnBeginMapAccess();
}

void HookEndMapAccess( IFileSystem *pThis )
{
	( (MapAccessFn)s_pHooks->pEndMapAccess )( pThis );
	s_pHooks->core.OnEndMapAccess();
}

void HookReloadMaterials( IMaterialSystem *pThis, const char *pSubString )
{
	ReloadMaterialsFn pOriginal = (ReloadMaterialsFn)s_pHooks->pReloadMaterials;
	const int nResult = s_pHooks->core.OnReloadMaterials(
		pSubString,
		[ pThis, pOriginal ]( const char *pName ) { pOriginal( pThis, pName ); },
		[]( const char *pNext, const char *pPrevious, char *pOut, int nOut ) -> int
		{
			IManagedRuntimeClient *pRuntime = ManagedRuntimeClient();
			return pRuntime ? pRuntime->MapMaterialReload( pNext, pPrevious, pOut, nOut ) : -1;
		},
		s_pHooks->szNames, sizeof( s_pHooks->szNames ) );
	if ( pSubString == NULL && nResult >= 0 )
		Msg( "[SourceSharp] map materials: %d targeted reload(s) instead of ReloadMaterials( NULL )\n", nResult );
}

bool Patch( void **pVtbl, int nSlot, void *pNew, void **ppOld )
{
	const long nPage = sysconf( _SC_PAGESIZE );
	uintptr_t page = (uintptr_t)&pVtbl[ nSlot ] & ~(uintptr_t)( nPage - 1 );
	if ( mprotect( (void *)page, nPage, PROT_READ | PROT_WRITE ) != 0 )
		return false;
	if ( ppOld )
		*ppOld = pVtbl[ nSlot ];
	pVtbl[ nSlot ] = pNew;
	mprotect( (void *)page, nPage, PROT_READ );
	return true;
}

template < typename T >
int SlotOf( T pMember )
{
	const VTableHook::Member m = VTableHook::MemberOf( pMember );
	return ( m.isVirtual && m.adjust == 0 ) ? m.slot : -1;
}
} // namespace

void SourceSharp_UninstallMapMaterialReload();

bool SourceSharp_InstallMapMaterialReload( IFileSystem *pFileSystem, IMaterialSystem *pMaterials )
{
	if ( s_pHooks || !pFileSystem || !pMaterials )
		return false;

	MapMaterialReloadHooks *p = new MapMaterialReloadHooks;
	p->nAddSearchPath = SlotOf( &IFileSystem::AddSearchPath );
	p->nBeginMapAccess = SlotOf( &IFileSystem::BeginMapAccess );
	p->nEndMapAccess = SlotOf( &IFileSystem::EndMapAccess );
	p->nReloadMaterials = SlotOf( &IMaterialSystem::ReloadMaterials );
	if ( p->nAddSearchPath < 0 || p->nBeginMapAccess < 0 || p->nEndMapAccess < 0 || p->nReloadMaterials < 0 )
	{
		Warning( "[SourceSharp] map materials: a hooked method is not a plain virtual of its interface; not installed\n" );
		delete p;
		return false;
	}
	p->pFileSystemVtbl = *(void ***)pFileSystem;
	p->pMaterialsVtbl = *(void ***)pMaterials;

	s_pHooks = p;
	const bool bOk = Patch( p->pFileSystemVtbl, p->nAddSearchPath, (void *)&HookAddSearchPath, &p->pAddSearchPath )
	              && Patch( p->pFileSystemVtbl, p->nBeginMapAccess, (void *)&HookBeginMapAccess, &p->pBeginMapAccess )
	              && Patch( p->pFileSystemVtbl, p->nEndMapAccess, (void *)&HookEndMapAccess, &p->pEndMapAccess )
	              && Patch( p->pMaterialsVtbl, p->nReloadMaterials, (void *)&HookReloadMaterials, &p->pReloadMaterials );
	if ( !bOk )
	{
		Warning( "[SourceSharp] map materials: could not patch a vtable; not installed\n" );
		SourceSharp_UninstallMapMaterialReload();
		return false;
	}
	Msg( "[SourceSharp] map materials: the spawn's ReloadMaterials( NULL ) is targeted (plan_map_load P1)\n" );
	return true;
}

void SourceSharp_UninstallMapMaterialReload()
{
	MapMaterialReloadHooks *p = s_pHooks;
	if ( !p )
		return;
	if ( p->pAddSearchPath )
		Patch( p->pFileSystemVtbl, p->nAddSearchPath, p->pAddSearchPath, nullptr );
	if ( p->pBeginMapAccess )
		Patch( p->pFileSystemVtbl, p->nBeginMapAccess, p->pBeginMapAccess, nullptr );
	if ( p->pEndMapAccess )
		Patch( p->pFileSystemVtbl, p->nEndMapAccess, p->pEndMapAccess, nullptr );
	if ( p->pReloadMaterials )
		Patch( p->pMaterialsVtbl, p->nReloadMaterials, p->pReloadMaterials, nullptr );
	s_pHooks = nullptr;
	delete p;
}
