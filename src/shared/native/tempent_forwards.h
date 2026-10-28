// One id per temp-entity forwarder entry point; managed code holds each one's logic.
// The values are array indices shared with the managed enum: append only. Each row's
// trailing comment names the C++ entry point and the managed value, and is parsed by
// tests. A RESERVED id has no forwarder and must not be reused.

#pragma once
#define SOURCESHARP_TEMPENT_FORWARDS_H

enum SourceSharpTeForward_t
{
	SOURCESHARP_TE_FWD_ARMOR_RICOCHET = 0, // TE_ArmorRicochet -> ArmorRicochet   RESERVED
	SOURCESHARP_TE_FWD_BSP_DECAL = 1, // TE_BSPDecal -> BSPDecal   RESERVED
	SOURCESHARP_TE_FWD_BEAM_POINTS = 2, // TE_BeamPoints -> BeamPoints   RESERVED
	SOURCESHARP_TE_FWD_BLOOD_STREAM = 3, // TE_BloodStream -> BloodStream
	SOURCESHARP_TE_FWD_BUBBLE_TRAIL = 4, // TE_BubbleTrail -> BubbleTrail   RESERVED
	SOURCESHARP_TE_FWD_BUBBLES = 5, // TE_Bubbles -> Bubbles   RESERVED
	SOURCESHARP_TE_FWD_DECAL = 6, // TE_Decal -> Decal
	SOURCESHARP_TE_FWD_TE_DISPATCH_EFFECT = 7, // TE_DispatchEffect -> TeDispatchEffect
	SOURCESHARP_TE_FWD_DUST = 8, // TE_Dust -> Dust   RESERVED
	SOURCESHARP_TE_FWD_ENERGY_SPLASH = 9, // TE_EnergySplash -> EnergySplash   RESERVED
	SOURCESHARP_TE_FWD_EXPLOSION = 10, // TE_Explosion -> Explosion   RESERVED
	SOURCESHARP_TE_FWD_LARGE_FUNNEL = 11, // TE_LargeFunnel -> LargeFunnel   RESERVED
	SOURCESHARP_TE_FWD_METAL_SPARKS = 12, // TE_MetalSparks -> MetalSparks   RESERVED
	SOURCESHARP_TE_FWD_MUZZLE_FLASH = 13, // TE_MuzzleFlash -> MuzzleFlash   RESERVED
	SOURCESHARP_TE_FWD_PROJECT_DECAL = 14, // TE_ProjectDecal -> ProjectDecal   RESERVED
	SOURCESHARP_TE_FWD_SMOKE = 15, // TE_Smoke -> Smoke
	SOURCESHARP_TE_FWD_SPARKS = 16, // TE_Sparks -> Sparks
	SOURCESHARP_TE_FWD_WORLD_DECAL = 17, // TE_WorldDecal -> WorldDecal

	SOURCESHARP_TE_FWD_COUNT = 18,
};
