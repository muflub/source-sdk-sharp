//========= SourceSharp — no typed handle over the shell =====================//
//
// plan_entity.md Phase 1 step 1e: "Every typed handle becomes CBaseHandle plus
// a slot-table resolve at the use site." This header is the half of that
// sentence a text census cannot enforce — it makes the typed handle
// IMPOSSIBLE TO WRITE rather than merely absent today.
//
// WHY A BAN AND NOT A CONVENTION. The census counts spellings in the compiled
// TUs, so it reports the day somebody writes `CHandle< CStandaloneEntity >`
// — but only if somebody runs it, and only after the fact. The failure it is
// reporting is not a style lapse. `CHandle<T>::Get()` is
//
//     return (T*)CBaseHandle::Get();
//
// (game/shared/ehandle.h) — a C-style cast off whatever the game's entity list
// hands back, with no check that the object at the other end is a T. Applied to
// this shell it is wrong twice over:
//
//   THE TABLE IT ASKS IS THE WRONG TABLE. CBaseHandle::Get() reads
//   g_pEntityList. §3.3's whole point is that the shim resolves through the
//   SLOT TABLE, which checks the serial and returns the pointer that was bound
//   — SourceSharp_ShellFromHandle, one downcast, allowlisted by file and line
//   in tools/entity-gates-allowlist, and covered by slot_table_test.cpp's
//   SerialReuse cases.
//
//   THE CAST IS UNCHECKED. An entry can hold an entity of the OTHER hierarchy,
//   and handle_cross.h has the whole argument: the address even LOOKS right,
//   because both hierarchies put IHandleEntity at the front; what is wrong is
//   every vtable slot past the shared interface. The failure is a call that
//   dispatches into a different class's function, and it appears at the first
//   virtual rather than at the cast.
//
// HOW THE BAN WORKS, AND WHAT IT DELIBERATELY STILL ALLOWS. The explicit
// specialization below is DECLARED AND NEVER DEFINED, so
// CHandle< CStandaloneEntity > is an incomplete type: an object of it cannot be
// declared, a member cannot be named, and sizeof is ill-formed. What remains
// legal is naming a pointer or a reference to it — which nothing can then
// dereference — because forbidding that would take a specialization the
// language does not offer and would buy nothing.
//
// It bans exactly one instantiation. CHandle over the SDK's own entities is
// stock's and is not this step's business; the shim's copies of it are what 1e
// removed, and this stops that one from coming back.
//
// AN ILL-FORMED PROGRAM IS THE ONLY THING THAT CAN CHECK THIS. No gtest
// assertion can say "this type cannot be instantiated" — an assertion about a
// type that does not exist does not compile either, and one about a type that
// does passes whether or not the ban is here. So the check is a compiled
// program that must be REFUSED: build_must_not_compile standalone_handle_ban
// in src/buildcpptests, with a companion leg that compiles the SAME source
// WITHOUT this header and requires it to succeed. Without that companion the
// refusal would be evidence of nothing — a typo refuses just as loudly.
//
//=============================================================================
#pragma once

// ENGINE-FREE ON PURPOSE, and that is what lets the probe compile it on its
// own. Neither name needs a definition here: the primary template is declared
// the way game/shared/ehandle.h declares it, and the shell is the class this
// header is about. Both are redeclarations wherever the real ones are already
// in scope, which is legal and is how this header stays includable from
// anywhere.
template< class T > class CHandle;
class CStandaloneEntity;

// THE BAN. Declared, never defined. See the block above for what that leaves
// legal and why.
//
// It must be seen BEFORE any implicit instantiation in the same translation
// unit, which is why shell_standalone.h includes it: that header is what makes
// CStandaloneEntity nameable at all, so nothing can spell the banned type
// without having read this first.
template<> class CHandle< CStandaloneEntity >;
