//========= SourceSharp — a renderable's entity handle, asked safely ==========//
//
// The view's draw batch (C7 item 7, shell_standalone_client.cpp) walks every
// renderable in a view's lists to find the standalone shells among them. Not
// every renderable is an entity: a detail sprite, a particle system or a
// CDefaultClientRenderable answers GetIClientEntity() with NULL, and its
// GetRefEHandle() is CDefaultClientRenderable's `Assert( false ); return
// *((CBaseHandle*)0);` -- a null dereference. ss_sandbox's first frame died
// there (2026-10-11). So the handle is asked only of an entity.
//
//=============================================================================//

#ifndef STANDALONE_RENDERABLE_HANDLE_H
#define STANDALONE_RENDERABLE_HANDLE_H

#include <stdint.h>

// True, with the handle word, when pUnknown is an entity; false otherwise,
// without asking it for a handle.
template <class Unknown>
inline bool RenderableEntityHandleWord( Unknown *pUnknown, uint32_t &nWord )
{
	if ( !pUnknown || !pUnknown->GetIClientEntity() )
		return false;
	nWord = (uint32_t)pUnknown->GetRefEHandle().ToInt();
	return true;
}

#endif // STANDALONE_RENDERABLE_HANDLE_H
