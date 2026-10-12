//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — may this pair's OWNER solve its penetration? =======//
//
// One rule, asked once per penetrating pair, by CCollisionEvent::
// ShouldSolvePenetration on the client.
//
// WHY IT IS A RULE AND NOT AN IF. Stock spells the whole decision as
//
//     if ( pGameData0 == pGameData1 )
//         if ( pObj0->GetGameFlags() & FVPHYSICS_PART_OF_RAGDOLL )
//             C_BaseEntity *pEnt = reinterpret_cast<C_BaseEntity *>(pGameData0);
//             C_BaseAnimating *pAnim = pEnt->GetBaseAnimating();
//
// and the first line is wrong in a way that only appears once something in the
// world has no game data: TWO NULLS COMPARE EQUAL. So a pair of objects that
// belong to no entity at all satisfies "these are two parts of the same thing",
// and the reinterpret_cast three lines later hands a null pointer to a virtual
// call.
//
// That is not hypothetical. It is a SIGSEGV, reproduced from PhysicsSimulate on
// the first frame after C_StandaloneEntity::BecomeRagdoll: a standalone
// ragdoll's objects carry no game data and cannot -- the field is read back as
// a C_BaseEntity * and a standalone entity is not one -- while RagdollActivate
// gives every one of them FVPHYSICS_PART_OF_RAGDOLL, which is the other half of
// the test. Backtrace:
//
//   #0  CCollisionEvent::ShouldSolvePenetration   game/client/physics.cpp:396
//       (pGameData0=0x0, pGameData1=0x0)
//   #19 CPhysicsSystem::PhysicsSimulate           game/client/physics.cpp:530
//   #21 OnRenderStart                             cdll_client_int.cpp:2259
//
// IT IS THE NULL-SUBJECT CLASS, in a place nothing had looked. Every other
// reader of client game data calls GetGameData() and checks the result -- ten
// sites in game/client/physics.cpp, all ten checked -- and this one is HANDED
// the pointer as a parameter, so a search for the call does not find it. A
// grep-based audit of "who reads game data" reports this file as clean.
//
// FALSE MEANS FALL THROUGH TO THE DEFAULT SOLVER, not "nothing happened".
// ShouldSolvePenetration's own `return true` at the bottom means "have the
// default implementation do it", and that is what every object which is not a
// self-penetrating ragdoll already gets. What the arm adds is the owner's
// CONSTRAINT GROUP solving the overlap itself, and reaching that needs an
// entity to ask -- so a pair with no entity has nothing to lose by falling
// through. It also stops two DIFFERENT ownerless ragdolls being mistaken for
// two halves of one, which the equality test alone cannot tell apart.
//
// No engine headers. It takes three facts and answers one question.
//
//=============================================================================//
#ifndef SELF_PENETRATION_RULE_H
#define SELF_PENETRATION_RULE_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Whether the penetrating pair's own constraint group should resolve it.
//
//   bHasGameData     the first object's game data is non-NULL
//   bSameGameData    both objects report the SAME game data
//   bPartOfRagdoll   the first object carries FVPHYSICS_PART_OF_RAGDOLL
//
// The three are asked in that order and each is necessary. Sameness alone was
// stock's whole first test and is the bug: it is satisfied by two nothings.
//-----------------------------------------------------------------------------
inline bool SelfPenetrationSolvedByOwner( bool bHasGameData, bool bSameGameData,
                                          bool bPartOfRagdoll )
{
	return bHasGameData && bSameGameData && bPartOfRagdoll;
}

#endif // SELF_PENETRATION_RULE_H
