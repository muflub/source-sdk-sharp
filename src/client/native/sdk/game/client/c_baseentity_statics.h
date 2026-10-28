//========= SourceSharp — c_baseentity.cpp's frame-loop statics ==============//
//
// NINE of C_BaseEntity's `static` member functions, out of the class scope.
//
// Of the two hundred and eighty-one CBaseEntity members defined out of line in
// game/client/c_baseentity.cpp at e806bce7, EIGHTEEN are declared `static`. A
// static member takes no `this`, so the class was supplying a namespace and
// nothing else -- the same finding, and the same fix, as the seventeen sound
// statics, the seven physics statics and entity_shared_statics.h's five. Under
// plan_baseentity.md Phase 13's `using CBaseEntity = IGameEntity;` an alias is
// not a class scope, so a definition written `C_BaseEntity::Foo()` is not a
// definition -- it is a compile error, which is what puts this file in step 3's
// removal set.
//
// THESE ARE THE FRAME LOOP'S. cdll_client_int.cpp calls five of C_BaseEntity's
// statics once per frame, in order, from CHLClient::FrameStageNotify:
//
//     InterpolateServerEntities   run interpolation for every entity that
//                                 wants it, then the teleport list  (lifted)
//     MarkAimEntsDirty            clear DIRTY_ABSTRANSFORM on the aim-ents so
//                                 they recompute                    (lifted)
//     CalcAimEntPositions         then recompute them               (lifted)
//     ToolRecordEntities          hand the frame to the tool framework (lifted)
//     CheckCLInterpChanged        notice a cl_interp convar change  (lifted)
//
// Not one of them is about a particular entity -- each walks a global list --
// which is why each was already `static`. All five are lifted now, along with
// the three the first two call (ProcessTeleportList, ProcessInterpolatedList,
// CheckInterpolatedVarParanoidMeasurement) and AddVisibleEntities.
//
// THE BODIES DO NOT MOVE, and stay in c_baseentity.cpp. They reach that
// translation unit's own g_AimEntsList, g_TeleportList, g_InterpolationList and
// the cl_interpolate / cl_interp ConVar objects. Exporting that state to move
// three bodies would be a bigger change than the lift and would buy nothing.
// Only the SCOPE each definition is written in moves, exactly as it did for
// GameSound_*, PhysicsLink_* and EntitySim_IsSimulatingOnAlternateTicks.
//
// THE CLASS-SCOPE NAMES SURVIVE AS INLINE FORWARDERS in c_baseentity.h rather
// than in a quarantine .cpp. An inline forwarder emits no symbol, so the lift
// takes three OFF c_baseentity.o instead of moving three into another file, it
// needs no client_csharp.vpc edit, and the block is deleted by the same edit
// that deletes the class body at step 4.
//
// THE SIX THIS FILE ONCE LISTED AS BLOCKED ARE LIFTED, and the decision it
// declined to make has been made. It said:
//
//     Each would need a friend declaration, an accessor, or the field made
//     public, and each of those is a decision about the class's surface
//     rather than a mechanical move -- so they are named here and left.
//
// FOUR of them do need one, and the answer is a `friend` block at the bottom of
// C_BaseEntity, which is argued where it is written. It grants exactly these
// four names exactly the access the member versions already had, adds nothing
// to the class's public surface, and is deleted by the same edit that deletes
// the class body at step 4 -- where an accessor would have to be found and
// removed on its own:
//
//     EntityInterp_ProcessTeleportList        pCur->m_hOldMoveParent,
//                                             m_hNetworkMoveParent,
//                                             m_iOldParentAttachment,
//                                             m_iParentAttachment,
//                                             RemoveFromTeleportList()
//     EntityInterp_ProcessInterpolatedList    pEnt->m_bReadyToDraw
//     EntityInterp_CheckParanoidMeasurement   pEnt->m_InterpolationListEntry
//     EntityInterp_CalcAimEntPositions        the private CalcAbsolutePosition()
//
// TWO OF THE SIX NEEDED NOTHING, and one of those was blocked on a claim that
// was simply wrong:
//
//     InterpolateServerEntities  was blocked on the two PROTECTED statics it
//                                calls. Both are free functions now, so the
//                                blocker dissolved rather than being worked
//                                around. Its third unqualified member call,
//                                IsSimulatingOnAlternateTicks(), goes to
//                                EntitySim_IsSimulatingOnAlternateTicks() --
//                                which is now the ONLY implementation: the
//                                member was a one-line forwarder to exactly
//                                this and its quarantine has been retired, so
//                                the two cannot disagree.
//     AddVisibleEntities         was listed as blocked on "ent->
//                                m_PredictableID". IT IS NOT. m_PredictableID
//                                is PUBLIC (c_baseentity.h:1407, under the
//                                `public:` at 1369), as is everything else the
//                                body touches, and the compiler lifts it clean
//                                with no friend at all.
//
// THAT LAST ONE IS THE LESSON, and it is the one this file already stated and
// then did not follow: a liftability verdict has to come from the COMPILER, not
// from reading the body. A member named `m_PredictableID` reads private and is
// not; CalcAbsolutePosition() reads like an ordinary call and is private.
// One wrong in each direction, in the same list of six.
//
// AND ONE VERDICT HERE IS TRUE BUT NOT PROVEN BY ITS OWN PROBE.
// CheckInterpolatedVarParanoidMeasurement's ENTIRE BODY sits behind
// INTERPOLATEDVAR_PARANOID_MEASUREMENT, which interpolatedvar.h:27 has
// commented out. Compile it alone and it lifts clean because there is nothing
// in it to fail. Its friend declaration is there for the configuration this
// build is not, and dropping it would go green here and break the first time
// anyone turned the define on.
//
// THE REMAINING STATICS are either already handled (the six in
// entity_abs_query_state.h, the sound statics, IsSimulatingOnAlternateTicks) or
// belong to a different subsystem's lane -- Instance x2, PrecacheModel,
// IsServer, IsClient, GetDLLType. CreatePredictedEntityByName is liftable and
// is NOT done here: its blocker is CPredictableList::AddToPredictableList,
// which is protected on ANOTHER class, so the friend would have to go on
// CPredictableList rather than on C_BaseEntity. That is predictable_entity.h's
// lane, which this one must not touch.
//
// NO ENTITY TYPE APPEARS IN ANY SIGNATURE HERE, which is why this header
// carries no `#ifndef SS_CBASEENTITY_ALIAS` block: there is nothing to guard,
// rather than a guard that was forgotten. alias_mechanics_test's
// NoForwardDeclarationSurvivesTheAlias sweeps every header in the tree and would
// catch it if that changed.
//
// CLIENT ONLY. This header is included by game/client/c_baseentity.h and by
// nothing on the server link line.
//
//=============================================================================//
#pragma once
#define C_BASEENTITY_STATICS_H

//-----------------------------------------------------------------------------
// The aim-ent pass, in two halves. The first clears DIRTY_ABSTRANSFORM on every
// aim-ent so that the next CalcAbsPosition recomputes it; the second does the
// recomputing. cdll_client_int.cpp runs them back to back.
//
// The second is a friend of C_BaseEntity: CalcAbsolutePosition() is private,
// which no grep for `->m_` can see. It was found by lifting it and reading the
// compiler, which is the only reliable oracle for this.
//-----------------------------------------------------------------------------
void EntityInterp_MarkAimEntsDirty();
void EntityInterp_CalcAimEntPositions();

//-----------------------------------------------------------------------------
// The interpolation pass. InterpolateServerEntities republishes s_bInterpolate
// (see entity_abs_query_state.h), re-derives every entity's interpolation
// amounts when the alternate-ticks or threading mode has changed, and then runs
// the two lists.
//
// ProcessTeleportList and ProcessInterpolatedList are friends of C_BaseEntity;
// InterpolateServerEntities is not, and does not need to be.
//-----------------------------------------------------------------------------
void EntityInterp_InterpolateServerEntities();
void EntityInterp_ProcessTeleportList();
void EntityInterp_ProcessInterpolatedList();

//-----------------------------------------------------------------------------
// The paranoid cross-check: walk every entity NOT in g_InterpolationList and
// warn about any that should have been. Compiled away entirely unless
// interpolatedvar.h's INTERPOLATEDVAR_PARANOID_MEASUREMENT is defined, which it
// is not -- so this is an empty function in every build the tree makes today.
// It is a friend of C_BaseEntity all the same, because the body it grows when
// the define is on reads the private m_InterpolationListEntry.
//-----------------------------------------------------------------------------
void EntityInterp_CheckParanoidMeasurement();

//-----------------------------------------------------------------------------
// Add the client-created predictables that have not been acknowledged yet to
// the leaf system, so they draw until the real entity arrives. Needs no friend:
// every member it touches, m_PredictableID included, is public.
//-----------------------------------------------------------------------------
void EntityRender_AddVisibleEntities();

//-----------------------------------------------------------------------------
// Hand every recording entity to the tool framework for this frame.
//-----------------------------------------------------------------------------
void EntityTool_RecordEntities();

//-----------------------------------------------------------------------------
// Notice that cl_interp or cl_interp_ratio changed and republish the effective
// value. Called once per frame; cheap when nothing changed.
//-----------------------------------------------------------------------------
void EntityInterp_CheckCLInterpChanged();

//-----------------------------------------------------------------------------
// THE ENTITY ALLOCATOR, which is not a static member function and is here
// anyway, because it is the same move for the same reason.
//
// C_BaseEntity declares its own operator new, operator new[], two four-argument
// debug forms, and operator delete, and c_baseentity.cpp defined five of the
// six out of line -- five more definitions written `C_BaseEntity::` for step 3
// to empty. What they do is allocate through MemAlloc and ZERO the block, which
// is why the class has them at all: "uses calloc so members are zero'd out on
// instantiation".
//
// THE OPERATORS THEMSELVES DID NOT LEAVE THE CLASS, and lifting them would have
// been a serious bug rather than a mechanical move. A free
// `operator new( size_t )` is not a lifted C_BaseEntity::operator new, it is a
// replacement for the GLOBAL one -- so every allocation in client.so would
// start going through an allocator that memsets. They stay as inline members
// forwarding here, which emits no symbol and so still takes the five off
// c_baseentity.o.
//
// FIVE OPERATORS, THREE HELPERS. operator new and operator new[] have
// character-identical bodies; so do their four-argument debug forms. The
// operators have to be separate because the language says so; the helpers do
// not. nBlockUse is not passed on because neither body ever read it.
//
// THE BODIES STAY IN c_baseentity.cpp, and here that is load-bearing twice
// over: MEM_ALLOC_CREDIT() records __FILE__, so a helper in another .cpp would
// silently re-credit every entity allocation; and the definitions sit inside
// that file's memdbgoff/memdbgon bracket, without which memdbgon.h's
// redefinition of new and delete is in force while they are compiled.
//-----------------------------------------------------------------------------
void *EntityMem_Alloc( size_t stAllocateBlock );
void EntityMem_Free( void *pMem );

//-----------------------------------------------------------------------------
// Whether the entity is floating in a fluid, on the CLIENT, where the answer is
// always no.
//
// The server's CBaseEntity::IsFloating is EFL_TOUCHING_FLUID plus a density
// lookup (baseentity.cpp:6688); the client has no fluid density to consult and
// the member was `return false;` with a note saying it exists only because
// shared code calls it. Non-virtual on BOTH sides -- baseentity.h:533 and
// c_baseentity.h:413 -- which had to be checked, because a one-sided read of a
// member that is virtual on one side and plain on the other compiles green in
// both configurations and breaks the other side at step 3.
//
// The one caller is movement_ground.h:135, through the class-scope name, which
// survives as an inline forwarder.
//-----------------------------------------------------------------------------
bool EntityWater_ClientIsFloating();
