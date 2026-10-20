//========= SourceSharp: the native forwards' identifiers ====================//
//
// ONE NUMBER PER FORWARDER ENTRY POINT, and the contract between the forwards
// and the managed code that now holds their logic.
//
// A forward used to know three things C++ has no business knowing: which temp
// entity class, which argument becomes which field, and what the field is
// called. All three are managed now, and what crosses the boundary is this id
// plus the arguments the signature already had, packed in declaration order.
//
// THE ORDER IS THE CONTRACT. These are array indices in all but name, so a
// value inserted in the middle renumbers everything after it and silently
// repoints every forward past that point at a different effect. Append only,
// and TempEntForwardIdTests compares this file against the managed enum rather
// than trusting the two to stay in step -- the same disk-parity check the
// declarations get against stock's own .cpp.
//
// IT WAS FORTY-TWO AND IT WAS RENUMBERED ONCE, HERE, which is the one exception
// to the paragraph above and is written down so the exception does not become a
// habit. plan_shim.md Phase 1f collapsed seventeen forwarder .cpp into two --
// shell_te_forwards.cpp and shell_te_forwards_client.cpp -- and twenty-four of
// the forty-two entry points went with them because nothing reached them: on
// the server tools/shim-census over the debug link line at c5eab9deb found no
// importer and no intra-file caller, and on the client all fifty-two were
// reached only through c_te.cpp's vtable, whose surviving `te->` call sites on
// the link line are three. A renumber is safe ONLY because both sides are in
// this repository and are rebuilt together: nothing persists one of these
// numbers across a build, so there is no saved file, no wire format and no
// other process holding a stale index. That is what made it safe once; it does
// not make appending-in-the-middle safe later.
//
// Each line carries BOTH names: the C++ entry point and the TeForward value it
// is. Spelling the managed name here rather than deriving it means there is no
// stripping rule for the two sides to disagree about -- which matters, because
// DispatchEffect and TE_DispatchEffect are different functions that strip to
// the same word. (The bare `DispatchEffect` id is one of the twenty-four that
// went: nothing native ever passed it and no dispatcher arm ever read it.)
//
// RESERVED ROWS. Four forwarders -- TE_ArmorRicochet, TE_BeamPoints,
// TE_BubbleTrail, TE_EnergySplash -- were deleted by the server gc cull
// (2026-10-01): nothing called them and the linker discarded them. Their ids
// are KEPT and marked RESERVED rather than removed, because removing one is a
// renumber and the paragraph above says the one renumber was not a precedent.
// No forwarder may use a RESERVED id (tempent_evicted_test checks both ways);
// a later renumber pass that owns the whole table can drop them. Three more
// joined them in plan_server_native_v2 T4b -- TE_Dust, TE_MetalSparks and
// TE_MuzzleFlash, whose only callers were ss::IEffects methods no server TU
// called. And two more in T4d -- TE_Explosion and TE_LargeFunnel, whose only
// callers were the ExplosionEffect and FunnelEffect slots EffectsOverTempEnts
// had already replaced. And two in T4f -- TE_BSPDecal and TE_ProjectDecal,
// whose only callers were the PaintDecal slot; EffectsOverTempEnts sends those
// temp entities itself now. The server's TE_Bubbles went with the BubbleEffect
// slot too, and its id stays live: the client's shell_te_forwards_client.cpp
// still forwards one.
//
//===========================================================================//

#ifndef SOURCESHARP_TEMPENT_FORWARDS_H
#define SOURCESHARP_TEMPENT_FORWARDS_H
#ifdef _WIN32
#pragma once
#endif

enum SourceSharpTeForward_t
{
	SOURCESHARP_TE_FWD_ARMOR_RICOCHET              =  0,   // TE_ArmorRicochet -> ArmorRicochet   RESERVED
	SOURCESHARP_TE_FWD_BSP_DECAL                   =  1,   // TE_BSPDecal -> BSPDecal   RESERVED
	SOURCESHARP_TE_FWD_BEAM_POINTS                 =  2,   // TE_BeamPoints -> BeamPoints   RESERVED
	SOURCESHARP_TE_FWD_BLOOD_STREAM                =  3,   // TE_BloodStream -> BloodStream
	SOURCESHARP_TE_FWD_BUBBLE_TRAIL                =  4,   // TE_BubbleTrail -> BubbleTrail   RESERVED
	SOURCESHARP_TE_FWD_BUBBLES                     =  5,   // TE_Bubbles -> Bubbles
	SOURCESHARP_TE_FWD_DECAL                       =  6,   // TE_Decal -> Decal
	SOURCESHARP_TE_FWD_TE_DISPATCH_EFFECT          =  7,   // TE_DispatchEffect -> TeDispatchEffect
	SOURCESHARP_TE_FWD_DUST                        =  8,   // TE_Dust -> Dust   RESERVED
	SOURCESHARP_TE_FWD_ENERGY_SPLASH               =  9,   // TE_EnergySplash -> EnergySplash   RESERVED
	SOURCESHARP_TE_FWD_EXPLOSION                   = 10,   // TE_Explosion -> Explosion   RESERVED
	SOURCESHARP_TE_FWD_LARGE_FUNNEL                = 11,   // TE_LargeFunnel -> LargeFunnel   RESERVED
	SOURCESHARP_TE_FWD_METAL_SPARKS                = 12,   // TE_MetalSparks -> MetalSparks   RESERVED
	SOURCESHARP_TE_FWD_MUZZLE_FLASH                = 13,   // TE_MuzzleFlash -> MuzzleFlash   RESERVED
	SOURCESHARP_TE_FWD_PROJECT_DECAL               = 14,   // TE_ProjectDecal -> ProjectDecal   RESERVED
	SOURCESHARP_TE_FWD_SMOKE                       = 15,   // TE_Smoke -> Smoke
	SOURCESHARP_TE_FWD_SPARKS                      = 16,   // TE_Sparks -> Sparks
	SOURCESHARP_TE_FWD_WORLD_DECAL                 = 17,   // TE_WorldDecal -> WorldDecal

	SOURCESHARP_TE_FWD_COUNT                       = 18,
};

#endif // SOURCESHARP_TEMPENT_FORWARDS_H
