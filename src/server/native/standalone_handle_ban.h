// Bans CHandle< CStandaloneEntity >. CHandle<T>::Get() reads g_pEntityList and
// casts unchecked, and an entry can hold an entity of the other hierarchy; a shell
// handle resolves through the slot table (SourceSharp_ShellFromHandle) instead.
#pragma once

// Redeclarations only, so this header compiles without the engine.
template <class T>
class CHandle;
class CStandaloneEntity;

// Declared, never defined, so the type is incomplete. It must be seen before any
// implicit instantiation, which is why shell_standalone.h includes it.
template <>
class CHandle<CStandaloneEntity>;
