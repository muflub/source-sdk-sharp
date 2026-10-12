//========= SourceSharp — the spawn's material reload, engine-free ===========//
//
// plan_map_load P1. The shipped engine's SV_SpawnServer mounts the new map's
// pak (IFileSystem::AddSearchPath( "maps/<map>.bsp", "GAME",
// PATH_ADD_TO_HEAD )), opens map access (BeginMapAccess) and then calls
// materials->ReloadMaterials( NULL ): every resident material re-parsed and
// every shader object restored, which rebuilds every loaded model's meshes.
// 0.56-0.94 s a load on a listen server at 96d57d1, the same on a same-map
// reload. It is the only ReloadMaterials( NULL ) through the engine's
// `materials` pointer (a scan of engine.so's 250 references to it).
//
// This is the state machine between those three calls, free of Source headers
// so it is tested on its own (map_material_reload_test.cpp). The shell
// (shell_map_material_reload.cpp) feeds it the hooked calls. Managed DECIDES
// what to reload (IManagedResources::MapMaterialReload, MapMaterialReload.cs);
// this only carries the answer out, and anything it cannot carry out -- no
// answer, an answer that did not fit -- is the stock full reload.
//
// ARMED ONLY INSIDE A MAP LOAD. A pak mounted and map access open is the
// spawn's window; EndMapAccess closes it. `mat_reloadallmaterials`, which
// reaches the same slot from the material system's own command, runs between
// frames and never inside that window, so it keeps its full reload.
//
//=============================================================================//
#ifndef SOURCESHARP_MAP_MATERIAL_RELOAD_H
#define SOURCESHARP_MAP_MATERIAL_RELOAD_H
#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>
#include <string.h>

class CMapMaterialReload
{
public:
	enum { kPathMax = 260 };

	// IFileSystem::AddSearchPath, after the engine's own. Only a map pak mounted
	// at the head of GAME is the spawn's.
	void OnAddSearchPath( const char *pPath, const char *pPathID, bool bAddToHead )
	{
		if ( !pPath || !pPathID || !bAddToHead || strcmp( pPathID, "GAME" ) != 0 )
			return;
		const size_t n = strlen( pPath );
		if ( n < 4 || n >= kPathMax || strcmp( pPath + n - 4, ".bsp" ) != 0 )
			return;
		memcpy( m_szMounted, pPath, n + 1 );
	}

	// IFileSystem::BeginMapAccess / EndMapAccess.
	void OnBeginMapAccess() { m_bArmed = m_szMounted[0] != '\0'; }
	void OnEndMapAccess() { m_bArmed = false; m_szMounted[0] = '\0'; }

	// IMaterialSystem::ReloadMaterials. `reload( name )` is the original slot;
	// `ask( next, previous, out, size )` is managed's answer: the bytes written
	// (newline-joined names, maybe 0) or -1 for the full reload. Returns how
	// many targeted reloads it made, or -1 when it made the full one.
	template < typename Reload, typename Ask >
	int OnReloadMaterials( const char *pSubString, Reload reload, Ask ask, char *pBuffer, int nBufferSize )
	{
		if ( pSubString != NULL || !m_bArmed )
		{
			reload( pSubString );
			return pSubString ? 1 : -1;
		}
		m_bArmed = false;

		const int nWritten = ask( m_szMounted, m_szPrevious, pBuffer, nBufferSize );
		memcpy( m_szPrevious, m_szMounted, strlen( m_szMounted ) + 1 );
		if ( nWritten < 0 || nWritten >= nBufferSize )
		{
			reload( NULL );
			return -1;
		}

		pBuffer[ nWritten ] = '\0';
		int nReloads = 0;
		for ( char *p = pBuffer; *p; )
		{
			char *pEnd = strchr( p, '\n' );
			if ( pEnd )
				*pEnd = '\0';
			if ( *p )
			{
				reload( p );
				++nReloads;
			}
			if ( !pEnd )
				break;
			p = pEnd + 1;
		}
		return nReloads;
	}

	bool IsArmed() const { return m_bArmed; }
	const char *Previous() const { return m_szPrevious; }

private:
	char m_szMounted[ kPathMax ] = {};
	char m_szPrevious[ kPathMax ] = {};
	bool m_bArmed = false;
};

#endif // SOURCESHARP_MAP_MATERIAL_RELOAD_H
