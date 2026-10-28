// Records, for one View_Render, every call client.so makes on the engine's
// render interfaces, one line each. Off unless managed code asks for a frame.
#pragma once



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

	// ShadowEnter records its `shadow.` line and marks every line until the matching
	// ShadowLeave as the shadow manager's own; ShadowNote is a `shadow.` line with no work.
	void ShadowEnter( const char *pszLine );
	void ShadowLeave();
	void ShadowNote( const char *pszLine );

	// A line too long for Note's buffer: pszPrefix then pszText, verbatim.
	void NoteLong( const char *pszPrefix, const char *pszText );
}
