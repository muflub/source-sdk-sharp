//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's skeleton, and parenting's relays ========//
// SS_REASON g  leaf math over mdl/bsp data
//
// TWO THINGS LIVE IN THIS FILE AND THEY ARE VERY DIFFERENT SIZES.
//
// The first is what is left of the SERVER'S SKELETON, which is one thunk: the
// skeleton itself -- the bone cache, the attachments, the sequence clock, every
// rebuild decision -- is SourceSharp.Server's ServerSkeletons over the managed
// bone port (plan physics_bones_native_zero 1.2). TestHitboxes stays because
// the engine's traces call it as an ICollideable virtual; it hands the ray, the
// trace and the entity's engine block to IManagedBones and copies the answer
// back. The three IShimModels bodies at the foot of this file are no longer
// reached on this side (see there).
//
// The second is PARENTING, and there is almost nothing of it here any more.
//
//-----------------------------------------------------------------------------
// PARENTING IS MANAGED POLICY (plan_entity.md §3.2 and §3.5, step 2h).
//
// The registry, the composition arithmetic, the cycle refusal, the deferred
// `parentname`, the frame rule and the once-a-frame sweep are
// SourceSharp.Abi/Hierarchy.cs, driven through IHierarchyWorld so that "a child
// follows its parent through a rotation" is a fact a test can state with no game
// running. HierarchyTests.cs has 52 of them, and two golden tables
// (sourcesharp/test/parent_compose_cases.txt and parent_frame_cases.txt) pin the
// managed arithmetic against the NATIVE arithmetic that used to be here —
// records of what the port replaced rather than of the port, which is what makes
// them worth more now that this file no longer holds the original.
//
// WHAT IS LEFT HERE IS FIFTEEN CALL SITES' WORTH OF RELAY. Every entry point
// into parenting is called from native — SetAbsTransform, the KeyValue parse,
// ~CStandaloneEntity, mapentities.cpp, UpdateMovement, UpdateFlight,
// shell_worldquery.cpp — and each of those keeps the name and the signature it
// always had. The bodies are a guard and a call.
//
//-----------------------------------------------------------------------------
// THE GUARD, WHICH IS THE WHOLE REASON THIS IS AFFORDABLE.
//
// A crossing cannot be conditional on managed state native cannot see, and a
// position write is the hottest thing on this hierarchy. The old bodies opened
// with `g_ParentLinks.Count() == 0` — a global that cost nothing in a map that
// parents nothing — and step 2h's ABI request called this "the one number that
// must be settled before the slots are wired".
//
// It is settled in the ENGINE BLOCK, which is what §3.2 says the block is for:
// *they are answered from memory that managed code writes*. Two words, both
// written by SourceSharp.Abi's HierarchyDriver and neither ever written here:
//
//   Block().Head.ChildCount != 0        something hangs off this entity
//   Block().Head.ParentHandle is valid  something carries this entity
//
// So the tail of a position write is one load at a constant offset and a branch,
// and it is FALSE for every entity in a map that parents nothing — the same
// nothing the global bought, per entity instead of per level, which is strictly
// sharper: a map with one link makes one entity pay rather than all six hundred.
//
// And two questions stop crossing altogether. StandaloneHierarchy_IsParented and
// SourceSharp_EntityGetParent are answered from ParentHandle alone; step 2h
// asked for a slot for the first of them and it does not need one.
//
// NEITHER WORD IS DECIDED HERE. Native reads ChildCount only as "is it zero" and
// ParentHandle only as "does it resolve". Which entity carries which, and how
// many, is the registry's, and the registry is managed.
//
//-----------------------------------------------------------------------------
// TWO ENTRY POINTS WENT AWAY RATHER THAN BECOMING RELAYS.
//
// StandaloneHierarchy_RunFrame and StandaloneHierarchy_LevelShutdown are gone,
// with the two calls that made them. Both already had a managed caller standing
// over them: SourceSharp.Server's StandaloneHierarchy drives this hierarchy's
// frame and its teardown from C#, so the sweep runs between the directory's
// think pass and StandaloneFinish — the same position in the frame the native
// sweep occupied — and the registry is emptied inside the level-shutdown
// crossing that already happens. A slot for either would have been a second
// crossing for work that is already inside one.
//
//-----------------------------------------------------------------------------
// WHAT IS STILL OUT OF SCOPE, and it is one thing.
//
//   A CBaseEntity CHILD of a standalone parent. Stock networks m_hMoveParent —
//   SendPropEHandle( SENDINFO_NAME( m_hMoveParent, moveparent ) ) — and the
//   client rebuilds its hierarchy from it, so an SDK child naming a standalone
//   parent would put a link on the wire into a hierarchy the client side of this
//   port does not have. That is a wire change and a client change, which is what
//   every other direction here avoids paying. SourceSharp_EntitySetParent
//   refuses it OUT LOUD rather than answering 0 in silence: C# holds one handle
//   type for both hierarchies and cannot tell which kind it is holding.
//
//   A standalone child of a CBaseEntity parent is SUPPORTED, and so are
//   attachment points. Both are the managed side's now.
//
//=============================================================================//

#include "ss_util.h"            // UTIL_ClearTrace, engine
#include "shell_standalone.h"
#include "shell_resolve.h"
// The ABI. Parenting's entry points relay into the managed export table, and
// the probe answers with the SOURCESHARP_PARENT_PROBE_* bits it declares.
#include "sourcesharp.h"
// The flat ray and trace IManagedBones::TestHitboxes carries.
#include "standalone_bone_trace.h"
#include "ss_string.h"


std::vector< CStandaloneEntity * > &StandaloneEntities();


// The managed export table, published by the gamesystem. Declared here rather
// than included, which is what every other shell that reaches it does -- see
// shell_dispatch.cpp and shell_decals.cpp.
#include "imanaged_areas.h"   // plan_engineapi_vtables E8: IManagedHierarchy; IManagedBones

//=============================================================================//
// THE SERVER'S SKELETON IS MANAGED (plan physics_bones_native_zero 1.2).
//
// What stood here -- EnsureBones, the attachment reads, BoneCount,
// LookupAttachment, SetPoseSequence, PoseCycle and the bone math they drove
// through bone_library.cpp -- is SourceSharp.Server's ServerSkeletons over
// SourceSharp.Shared.Bones' ServerBoneDriver. Every managed caller asks it
// directly; the engine's hitbox trace is the one question that still arrives
// here, as an ICollideable virtual, and this is its thunk.
//=============================================================================//

//-----------------------------------------------------------------------------
// A RAY AGAINST THIS MODEL'S HITBOXES.
//
// TRUE IS NOT "THE RAY HIT": it means "I have hitboxes and tested them", and
// tr.DidHit() is the hit, as stock's CBaseAnimating::TestHitboxes means the
// pair. False -- no managed side, no model, no hitbox set -- leaves the trace
// untouched and the engine falls back to the box.
//
// The decision is all managed; what is here is the layout. The ray and the
// trace cross flat (standalone_bone_trace.h), the entity's engine block by
// pointer -- it is where its model, place, sequence clock and hitbox set are,
// so the trace costs one crossing and no read back. The surface NAME stays
// native: managed says whether the hit named it "**studio**", and the literal
// is written here.
//-----------------------------------------------------------------------------
static uint32_t HandleOf( const CStandaloneEntity *pEntity );   // below, with the relays

bool CStandaloneEntity::TestHitboxes( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr )
{
	IManagedBones *pBones = ManagedBones();

	if ( !pBones )
		return false;

	float flRay[ SS_BONE_RAY_FLOATS ];

	for ( int i = 0; i < 3; i++ )
	{
		flRay[ SS_BONE_RAY_START_X + i ]        = ray.m_Start[i];
		flRay[ SS_BONE_RAY_DELTA_X + i ]        = ray.m_Delta[i];
		flRay[ SS_BONE_RAY_START_OFFSET_X + i ] = ray.m_StartOffset[i];
		flRay[ SS_BONE_RAY_EXTENTS_X + i ]      = ray.m_Extents[i];
	}

	flRay[ SS_BONE_RAY_IS_RAY ]   = ray.m_IsRay ? 1.0f : 0.0f;
	flRay[ SS_BONE_RAY_IS_SWEPT ] = ray.m_IsSwept ? 1.0f : 0.0f;

	float   flTrace[ SS_BONE_TRACE_FLOATS ];
	int32_t nTrace[ SS_BONE_TRACE_INTS ];

	for ( int i = 0; i < 3; i++ )
	{
		flTrace[ SS_BONE_TRACE_START_X + i ]        = tr.startpos[i];
		flTrace[ SS_BONE_TRACE_END_X + i ]          = tr.endpos[i];
		flTrace[ SS_BONE_TRACE_PLANE_NORMAL_X + i ] = tr.plane.normal[i];
	}

	flTrace[ SS_BONE_TRACE_PLANE_DIST ] = tr.plane.dist;
	flTrace[ SS_BONE_TRACE_FRACTION ]   = tr.fraction;

	nTrace[ SS_BONE_TRACE_PLANE_TYPE ]        = tr.plane.type;
	nTrace[ SS_BONE_TRACE_CONTENTS ]          = tr.contents;
	nTrace[ SS_BONE_TRACE_ALL_SOLID ]         = tr.allsolid ? 1 : 0;
	nTrace[ SS_BONE_TRACE_START_SOLID ]       = tr.startsolid ? 1 : 0;
	nTrace[ SS_BONE_TRACE_HIT_GROUP ]         = tr.hitgroup;
	nTrace[ SS_BONE_TRACE_HIT_BOX ]           = tr.hitbox;
	nTrace[ SS_BONE_TRACE_PHYSICS_BONE ]      = tr.physicsbone;
	nTrace[ SS_BONE_TRACE_SURFACE_FLAGS ]     = tr.surface.flags;
	nTrace[ SS_BONE_TRACE_SURFACE_PROPS ]     = tr.surface.surfaceProps;
	nTrace[ SS_BONE_TRACE_SURFACE_IS_STUDIO ] = 0;

	if ( !pBones->TestHitboxes( HandleOf( this ), &Block(), flRay, (int32_t)fContentsMask, flTrace, nTrace ) )
		return false;

	for ( int i = 0; i < 3; i++ )
	{
		tr.startpos[i]     = flTrace[ SS_BONE_TRACE_START_X + i ];
		tr.endpos[i]       = flTrace[ SS_BONE_TRACE_END_X + i ];
		tr.plane.normal[i] = flTrace[ SS_BONE_TRACE_PLANE_NORMAL_X + i ];
	}

	tr.plane.dist         = flTrace[ SS_BONE_TRACE_PLANE_DIST ];
	tr.fraction           = flTrace[ SS_BONE_TRACE_FRACTION ];
	tr.plane.type         = (byte)nTrace[ SS_BONE_TRACE_PLANE_TYPE ];
	tr.contents           = nTrace[ SS_BONE_TRACE_CONTENTS ];
	tr.allsolid           = nTrace[ SS_BONE_TRACE_ALL_SOLID ] != 0;
	tr.startsolid         = nTrace[ SS_BONE_TRACE_START_SOLID ] != 0;
	tr.hitgroup           = nTrace[ SS_BONE_TRACE_HIT_GROUP ];
	tr.hitbox             = nTrace[ SS_BONE_TRACE_HIT_BOX ];
	tr.physicsbone        = (short)nTrace[ SS_BONE_TRACE_PHYSICS_BONE ];
	tr.surface.flags      = (unsigned short)nTrace[ SS_BONE_TRACE_SURFACE_FLAGS ];
	tr.surface.surfaceProps = (unsigned short)nTrace[ SS_BONE_TRACE_SURFACE_PROPS ];

	if ( nTrace[ SS_BONE_TRACE_SURFACE_IS_STUDIO ] )
		tr.surface.name = "**studio**";

	return true;
}

//=============================================================================//
// PARENTING'S RELAYS.
//
// Read the file header for why there is nothing else. Each function below keeps
// the name and the signature its callers have always used, so making managed the
// authority changed no call site outside this file except the two that were
// deleted outright.
//=============================================================================//

//-----------------------------------------------------------------------------
// The managed side, or NULL before it has loaded.
//
// Every relay tests it. A map can be parsed and an entity destroyed with no
// managed side present — a dedicated server between reloads, a load that failed
// — and the honest behaviour then is that nothing is parented to anything, which
// is what a null table produces without a special case.
//-----------------------------------------------------------------------------
static IManagedHierarchy *HierarchyApi()
{
	// plan_engineapi_vtables E8: the managed-built hierarchy area, acquired
	// again on every load and NULL outside one -- the same null as before.
	return ManagedHierarchy();
}

//-----------------------------------------------------------------------------
// THE TWO GUARD WORDS, read. Managed code writes both (SourceSharp.Abi's
// HierarchyDriver); nothing here ever does.
//
// Const-correct and pointer-safe, because two of the callers are const and one
// of them is a destructor.
//-----------------------------------------------------------------------------
static bool CarriesAnything( const CStandaloneEntity *pEntity )
{
	return pEntity != NULL && pEntity->Block().Head.ChildCount != 0;
}

static bool IsCarried( const CStandaloneEntity *pEntity )
{
	return pEntity != NULL &&
	       pEntity->Block().Head.ParentHandle != (uint32_t)INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// A handle for a shell, or INVALID_EHANDLE_INDEX. The one place the conversion
// is spelled, so a relay is a guard and a call rather than a guard, a
// conversion and a call.
//-----------------------------------------------------------------------------
static uint32_t HandleOf( const CStandaloneEntity *pEntity )
{
	if ( !pEntity )
		return INVALID_EHANDLE_INDEX;

	const CBaseHandle &handle = pEntity->GetRefEHandle();

	return handle.IsValid() ? (uint32_t)handle.ToInt() : (uint32_t)INVALID_EHANDLE_INDEX;
}

//=============================================================================//
// The two halves of a move.
//=============================================================================//

//-----------------------------------------------------------------------------
// A parent has moved, so its children move with it. From the tail of
// CStandaloneEntity::OnPositionChanged, which is the single place an absolute
// position is written on this hierarchy — which is what makes composition eager
// and therefore what makes ordering a non-question.
//
// THE GUARD IS THE POINT. This is reached on EVERY position write of EVERY
// entity, and ChildCount is zero for all but the handful a map actually parents
// something to.
//-----------------------------------------------------------------------------
void StandaloneHierarchy_ParentMoved( CStandaloneEntity *pParent )
{
	if ( !CarriesAnything( pParent ) )
		return;

	IManagedHierarchy *pApi = HierarchyApi();

	if ( pApi )
		pApi->HierarchyParentMoved( HandleOf( pParent ) );
}

//-----------------------------------------------------------------------------
// A child has moved ITSELF, so its offset from its parent is now something
// else. From SetAbsOrigin and SetAbsAngles — the public setters, which mean
// "the world has been told where this is" — and never from the composition,
// which computed the new absolute FROM that offset.
//-----------------------------------------------------------------------------
void StandaloneHierarchy_ChildMoved( CStandaloneEntity *pChild )
{
	if ( !IsCarried( pChild ) )
		return;

	IManagedHierarchy *pApi = HierarchyApi();

	if ( pApi )
		pApi->HierarchyChildMoved( HandleOf( pChild ) );
}

//-----------------------------------------------------------------------------
// WHETHER SOMETHING CARRIES THIS ENTITY, and it costs no crossing.
//
// Step 2h asked for a slot for this. It does not need one: managed code writes
// the parent's handle into the block when it makes a link and clears it when it
// drops one, so the question is a load and a resolve.
//
// RESOLVED rather than merely tested against the invalid value, which is the
// same answer the registry walk gave: a link whose parent has gone is dropped by
// the managed sweep, and between the parent's death and that sweep the word
// still names it. SourceSharp_ResolveEntity revalidates the serial, so a slot
// the engine has since given to something else answers "nobody" — which is what
// "nothing carries me" has always meant here.
//
// It is a different question from whether the child's FRAME can be built right
// now: a parent whose model has no such attachment still owns the child, and
// reporting it unparented would have UpdateFlight fly it out of its parent's
// hands.
//-----------------------------------------------------------------------------
bool StandaloneHierarchy_IsParented( const CStandaloneEntity *pChild )
{
	if ( !IsCarried( pChild ) )
		return false;

	return SourceSharp_ResolveEntity( pChild->Block().Head.ParentHandle ).IsValid();
}

//=============================================================================//
// The two frames.
//
// An entity with no parent lives in the world's frame and one with a parent
// lives in its parent's, and code that MOVES an entity has to know which — see
// CStandaloneEntity::UpdateMovement, where integrating a parented entity in
// world space means its own step and its parent's composition writing the same
// origin from two different frames, each undoing half of the other's every
// frame. The symptom is jitter, which reads as a solver problem.
//
// Both convert IN PLACE and answer false, touching nothing, for an entity with
// no parent — so a caller that does not care can ignore the result and still be
// correct.
//=============================================================================//

//-----------------------------------------------------------------------------
// The two directions differ by one function pointer, so they are one relay and
// two wrappers rather than two copies. A Vector and a QAngle are three floats
// each and are passed as such: the managed side reads them as float triples, and
// a struct crossing this boundary by value has an ABI of its own that neither
// side states.
//-----------------------------------------------------------------------------
static bool HierarchyConvert( IManagedHierarchy *pApi, const CStandaloneEntity *pChild,
                              Vector *pOrigin, QAngle *pAngles, bool bToWorld )
{
	if ( !pApi || !IsCarried( pChild ) )
		return false;

	float flOrigin[3];
	float flAngles[3];

	if ( pOrigin )
	{
		flOrigin[0] = pOrigin->x;
		flOrigin[1] = pOrigin->y;
		flOrigin[2] = pOrigin->z;
	}

	if ( pAngles )
	{
		flAngles[0] = pAngles->x;
		flAngles[1] = pAngles->y;
		flAngles[2] = pAngles->z;
	}

	const uint32_t hChild = HandleOf( pChild );
	float *pOriginOut = pOrigin ? flOrigin : NULL;
	float *pAnglesOut = pAngles ? flAngles : NULL;

	if ( !( bToWorld ? pApi->HierarchyToWorld( hChild, pOriginOut, pAnglesOut )
	                 : pApi->HierarchyToLocal( hChild, pOriginOut, pAnglesOut ) ) )
		return false;

	if ( pOrigin )
		pOrigin->Init( flOrigin[0], flOrigin[1], flOrigin[2] );

	if ( pAngles )
		pAngles->Init( flAngles[0], flAngles[1], flAngles[2] );

	return true;
}

bool StandaloneHierarchy_WorldToLocal( const CStandaloneEntity *pChild,
                                       Vector *pOrigin, QAngle *pAngles )
{
	IManagedHierarchy *pApi = HierarchyApi();

	return HierarchyConvert( pApi, pChild, pOrigin, pAngles, false );
}

//-----------------------------------------------------------------------------
// A NEW OFFSET, recorded and applied.
//
// NOT GUARDED ON ParentHandle, and it is the one relay that is not. An entity
// that turns out to have no parent is placed absolutely rather than refused: a
// movement started while parented can outlive the parent, and the entity should
// finish it where it stands instead of stopping. That is a decision, so managed
// code makes it — see Hierarchy.SetLocal — and the guard would take the call
// away before it could.
//-----------------------------------------------------------------------------
void StandaloneHierarchy_SetLocal( CStandaloneEntity *pChild, const Vector &vecOrigin,
                                   const QAngle &angles )
{
	if ( !pChild )
		return;

	IManagedHierarchy *pApi = HierarchyApi();

	if ( !pApi )
		return;

	const float flOrigin[3] = { vecOrigin.x, vecOrigin.y, vecOrigin.z };
	const float flAngles[3] = { angles.x, angles.y, angles.z };

	pApi->HierarchySetLocal( HandleOf( pChild ), flOrigin, flAngles );
}

//=============================================================================//
// Linking and unlinking.
//=============================================================================//

// StandaloneHierarchy_ClearParent and StandaloneHierarchy_SetParent went
// (plan_server_native_min P1): the debug cs_standalone_parent was their sole
// referrer and went with them; the managed hierarchy is the only writer now.

void StandaloneHierarchy_SetParentName( CStandaloneEntity *pChild, const char *pParentName )
{
	IManagedHierarchy *pApi = HierarchyApi();

	if ( pChild && pApi )
		pApi->HierarchySetParentName( HandleOf( pChild ), pParentName ? pParentName : "" );
}

void StandaloneHierarchy_SetParentAttachmentName( CStandaloneEntity *pChild,
                                                  const char *pAttachmentName )
{
	IManagedHierarchy *pApi = HierarchyApi();

	if ( pChild && pApi )
	{
		pApi->HierarchySetParentAttachmentName( HandleOf( pChild ),
		                                        pAttachmentName ? pAttachmentName : "" );
	}
}

//-----------------------------------------------------------------------------
// Once per map load, from mapentities.cpp, after every entity has spawned. A
// third pass, and it has to be a third one: the names exist from parse time, but
// the OFFSET a child keeps is measured from where the two of them ended up.
//-----------------------------------------------------------------------------
void StandaloneHierarchy_ResolveParentNames()
{
	IManagedHierarchy *pApi = HierarchyApi();

	if ( pApi )
		pApi->HierarchyResolveParentNames();
}

//=============================================================================//
// Teardown.
//=============================================================================//

//=============================================================================//
// WHAT MANAGED PARENTING ASKS BACK, and it is four questions.
//
// The other direction: managed→native, through SourceSharpEngineApi. Not one of
// them decides anything — each is a read of engine or SDK state that managed
// code has no other route to. Everything else IHierarchyWorld asks was already
// on that table.
//=============================================================================//

//-----------------------------------------------------------------------------
// WHICH HIERARCHY HOLDS THIS ENTITY, AND WHETHER IT CAN POSE.
//
// One call and three bits, because SourceSharp_ParentFrameKind takes all three
// together: three slots would be three crossings to compute one enum, and a
// caller that asked two of them a frame apart could be told the entity is on
// both hierarchies, which no entity ever is.
//
// The partition is exact and it is the SAME discriminator the entity API and the
// trace filters use — SourceSharp_ResolveEntity, whose two arms are answered by
// virtuals — so a recycled slot answers "nobody" rather than being reinterpreted
// as the wrong kind.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityParentProbe( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( !ent.IsValid() )
		return 0;

	int32_t nBits = ent.pStandalone ? SOURCESHARP_PARENT_PROBE_STANDALONE
	                                : SOURCESHARP_PARENT_PROBE_BASE;

	// A standalone entity can always be ASKED for an attachment: its shell
	// answers 0 attachments for a model that has none, which the frame rule
	// does not need to know. No other entity on the server has a skeleton.
	if ( ent.pStandalone != NULL )
		nBits |= SOURCESHARP_PARENT_PROBE_CAN_POSE;

	return nBits;
}

//-----------------------------------------------------------------------------
// WHERE A ONE-BASED ATTACHMENT IS, on an entity of either hierarchy.
//
// The one thing managed parenting cannot derive. Both arms are somebody else's
// implementation on purpose: this hierarchy's is CStandaloneEntity::GetAttachment
// at the top of this file, and the SDK's is CBaseAnimating::GetAttachment, which
// has answered it since the SDK shipped.
//
// Writes the entity's own place on failure, which is what both of those do — but
// it also RETURNS 0, and managed parenting reads that: a child hanging from an
// attachment the model does not carry is left where it is rather than composed
// against the fallback silently.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityGetAttachmentFrame( uint32_t hEntity, int32_t nAttachment,
                                              float *pOutOriginXYZ, float *pOutAnglesPYR )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	Vector vecOrigin( 0, 0, 0 );
	QAngle angAngles( 0, 0, 0 );

	// A STANDALONE ENTITY'S ATTACHMENTS ARE NOT ANSWERED HERE ANY MORE: its
	// skeleton is managed (ServerSkeletons, plan physics_bones_native_zero
	// 1.2), and EngineEntities.GetAttachmentFrame answers every standalone
	// handle before this slot is asked. What reaches this body is an entity
	// with no skeleton, whose answer is its own transform and "not found";
	// a standalone handle that reached it anyway gets the same, which is the
	// answer for a model that carries no such attachment.
	(void)nAttachment;

	// SourceSharpEntity_t::Animating() RETURNS NULL ARMED, unconditionally and
	// by its own definition (shell_standalone_base.cpp:511-521: "armed there is
	// no SDK entity to have one"). So this branch is dead armed and its
	// EntityToWorldTransform() -- a CBaseEntity inline -- has nothing to be
	// called on. The `ent.IsValid()` branch below is what an armed entity
	// without a shell attachment reaches, and it answers the same question
	// through SourceSharpEntity_t::EntityToWorld(), which is the projection
	// written for exactly this.
	if ( ent.IsValid() )
	{
		MatrixAngles( ent.EntityToWorld(), angAngles, vecOrigin );
	}

	if ( pOutOriginXYZ )
	{
		pOutOriginXYZ[0] = vecOrigin.x;
		pOutOriginXYZ[1] = vecOrigin.y;
		pOutOriginXYZ[2] = vecOrigin.z;
	}

	if ( pOutAnglesPYR )
	{
		pOutAnglesPYR[0] = angAngles.x;
		pOutAnglesPYR[1] = angAngles.y;
		pOutAnglesPYR[2] = angAngles.z;
	}

	return 0;
}

//-----------------------------------------------------------------------------
// An attachment NAME to a one-based index, on either hierarchy. Zero for a name
// the model does not carry — the engine's own spelling, and the same answer both
// sides' LookupAttachment gives.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityLookupAttachmentAny( uint32_t hEntity, const char *pAttachmentName )
{
	// NOTHING NATIVE ANSWERS THIS NOW. The standalone arm's skeleton is managed
	// (ServerSkeletons, plan physics_bones_native_zero 1.2) and
	// EngineEntities.LookupAttachmentAny answers the whole of it there; the
	// SDK arm was dead armed by definition (Animating() returns NULL when the
	// alias is armed). The slot stays until IShimHierarchy is renumbered.
	(void)hEntity;
	(void)pAttachmentName;
	return 0;
}

//-----------------------------------------------------------------------------
// THE ENGINE BLOCK, WITH ITS OWN LENGTH.
//
// SourceSharp_EntityGetDataBlob answers the NETWORKED payload, whose length is
// the entity's tier -- eight words for a class that declared no state, which is
// most of a map. The block is the fixed prefix of the same allocation and is
// always the full sizeof( EngineBlock_t ): operator new floors every allocation
// at SourceSharp_StandaloneMinBlobBytes() exactly so.
//
// SO THE TWO OVERLAP AND DISAGREE ABOUT HOW LONG THEY ARE, and managed code
// reading the block through the other one's length sees thirty-two of its three
// hundred and fifty-two bytes. Every read past that is refused, silently,
// because a short span is a legal span. Parenting met it as "every child in
// dm_lockdown stands still": the composition asked where the parent was, the
// read was refused, and the frame could not be built.
//
// NULL for anything not on this hierarchy -- including worldspawn, which does
// carry a blob and does NOT carry one of these.
//-----------------------------------------------------------------------------
void *SourceSharp_EntityGetEngineBlock( uint32_t hEntity, int32_t *pOutSize )
{
	if ( pOutSize )
		*pOutSize = 0;

	CStandaloneEntity *pEntity = SourceSharp_ResolveStandalone( hEntity );

	if ( !pEntity )
		return NULL;

	if ( pOutSize )
		*pOutSize = (int32_t)sizeof( EngineBlock_t );

	return &pEntity->Block();
}

//-----------------------------------------------------------------------------
// A PARENTED CHILD STOPS BEING SIMULATED, and this is what stops it.
//
// A DIVERGENCE FROM STOCK RATHER THAN AN IMPLEMENTATION DETAIL. Stock hands a
// simulated child to a SHADOW controller: the solver keeps the object and is
// told to follow the parent's transform each tick, so the child still collides
// and still pushes things. There is no shadow support on this hierarchy, and
// leaving the object alone is strictly WORSE than destroying it -- PropDynamic
// calls MakePhysicsStatic, and a static object cannot be moved by writing the
// entity's origin. Both live children in the shipped map are exactly that, so
// without this the drawn model follows its parent while an invisible collision
// hull stays at the map position for the rest of the round, stopping bullets
// and players in mid-air.
//
// SO A PARENTED CHILD BECOMES A THING THAT FOLLOWS. The decision is managed
// code's -- Hierarchy.SetParent calls it at the moment a link is made, and
// only then -- and this is the hand that carries it out, because a solver
// object is native and nothing else about it crosses.
//
// THE OBJECT IS NOT GIVEN BACK ON UNPARENTING, and that asymmetry is deliberate
// rather than forgotten: the object destroyed here was built from the model's
// compiled collision data by whoever asked for it, and only they know whether
// it should be static, asleep or debris. Re-creating one would be this file
// guessing at a decision that belongs to the entity.
//-----------------------------------------------------------------------------
void SourceSharp_EntityDestroyPhysics( uint32_t hEntity )
{
	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
		pStandalone->DestroyPhysics();
}

//=============================================================================//
// THE ENTITY API'S OWN PARENTING, relayed.
//
// A HANDLE crosses and a pointer never does. That is not a style choice: the
// registry is keyed by handles and every end is revalidated on every walk,
// precisely so that no raw parent pointer exists to be held past its owner.
//=============================================================================//

int32_t SourceSharp_EntitySetParent( uint32_t hChild, uint32_t hParent,
                                     int32_t nParentAttachment )
{
	CStandaloneEntity *pChild = SourceSharp_ResolveStandalone( hChild );

	// A CBaseEntity child is the case this port refuses, and it is refused HERE
	// rather than silently: C# holds one handle type for both hierarchies, so a
	// caller cannot tell which kind it has, and a call that did nothing and said
	// nothing would read as parenting that does not work.
	if ( !pChild )
	{
		const SourceSharpEntity_t child = SourceSharp_ResolveEntity( hChild );

		if ( child.pBase )
		{
			Warning( "[SourceSharp] cannot parent '%s': it is a CBaseEntity, and an SDK "
			         "child of a managed parent is not supported — see the header of "
			         "shell_standalone_hierarchy.cpp\n",
			         child.Classname() );
		}

		return 0;
	}

	// INVALID_EHANDLE_INDEX and NOT zero, which is the whole of C#'s
	// EntityHandle.None — zero is a real handle naming a real entity, and
	// treating it as "no parent" would silently unparent anything asked to
	// follow the world. Passed through unchanged: managed code reads the same
	// constant as "unparent", which is the one place the two spellings must
	// agree and now the only place they meet.
	IManagedHierarchy *pApi = HierarchyApi();

	if ( !pApi )
		return 0;

	// A parent that does not resolve is a REFUSAL, not an unparenting, and
	// managed code says so. Silently dropping the link would turn "the entity
	// you named has been removed" into "you asked for no parent", and the child
	// would stand still with nothing anywhere saying why.
	return pApi->HierarchySetParent( hChild, hParent, nParentAttachment );
}

//-----------------------------------------------------------------------------
// WHO CARRIES THIS ENTITY, from the block. No crossing, for
// StandaloneHierarchy_IsParented's reason — this is the same word and the same
// revalidation, answering with the handle instead of with a bool.
//-----------------------------------------------------------------------------
uint32_t SourceSharp_EntityGetParent( uint32_t hChild )
{
	CStandaloneEntity *pChild = SourceSharp_ResolveStandalone( hChild );

	if ( !IsCarried( pChild ) )
		return INVALID_EHANDLE_INDEX;

	const uint32_t hParent = pChild->Block().Head.ParentHandle;

	// Resolved before it is answered, so a handle whose entity has gone comes
	// back as nothing rather than as a number C# would ask about and be told
	// about a stranger.
	return SourceSharp_ResolveEntity( hParent ).IsValid() ? hParent
	                                                      : (uint32_t)INVALID_EHANDLE_INDEX;
}

//-----------------------------------------------------------------------------
// WHICH FRAME on the parent, which the block does NOT carry.
//
// Asked of managed code rather than given a third guard word, and the reason is
// what the word would have to be worth: an attachment index is read by nothing
// on the hot path — not by a position write, not by a removal, not by the frame
// conversions — so a word for it would sit in six hundred entities to save a
// crossing that no frame makes. Nothing in the shipped content calls this at
// all; the entity API declares it because "parented to the origin" and "not
// parented" are different answers and SourceSharp_EntityGetParent above can only
// carry the second.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityGetParentAttachment( uint32_t hChild )
{
	IManagedHierarchy *pApi = HierarchyApi();

	if ( !pApi )
		return 0;

	return pApi->HierarchyGetParentAttachment( hChild );
}

void SourceSharp_EntitySetLocalTransform( uint32_t hChild, const float *pOrigin,
                                          const float *pAngles )
{
	CStandaloneEntity *pChild = SourceSharp_ResolveStandalone( hChild );

	if ( !pChild )
		return;

	// Each half defaults to what the child already has, so C# can move a child
	// along its parent without having to restate the facing it is not changing.
	Vector vecLocal = pChild->GetAbsOrigin();
	QAngle angLocal = pChild->GetAbsAngles();

	StandaloneHierarchy_WorldToLocal( pChild, &vecLocal, &angLocal );

	if ( pOrigin )
		vecLocal.Init( pOrigin[0], pOrigin[1], pOrigin[2] );

	if ( pAngles )
		angLocal.Init( pAngles[0], pAngles[1], pAngles[2] );

	StandaloneHierarchy_SetLocal( pChild, vecLocal, angLocal );
}

void SourceSharp_EntityGetLocalTransform( uint32_t hChild, float *pOutOrigin, float *pOutAngles )
{
	CStandaloneEntity *pChild = SourceSharp_ResolveStandalone( hChild );

	if ( !pChild )
		return;

	// Seeded with the ABSOLUTES and overwritten only if there is a parent to
	// measure against. An unparented entity's local frame IS its world frame,
	// which is the answer that lets C# read a local origin without first asking
	// whether anything is carrying the entity.
	Vector vecLocal = pChild->GetAbsOrigin();
	QAngle angLocal = pChild->GetAbsAngles();

	StandaloneHierarchy_WorldToLocal( pChild, &vecLocal, &angLocal );

	if ( pOutOrigin )
	{
		pOutOrigin[0] = vecLocal.x;
		pOutOrigin[1] = vecLocal.y;
		pOutOrigin[2] = vecLocal.z;
	}

	if ( pOutAngles )
	{
		pOutAngles[0] = angLocal.x;
		pOutAngles[1] = angLocal.y;
		pOutAngles[2] = angLocal.z;
	}
}

//=============================================================================//
// Looking at it, and where the two ways of doing that went.
//
// Parenting's failures are all VISUAL -- a child in the wrong place, a child
// that stopped following, a child following the wrong frame -- and none of the
// three prints anything, so both console doors exist and neither is native any
// more.
//
// cs_sv_hierarchy, which printed the registry, went managed when the registry
// did (SourceSharp.Server/HierarchyCommands.cs). It reads the same links the
// composition reads rather than a native copy of them, which is the difference
// between a reading and a second opinion.
//
// cs_sv_setlocal went with it (lane 3c-entityshell). It stayed native on the
// argument that it was "a door into the ABI rather than a report of state: it
// drives SourceSharp_EntitySetLocalTransform, so what it exercises is the call
// C# makes" -- which was right while C# had no way to reach that slot from a
// command of its own, and stopped being right when ScriptedEntity.LocalOrigin
// and IEngineEntities.SetLocalTransform arrived. HierarchySetLocalCommand types
// into the same crossing from the side the caller is on, asks the directory for
// the child instead of sweeping every standalone entity, and refuses a
// coordinate that is not a number instead of reading it as zero the way atof
// did here.
//
// IT HAD NO DRIVER ANYWHERE when it moved: 0 references in tools/, docs/, cfg/,
// the managed tree and the gtests (tools/reached-by-name). That matters because a
// console command is reached BY NAME and no link-line census sees one --
// cs_standalone_spawn in shell_standalone_debug.cpp looks equally dead by every
// symbol measure and is driven by eight tools including tools/live-gate.
//=============================================================================//

//=============================================================================//
// THE SKELETON PROBE, cs_sv_boneprobe, is managed now (SkeletonCommands.cs,
// debug builds only): its `seq` and `scale` arms drive ServerSkeletons, and
// its record/replay arms -- an oracle for the native bone_library.cpp the
// server no longer links -- went with the file they checked.
//=============================================================================//

//-----------------------------------------------------------------------------
// A RETIRED SKELETON BODY WAS REACHED. Said out loud by the one native bone
// body the server still carries with nothing behind it (EntitySetSequence, an
// IShimModelsServer slot: EngineEntityRender asks ServerSkeletons instead).
// The shared IShimModels bone bodies that used it left with their rows when
// the client's skeleton followed (plan physics_bones_native_zero 1.2).
//-----------------------------------------------------------------------------
void StandaloneBones_Unreached( const char *pszSlot )
{
	Warning( "[SourceSharp] %s reached the server's native body; the server's "
	         "skeleton is managed (ServerSkeletons) and answers this itself\n", pszSlot );
}

