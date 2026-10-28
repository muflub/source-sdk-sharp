// Bans CHandle< C_StandaloneEntity >. A typed client handle resolves through
// the slot table to whichever kind of occupant the entry holds, and then
// casts it, so the wrong class comes back as a live pointer. Use
// SourceSharp_ClientUnknownFromHandle with SDKEntityFromUnknown (or
// SourceSharp_ClientShellFromHandle) instead, which answer NULL for the other kind.
#pragma once

template< class T > class CHandle;
class C_StandaloneEntity;

// Declared, never defined. Must be seen before any implicit instantiation, so
// shell_standalone_client.h includes it.
template<> class CHandle< C_StandaloneEntity >;
