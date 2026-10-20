//========= SourceSharp — the render call recorder ============================//
//
// plan_client_novalve.md C7 step 1: the instrument the render plan is measured
// with. Armed for exactly one View_Render, it records, in order, every call
// client.so makes on the engine's render interfaces (IVRenderView,
// IMatRenderContext, IVModelRender, IStaticPropMgrClient and the four render
// members of IVEngineClient), one line each. OFF unless managed asks for a
// frame; installing it is a slot write per hooked method and so is removing it.
//
// shell_render_trace_client.cpp says what each line holds and why the engine's
// own calls on the same objects are not in it.
//
//=============================================================================//
#ifndef SHELL_RENDER_TRACE_CLIENT_H
#define SHELL_RENDER_TRACE_CLIENT_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>

namespace RenderTrace
{
	// Installs the hooks and starts an empty trace. False when one is already
	// running or a hook could not be installed (then nothing is installed).
	bool Begin();

	// Removes every hook Begin installed. The text stays until the next Begin.
	void End();

	bool Active();

	// The finished trace: one call per line, '\n'-terminated.
	const char *Text( int32_t *pLength );

	// A line the caller adds (the frame's inputs, a marker). Recorded only while
	// a trace is running.
	void Note( const char *pszFormat, ... );

	// The shadow manager's frame work (C7 cluster 12 S2, shell_shadows_client.cpp):
	// ShadowEnter records its `shadow.` line and marks every line until the
	// matching ShadowLeave `~`, as the calls the manager makes inside it are
	// its own; ShadowNote is a `shadow.` line with no work inside it.
	void ShadowEnter( const char *pszLine );
	void ShadowLeave();
	void ShadowNote( const char *pszLine );

	// A line too long for Note's buffer: pszPrefix then pszText, verbatim.
	void NoteLong( const char *pszPrefix, const char *pszText );
}

#endif // SHELL_RENDER_TRACE_CLIENT_H
