//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the op relays: how a call reaches C# ================//
// SS_REASON a  a vtable the engine calls through
//
// ONE OF THE TWO HALVES `shell_standalone_scripted.cpp` split into for
// plan_entity.md's Phase 1 exit. This is the relay half: take what the shell
// was handed, say it in the op vocabulary both sides share, and hand back the
// answer. The other half is `shell_standalone_class.cpp` — which classnames
// exist, and the factory that makes them — a different job that shared a file
// with this one only by history.
//
// EVERY RELAY OPENS WITH THE OVERRIDE BIT, and that is step 1f's change
// (plan_entity.md §3.4). These bodies used to call thirteen ABI slots
// unconditionally: a think crossed into managed code whether or not the C#
// class had an OnThink, a touch crossed sixty-six times a second whether or not
// it had an OnTouch. Now the first thing each one does is test one bit of the
// class descriptor it is already holding, and a class that does not override
// the op costs ZERO crossings for it. That is the mechanism plan_entity.md §5's
// crossing budget is paid back with, and it is why there is an op NUMBER at all
// -- names or hashes cannot be a bit position.
//
// NO HEADER OF ITS OWN, deliberately. Every function defined here is already
// declared somewhere a caller reaches for another reason: the `OnManaged*`
// relays by `shell_standalone.h`, and the ABI entry points by the game system
// that assigns them into `engineApi`. (StandaloneFilterAnswer, declared by
// entityfilter.h, went in the server gc cull, and the header after it.) A header
// here would be a second declaration of things that already have one.
//
//=============================================================================//

#include "shell_standalone_class.h"
#include "shell_standalone_player.h"
#include "entity_op.h"
#include "slot_table.h"
#include "generic_nettable.h"
#include "standalone_relays.h"
#include "ss_variant.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "sourcesharp.h"

#include "imanaged_areas.h"   // plan_engineapi_vtables E9: IManagedEntities
#include "ss_takedamageinfo.h"	// server.so's damage info
uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity );


//=============================================================================//
// The one crossing.
//=============================================================================//

//-----------------------------------------------------------------------------
// An argument block with nothing in it.
//
// ZEROED IS NOT EMPTY, which is the one trap in this struct. hOther zeroed is
// entity index 0 with serial 0 -- a handle that RESOLVES, to the world -- so a
// relay that forgot to set it would report every touch as a touch by the world
// rather than by nobody. "No entity" has one spelling in this API and it is
// INVALID_EHANDLE_INDEX, so that is what an empty args block carries.
//-----------------------------------------------------------------------------
static SourceSharpOpArgs EmptyOpArgs()
{
	SourceSharpOpArgs args;

	memset( &args, 0, sizeof( args ) );
	args.hOther = (uint32_t)INVALID_EHANDLE_INDEX;

	return args;
}

//-----------------------------------------------------------------------------
// The crossing itself. The blob is attached here rather than by each caller,
// because it is the same two lines every time and because the shell has it in
// hand -- it IS the entity's own allocation, so handing it over costs nothing
// and saves the managed side a round trip for what the caller was holding.
//-----------------------------------------------------------------------------
static int DispatchOp( CStandaloneEntity *pEntity, int nOp, SourceSharpOpArgs &args )
{
	IManagedEntities *pApi = ManagedEntities();   // plan_engineapi_vtables E9

	if ( !pApi )
		return 0;

	args.pBlob      = pEntity->DataBlob();
	args.nBlobBytes = pEntity->BlobBytes();

	return pApi->Dispatch( pEntity->ManagedHandle(), nOp, &args );
}

//=============================================================================//
// The relays.
//=============================================================================//

//-----------------------------------------------------------------------------
// The classname travels with SPAWN and with KEYVALUE, because those are the two
// calls that arrive BEFORE managed code has an instance and it is the classname
// that builds one. Asking the entity for its class instead would be a native
// round trip per key of a map.
//
// Neither can be switched off by the bit. See ENTITY_OP_ALWAYS_MASK.
//-----------------------------------------------------------------------------
void CStandaloneEntity::OnManagedSpawn()
{
	if ( !ClassOverridesOp( ENTITY_OP_SPAWN ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.pText0 = GetClassname();

	DispatchOp( this, ENTITY_OP_SPAWN, args );
}

bool CStandaloneEntity::OnManagedKeyValue( const char *szKeyName, const char *szValue )
{
	if ( !ClassOverridesOp( ENTITY_OP_KEY_VALUE ) )
		return false;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.pText0 = GetClassname();
	args.pText1 = szKeyName;
	args.pText2 = szValue;

	return DispatchOp( this, ENTITY_OP_KEY_VALUE, args ) != 0;
}

//-----------------------------------------------------------------------------
// An input, forwarded with the value as both text and a number.
//
// Converted rather than merely read. A map sends "3" as a STRING — ent_fire and
// most output connections do — and variant_t::Float() returns zero for a string
// rather than parsing it. Reading it directly meant every numeric input arrived
// as nothing: a counter told to add three added zero, and said so to nobody.
//-----------------------------------------------------------------------------
// ONE BODY FOR BOTH KINDS OF ENTITY -- plan_entity.md step 8e-entfire.
//
// pShell is the shell when there is one and NULL for a shell-less entity, whose
// handle is all there is: no C++ object, so no blob and no override bit to
// test. Everything else -- the value, its number, its type, the activator
// handle and the op number -- is identical, and that identity is the point. An input to a
// shell-less entity used to be undeliverable at all; the fix must not deliver
// it down a SECOND path, because two input paths drift and the drift is
// invisible from a console.
static bool DispatchInput( CStandaloneEntity *pShell, uint32_t hEntity,
                           const char *szInputName, const Activator_t &activator,
                           const ss::variant_t &Value )
{
	// THE STRING FORM IS TAKEN WITHOUT CONVERTING FIRST, and that is the
	// removal rather than a shortening -- plan_entity.md's variant_t::Convert
	// subsystem. `variant_t text = Value; text.Convert( FIELD_STRING );
	// text.String()` stood here, and the Convert changed the TYPE and never the
	// CHARACTERS:
	//
	//   String() is `(fieldType == FIELD_STRING) ? STRING(iszVal) : ToString()`
	//   -- variant_t.h:47, inline -- so every type that Convert REFUSES already
	//   reached ToString by itself. And the one type it accepts, FIELD_EHANDLE,
	//   ToString spells the same way the conversion assigns: the entity's
	//   targetname, or "<<null entity>>". So the two agree on all ten types,
	//   which VariantToStringTests asserts over every one of them
	//   (ConvertingToAStringFirstNeverChangesTheCharacters).
	//
	// What the call did cost was a MAKE_STRING per ehandle-valued input -- a
	// permanent string-pool allocation for a name that is copied into managed
	// memory microseconds later and never looked at again.
	//
	// The one real difference: for a non-string variant the pointer now comes
	// from ToString's `static char szBuf[512]` rather than the string pool.
	// That is safe HERE and only here, because the very next thing that happens
	// to it is Entry.cs DecodeArgs' FromUtf8, which copies it before any game
	// code runs (Entry.cs:3612 evaluates DecodeArgs as Dispatch's argument). It
	// also truncates a targetname at 511 characters, where the pooled string
	// would not; no map has one.
	//
	// THE FIELD TYPE TRAVELS TOO, AND NOTHING READS IT YET. See below.
	//
	// Convert still works on a copy for the NUMBER, because that one mutates.
	ss::variant_t number = Value;
	const int32_t nFieldType = (int32_t)number.FieldType();
	const float flValue = number.Convert( FIELD_FLOAT ) ? number.Float() : 0.0f;

	// The activator crosses as a HANDLE, and that is why widening it needed no
	// ABI slot: the managed side numbers both hierarchies the same way, so the
	// handle for an entity outside the SDK is one C# could already resolve.
	// What was missing was never the wire format, it was a native argument able
	// to hold the other kind — the same thing the touch relays said about a
	// toucher, and it was true twice for the same reason.
	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther   = activator.pBase
		? SourceSharp_HandleFromEntity( activator.pBase )
		: SourceSharp_HandleFromAnyEntity( activator.pGameHandle );
	args.pText1   = szInputName;
	args.pText2   = Value.String();
	args.flFloat0 = flValue;

	// THE VARIANT'S OWN TYPE, PLUS ONE. ARMED, AND NOTHING READS IT YET.
	//
	// flFloat0 above is still derived by the NATIVE Convert, and that is the
	// last piece of variant_t::Convert on this path. It cannot move until the
	// managed side can see what the value actually IS: the number a string
	// yields is atof, the number a boolean yields is ZERO (stock has no
	// FIELD_BOOLEAN arm at all), and the number a colour yields is zero as well
	// -- while atof of a colour's own spelling, "255 128 64 32", is 255. So
	// deriving the number from the text alone is a wrong answer for two of the
	// nine types, silently, which is exactly the failure this subsystem exists
	// to avoid.
	//
	// With the type here, VariantValue.FromCrossing does the whole table in
	// managed code and the Convert above goes. The remaining step is ONE LINE in
	// a file this lane does not own -- Entry.cs DecodeArgs' EntityOp.Input case
	// gaining `Flags = args->Int0` -- and it was filed as a request to the
	// integrator.
	//
	// PLUS ONE so that zero means "the shim did not say" rather than
	// FIELD_VOID, which is a real type and a plausible-looking answer. A
	// one-sided slot that answers a plausible zero has cost this port two lanes
	// already.
	args.nInt0    = nFieldType + 1;

	if ( pShell )
		return DispatchOp( pShell, ENTITY_OP_INPUT, args ) != 0;

	IManagedEntities *pApi = ManagedEntities();   // plan_engineapi_vtables E9

	if ( !pApi )
		return false;

	// NO BLOB, and that is not a degraded call. The blob exists so a shell's
	// getters can be a load at a constant offset on a path the engine takes;
	// nothing takes that path for an entity the engine cannot see, so a
	// shell-less entity keeps its state as ordinary managed fields and the
	// managed Resolve is already written to take an empty span (ScriptedEntity
	// Registry.Resolve, ScriptedEntity.cs).
	args.pBlob      = NULL;
	args.nBlobBytes = 0;

	return pApi->Dispatch( hEntity, ENTITY_OP_INPUT, &args ) != 0;
}

bool CStandaloneEntity::OnManagedInput( const char *szInputName, const Activator_t &activator,
                                        const ss::variant_t &Value )
{
	if ( !ClassOverridesOp( ENTITY_OP_INPUT ) )
		return false;

	return DispatchInput( this, 0, szInputName, activator, Value );
}

//-----------------------------------------------------------------------------
// ReportTouch, OnManagedStartTouch, OnManagedTouch AND OnManagedEndTouch WERE
// HERE, AND ARE DELETED -- plan_entity.md step 2b.
//
// Together they were the touch half of this file: one inbound crossing per
// touching pair per frame, dispatched from the link table the shell kept.
// plan_entity.md 3.8 replaces the whole mechanism with ONE crossing a frame --
// native hands managed the overlapping pairs, managed diffs them against its
// own link table and raises the three edges on the instance directly.
//
// SO THERE IS NOTHING LEFT TO RELAY. The edges are raised where the entity
// already is, inside the GameFrame crossing, and a call from here would be the
// crossing the step exists to remove.
//
// The two sentences these bodies carried are not lost, only moved:
//
//   "EVERY toucher is relayed" -- who a touch is delivered to is a rule about
//   the game and lives in ScriptedEntityRegistry.Touch, where TouchedByAnything
//   is overridden. Unchanged by this step.
//
//   "Which of the two a class gets is its kind: an item is told every tick, a
//   brush on the edges only" -- that WAS StandaloneClassDesc_t::bTouchEveryTick,
//   decided in C++ for every entity in the mod. It is
//   ScriptedEntity.TouchEveryTick now, a per-class managed choice, which is
//   what step 2b's paragraph asks for by name.
//
// ENTITY_OP_START_TOUCH, ENTITY_OP_TOUCH and ENTITY_OP_END_TOUCH stay in the
// op enum and stay in the override-bit table: they are still what a class
// OVERRIDES, which is how managed code decides whether to raise them.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// It ran into something, which for a projectile is the only event that matters.
//
// Reported as its own op but through the same managed call a touch uses,
// because from the C# side they are the same question — "you hit this" — and
// the toucher may legitimately be nothing at all. A projectile that struck
// nothing named hands over an invalid handle, which is what "no entity" looks
// like everywhere else in this API.
//
// No players-only filter: the class that wants this says TouchedByAnything, and
// a rocket that only exploded on people would be a strange rocket.
//
// HandleFromAnyEntity, exactly as OnManagedBlocked does it (OnManagedEndTouch
// did too, until step 2b deleted it):
// the narrow spelling took a CBaseEntity *, so what a projectile hit on THIS
// hierarchy — which is most of the solid things in this mod — crossed as the
// invalid handle and the managed side heard nothing.
//-----------------------------------------------------------------------------
void CStandaloneEntity::OnManagedImpact( IHandleEntity *pOther )
{
	if ( !ClassOverridesOp( ENTITY_OP_IMPACT ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther = SourceSharp_HandleFromAnyEntity( pOther );

	DispatchOp( this, ENTITY_OP_IMPACT, args );
}

//-----------------------------------------------------------------------------
// It arrived, or it met something it could not move through.
//-----------------------------------------------------------------------------
void CStandaloneEntity::OnManagedMoveDone()
{
	if ( !ClassOverridesOp( ENTITY_OP_MOVE_DONE ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();

	DispatchOp( this, ENTITY_OP_MOVE_DONE, args );
}

// NO PHYSICS-CREATED RELAY (2026-10-12): the object is made by managed's
// EntityPhysicsCreation, which tells the managed class itself.

// The blocker crosses as a HANDLE, which never cared which hierarchy its owner
// lived on — so taking an IHandleEntity here rather than a CBaseEntity costs
// one call name and buys the half of the world this could not previously name.
void CStandaloneEntity::OnManagedBlocked( IHandleEntity *pOther )
{
	if ( !ClassOverridesOp( ENTITY_OP_BLOCKED ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther = SourceSharp_HandleFromAnyEntity( pOther );

	DispatchOp( this, ENTITY_OP_BLOCKED, args );
}

//-----------------------------------------------------------------------------
// THE REMOVAL PUSH IS NOT MADE FROM HERE ANY MORE -- plan_entity.md step 2c.
//
// The relay that stood here -- the base's removal notification -- was called
// by ~CStandaloneEntity:
// one inbound crossing per entity removed, and a level teardown was six
// hundred of them. What it carried is policy -- tell the managed instance it is
// going, and drop the registry's row -- and §3.5 puts it in the directory's
// removal pass, which runs in the same position in the frame and costs no
// crossing at all because it is already inside the frame's own.
//
// ENTITY_OP_REMOVED IS STILL DECLARED (entity_op.h) and is still in
// ENTITY_OP_ALWAYS_MASK. Renumbering the op enum to close a gap would renumber
// every op after it, on both sides, for no behaviour. (The four dead
// OnNetEntity* slots once carried the same argument; they were REAPED at ABI
// 240 once a live run showed nothing reached them, which is the bar this gap
// would have to clear too.)
//
// AND THIS OP DOES NOT CLEAR THAT BAR, WHICH IS THE POINT OF SAYING SO HERE.
// It read "it has no sender on this side" until lane console-warnings gave it
// one: the ONE sender below is reached on every client disconnect, and a live
// run measured what its absence cost. Reaping ENTITY_OP_REMOVED on the strength
// of the sentence above would delete that fix.
//
// The receiver stays for its own reason as well -- the client half of the port
// has not reached this step.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE ONE PATH THE DIRECTORY'S REMOVAL PASS CANNOT REACH: a shell that NATIVE
// decides to free.
//
// 2c's replacement for the destructor relay is EntityDirectory's free pass, and
// it works for every removal MANAGED starts -- it tells the entity, writes the
// liveness record, unbinds the row, and only then asks for the shell. It cannot
// work for a removal native starts, because managed code is not in that call at
// all. There is exactly one such path in this build and it is
// CServerGameClients::ClientDisconnect, which does `delete pStandalone`.
//
// MEASURED, AND THE SYMPTOM WAS FOUR LAYERS AWAY FROM THE CAUSE. Two live runs
// on dm_lockdown, after a second client joined and left:
//
//   reload: ss_player '' (281477122) could not be rebuilt (PlayerNetState needs
//     692 byte(s) and entity 2 ('ss_player') offers 0)
//   reload: 480 of 481 entit(ies) restored; 1 FAILED to rebuild, first
//     'ss_player'
//
// The departed player's managed instance was still in the registry, so the hot
// reload carried a record for it; the entity behind it was long gone, so the
// bind was refused -- correctly -- and the record could not be replayed. The
// same orphan is why LivenessMirror went on answering "alive" about a freed
// entity, which is the direction that record must never fail in, and why
// PlayerCommandQueue.Forget was never reached for a leaving player.
//
// THIS IS NOT 2c COMING BACK. 2c's number was one crossing per entity removed
// and six hundred at a level teardown; this is one crossing per CLIENT
// DISCONNECT, of which a listen server sees a handful in a map's life. The
// destructor relay stays deleted and every map entity still leaves through the
// directory's pass.
//
// BEFORE the delete, not after: the announcement carries the entity's handle
// and its blob, and both belong to memory this call is about to free.
//-----------------------------------------------------------------------------
void SourceSharp_AnnounceStandaloneRemoved( CStandaloneEntity *pEntity )
{
	if ( !pEntity )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();

	DispatchOp( pEntity, ENTITY_OP_REMOVED, args );
}


//-----------------------------------------------------------------------------
// Damage, forwarded whole rather than reduced to a number: which weapon, from
// whom, and of what type all matter to a breakable that yields to a crowbar and
// shrugs off bullets. The shell applies the answer to health; deciding how much
// lands is managed code's.
//-----------------------------------------------------------------------------
int CStandaloneEntity::OnManagedDamage( const ss::CTakeDamageInfo &info )
{
	if ( !ClassOverridesOp( ENTITY_OP_DAMAGE ) )
		return 0;

	// Where it landed and which way it was going. Both are zero for damage with
	// no place — a trigger_hurt hurts all of you at once — and a class that needs
	// neither simply does not look.
	const Vector &vecPos   = info.GetDamagePosition();
	const Vector &vecForce = info.GetDamageForce();

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther   = SourceSharp_HandleFromEntity( info.GetAttacker() );
	args.flFloat0 = info.GetDamage();
	args.nInt0    = info.GetDamageType();
	args.pVec0    = vecPos.Base();
	args.pVec1    = vecForce.Base();

	return DispatchOp( this, ENTITY_OP_DAMAGE, args );
}

void CStandaloneEntity::OnManagedKilled( IGameEntity *pAttacker )
{
	if ( !ClassOverridesOp( ENTITY_OP_KILLED ) )
		return;

	SourceSharpOpArgs args = EmptyOpArgs();
	args.hOther = SourceSharp_HandleFromEntity( pAttacker );

	DispatchOp( this, ENTITY_OP_KILLED, args );
}

//=============================================================================//
// The outbound half: managed telling native.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE FAR SIDE OF THE EntityDispatch SLOT (plan_entity.md step 1b,
// section 2 of its ABI request).
//
// The same op vocabulary in the other direction. One op uses it today:
// ENTITY_OP_POSITION_CHANGED, which is what managed code says after writing the
// block's Origin and Angles itself. The handler is
// CStandaloneEntity::OnPositionChanged, which is also the tail of
// SetAbsTransform -- so the op path and the native path are one piece of code
// and cannot drift, and the handler is exercised by every move in the game
// rather than only by its own test.
//
// pArgs->pVec0 is the PREVIOUS origin, which the trigger half needs and which
// cannot be recovered once managed has written the new one. NULL is taken as
// "it was where it is", which fires no trigger edges -- the honest answer when
// the caller could not say where it came from.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityDispatch( uint32_t hEntity, int32_t nOp, const SourceSharpOpArgs *pArgs )
{
	CStandaloneEntity *pShell = SourceSharp_ShellFromHandle( CBaseHandle::UnsafeFromIndex( (int)hEntity ) );

	if ( !pShell )
		return 0;

	switch ( nOp )
	{
	case ENTITY_OP_POSITION_CHANGED:
	{
		const Vector vecPrevOrigin = ( pArgs && pArgs->pVec0 )
			? Vector( pArgs->pVec0[0], pArgs->pVec0[1], pArgs->pVec0[2] )
			: pShell->Block().Head.Origin;

		pShell->OnPositionChanged( vecPrevOrigin );
		return 1;
	}

	default:
		// An op this build does not implement, which is what a managed side
		// newer than the native one looks like. Zero rather than an assert: the
		// honest answer to "I do not know that event" is that nothing happened.
		return 0;
	}
}

//-----------------------------------------------------------------------------
// A MANAGED WRITE TO ONE ENGINE-BLOCK FIELD (step 1b's first ABI request).
//
// nBlockOffset is measured INSIDE the block; MarkBlockChanged adds BlockBase(),
// which is the number managed code cannot know. One edict_t::StateChanged(
// offset ) and nothing else -- never FL_FULL_EDICT_CHANGED, which is the whole
// distinction between this slot and EntityMarkStateChanged beside it.
//
// A handle that is not on this hierarchy is a silent no-op, the shape
// SourceSharp_EntityMarkStateChanged already has.
//-----------------------------------------------------------------------------
void SourceSharp_EntityMarkBlockChanged( uint32_t hEntity, int32_t nBlockOffset )
{
	CStandaloneEntity *pShell = SourceSharp_ShellFromHandle( CBaseHandle::UnsafeFromIndex( (int)hEntity ) );

	if ( !pShell )
		return;

	pShell->MarkBlockChanged( nBlockOffset );
}
