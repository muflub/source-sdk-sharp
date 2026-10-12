//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client bone leaf library's gate and counters ====//
//
// plan_entity.md §6, the bone-leaf-library row, and §3.7. Lane `f-bone`.
//
// WHAT THIS IS, AND WHY IT IS NOT ON C_BaseAnimating ANY MORE
//
// Three separate things lived as `C_BaseAnimating` STATICS in
// `game/client/c_baseanimating.cpp`, which the flip deletes:
//
//   * the bone-access gate — a global "may SetupBones run right now" flag with
//     a push/pop stack and a mutex,
//   * the model-bone generation counter — one `unsigned long` that every bone
//     cache compares itself against to decide whether it is stale, and
//   * the bone-setup thread pool's init/shutdown pair and the per-frame sweeps
//     that drive the parallel bone setup and the client-side animation list.
//
// None of the three is a fact about any entity. They are the leaf library's own
// state wearing the entity class's name, which is exactly what §6's LEAVE table
// says about them: they "are §6's bone-leaf-library row wearing the class's name
// and must be re-homed, not deleted with it".
//
// So they are re-homed here, and NOTHING IN THIS HEADER NAMES AN ENTITY TYPE.
// That is the test: §3.7 requires the bone library to be "a leaf library with no
// entity type in their interface", and a `void (*)()` sweep is how the two
// per-frame walks keep that promise while the lists they walk stay with whoever
// owns the entities — stock's `C_BaseAnimating` today, the managed hierarchy
// after the flip, and BOTH during the transition, which is why registration is
// a list rather than a single slot.
//
// WHAT IS **NOT** HERE, AND WHY — the rulings that go with the re-home.
//
//   `LockStudioHdr` / `UnlockStudioHdr` are NOT on this row and are NOT here.
//   §6's carve-out lists `UnlockStudioHdr (:957)` among the "C_BaseAnimating
//   STATICS"; it is not one. Both are PRIVATE INSTANCE methods
//   (c_baseanimating.h:680-681) over `m_pStudioHdr` / `m_hStudioHdr`, which are
//   the ENTITY's model-cache lifetime. They leave with the entity. They are
//   still owed after the flip only because two header inlines — `GetModelPtr()`
//   (:846) and `InvalidateMdlCache()` (:855) — expand their bodies into other
//   translation units, and the fix for each of those is that TU's own port, not
//   a re-home here. See status/f-bone.md for the four owners and their lanes.
//
//===========================================================================//

#ifndef SOURCESHARP_BONE_LEAF_LIBRARY_H
#define SOURCESHARP_BONE_LEAF_LIBRARY_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// THE BONE-ACCESS GATE.
//
// A push/pop stack guarding whether bones and attachments may be read. There is
// exactly ONE gate; `view.cpp`, `viewrender.cpp` and the shim already reach it
// by these names, and `C_BaseAnimating::PushAllowBoneAccess` / `PopBoneAccess`
// are now the forwarders rather than the other way round.
//
// `pTag` is matched between push and pop by an Assert. Stock passes the literal
// `(char const *)1` from the scoped guard to mean "unchecked", and that is kept
// verbatim rather than turned into NULL: NULL is a legal tag that the Assert
// compares by pointer, and 1 is the value it special-cases.
//-----------------------------------------------------------------------------
void SourceSharp_BoneAccessPush( bool bAllowBones, bool bAllowAttachments, const char *pTag );
void SourceSharp_BoneAccessPop( const char *pTag );

// Whether the gate is currently open for this kind of model. The ONLY thing
// that varies between a view model and a normal one is which of the two flags
// is read, so the caller passes the fact rather than the entity.
bool SourceSharp_BoneAccessAllowed( bool bViewModel );

//-----------------------------------------------------------------------------
// The scoped form. Stock spells this `C_BaseAnimating::AutoAllowBoneAccess`;
// this is the same object with the entity class's name taken off it.
//-----------------------------------------------------------------------------
class CSourceSharpBoneAccessScope
{
public:
	CSourceSharpBoneAccessScope( bool bAllowBones, bool bAllowAttachments, const char *pTag )
		: m_pTag( pTag )
	{
		SourceSharp_BoneAccessPush( bAllowBones, bAllowAttachments, pTag );
	}

	~CSourceSharpBoneAccessScope()
	{
		SourceSharp_BoneAccessPop( m_pTag );
	}

private:
	CSourceSharpBoneAccessScope( const CSourceSharpBoneAccessScope & );
	CSourceSharpBoneAccessScope &operator=( const CSourceSharpBoneAccessScope & );

	const char *m_pTag;
};

//-----------------------------------------------------------------------------
// THE MODEL-BONE GENERATION COUNTER.
//
// Bumped once per frame; a bone cache whose stamp is not the current value is
// stale. Stock's `C_BaseAnimating::InvalidateBoneCaches()` is the bump and
// `g_iModelBoneCounter` is the value; both are here now, and the entity's cache
// stamp is still the entity's.
//-----------------------------------------------------------------------------
void SourceSharp_InvalidateBoneCaches();
unsigned long SourceSharp_ModelBoneCounter();

//-----------------------------------------------------------------------------
// THE BONE-SETUP THREAD POOL, and the two per-frame sweeps over it.
//
// Init/Shutdown are stock's empty pair, kept because the client DLL's start-up
// and shut-down both call them by name and an empty body is the record that the
// pool is the task system's, not this library's.
//
// `SourceSharp_ThreadedBoneSetup()` and `SourceSharp_UpdateClientSideAnimations()`
// each run every registered sweep. A sweep is a `void (*)()` — the list it walks
// and the entity type in it belong to whoever registered it, which is the whole
// reason this header can be read without an entity class in scope.
//-----------------------------------------------------------------------------
void SourceSharp_InitBoneSetupThreadPool();
void SourceSharp_ShutdownBoneSetupThreadPool();

enum SourceSharpBoneSweep_t
{
	// The parallel `SetupBones` pass over whatever was queued this frame.
	kBoneSweep_ThreadedBoneSetup = 0,
	// The per-frame advance of client-side (server-unaware) animation.
	kBoneSweep_ClientSideAnimations,
	// Emptying the parallel setup queue and resetting the generation stamp when
	// a level ends -- lane `f-block`, client family 9. See
	// SourceSharp_ClientBoneSetupLevelShutdown below for why it is a sweep.
	kBoneSweep_LevelShutdown,

	kBoneSweep_Count
};

typedef void (*SourceSharpBoneSweepFn)( void );

// Registration is expected during static initialisation and is capped; the
// cap is a hard Assert rather than a growing vector precisely so that this
// file has no dynamically-initialised state of its own to order against.
void SourceSharp_RegisterBoneSweep( SourceSharpBoneSweep_t which, SourceSharpBoneSweepFn pfn );

void SourceSharp_ThreadedBoneSetup();
void SourceSharp_UpdateClientSideAnimations();

//-----------------------------------------------------------------------------
// THE LEVEL-SHUTDOWN HALF OF THE BONE THREAD POOL -- lane `f-block`, client
// family 9, and the last of this lane's symbols to find a home that survives.
//
// `shell_clientsystems.cpp`'s CBaseAnimatingGameSystem row calls this from
// LevelShutdownPostEntity. It USED to be defined in game/client/c_baseanimating.
// cpp, which plan_entity.md §6 DELETES -- so `nm --defined-only` over all 286
// client objects found the only definition inside the removal set, and the port's
// own shell_clientsystems.o owed a symbol that would simply stop existing.
//
// A SWEEP AND NOT A MOVE, and the difference is the whole point. The body resets
// `g_PreviousBoneSetups` and `g_iPreviousBoneCounter`, which live in
// c_baseanimating.cpp beside ThreadedBoneSetup, whose queue they are -- and
// `g_PreviousBoneSetups` is a `CUtlVector< C_BaseAnimating * >`, TYPED ON THE
// LEAVING CLASS. Moving the body here would drag that type with it. Registering
// it inverts the dependency exactly as ThreadedBoneSetup and
// UpdateClientSideAnimations already do: the leaving TU REGISTERS, this staying
// one RUNS, and when c_baseanimating.cpp leaves the registration stops happening
// and the sweep runs zero callbacks. The capability is removed at the moment the
// queue it empties stops existing, which is the correct moment.
//-----------------------------------------------------------------------------
void SourceSharp_ClientBoneSetupLevelShutdown();

// How many sweeps are registered for a kind. Exists for the gtests: a sweep
// that silently failed to register is the failure mode this re-home can have,
// and it is invisible from the outside otherwise.
int SourceSharp_BoneSweepCount( SourceSharpBoneSweep_t which );

#endif // SOURCESHARP_BONE_LEAF_LIBRARY_H
