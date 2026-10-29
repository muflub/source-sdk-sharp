// Which control points a managed particle system follows its owner with. An unwritten
// control point reads as the map origin, so effects such as burning_character, whose
// operators read point 1, need it fed.
#pragma once

// An unconfigured system follows control point 0 only.
enum
{
	kSourceSharpParticleFollowDefault = 1
};

// The highest point a 32-bit mask can name. MAX_PARTICLE_CONTROL_POINTS is 64, so the
// mask runs out before the array does.
enum
{
	kSourceSharpParticleMaxFollowPoint = 31
};

// Bit 0 is forced on, or the effect would stay where it was created.
inline int SourceSharpParticleFollow_Normalise(int nRequested)
{
	return nRequested | kSourceSharpParticleFollowDefault;
}

// Whether the sweep keeps this point at the owner. Out-of-range points are not clamped.
inline bool SourceSharpParticleFollow_Wants(int nMask, int nPoint)
{
	if (nPoint < 0 || nPoint > kSourceSharpParticleMaxFollowPoint)
		return false;

	return (nMask & (1 << nPoint)) != 0;
}
