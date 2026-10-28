//========= SourceSharp — no typed handle over the client shell ==============//
//
// plan_entity.md Phase 5 step 5d: 1e's gate, client side. 1e's twin of this
// file is sourcesharp/game/server/standalone_handle_ban.h and the argument for
// the mechanism is made there in full; this header says the half that is
// DIFFERENT on the client, which is the half worth reading.
//
// WHY THE CLIENT NEEDS ITS OWN BAN AND NOT A SHARED ONE. The two shells are two
// classes — CStandaloneEntity and C_StandaloneEntity — declared in two headers
// compiled into two DLLs, and an explicit specialization has to name the type
// it specializes. There is no expression that bans "the shell" on both sides at
// once, so a shared header would either be a ban on nothing (both names
// forward-declared, neither the real class) or a header that cannot be included
// from either side. Two headers, one per side, is the same shape the rest of
// this port takes for the same reason.
//
// AND THE CLIENT'S HAZARD IS STRICTLY WORSE. On the server, CHandle<T>::Get()
// reads g_pEntityList — the table this plan deletes, so a typed handle over the
// shell resolves against something that is going away. On the CLIENT, since
// step 5a, CHandle<T>::Get() reads ClientEntityList(), and ClientEntityList()
// IS the slot table: CSourceSharpClientEntityList, whose GetClientUnknown*
// arms answer for BOTH kinds of occupant — a C_StandaloneEntity shell out of
// §3.3's table, and a stock client entity out of the port seam that is left of
// CClientEntityList. So the typed handle does not fail to resolve here. It
// resolves, successfully, to whichever of the two the entry holds, and then
// applies
//
//     return (T*)CBaseHandle::Get();                     (game/shared/ehandle.h)
//
// to the answer. A stock C_BaseEntity behind a CHandle< C_StandaloneEntity >,
// or a shell behind a CHandle< C_BaseEntity >, comes back as a live pointer of
// the wrong class at an address that even looks right — both hierarchies put
// IHandleEntity at the front — and what is wrong is every vtable slot past the
// shared interface. That is behaviour ledger row C8, and beam_endpoint.h
// (whose route is BeamEndpoint.cs's since the C2 beam port) recorded what it
// looked like when it happened: a client killed outright at the
// first virtual call in ComputeBeamEntPosition.
//
// The route this replaces it with is SourceSharp_ClientUnknownFromHandle plus
// SDKEntityFromUnknown (or SourceSharp_ClientShellFromHandle for a shell): the
// hierarchy question asked through a virtual, so an occupant of the other kind
// comes back NULL rather than mis-typed.
//
// HOW THE BAN WORKS. The explicit specialization below is DECLARED AND NEVER
// DEFINED, so CHandle< C_StandaloneEntity > is an incomplete type: an object of
// it cannot be declared, a member cannot be named, and sizeof is ill-formed.
// Naming a pointer or a reference to it stays legal — nothing can then
// dereference one — because forbidding that would take a specialization the
// language does not offer and would buy nothing.
//
// AN ILL-FORMED PROGRAM IS THE ONLY THING THAT CAN CHECK THIS, and a refusal on
// its own is evidence of nothing, because a typo refuses just as loudly. So
// src/buildcpptests compiles ONE source twice, differing by one #include:
// build_must_not_compile standalone_handle_ban_client, and the CONTROL leg
// standalone_handle_ban_client_absent which must compile AND RUN.
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
class C_StandaloneEntity;

// THE BAN. Declared, never defined.
//
// It must be seen BEFORE any implicit instantiation in the same translation
// unit, which is why shell_standalone_client.h includes it: that header is what
// makes C_StandaloneEntity nameable at all, so nothing can spell the banned
// type without having read this first.
template<> class CHandle< C_StandaloneEntity >;
