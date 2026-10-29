//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "ifilelist.h"

// Particle attachment methods
enum ParticleAttachment_t
{
	PATTACH_ABSORIGIN = 0, // Create at absorigin, but don't follow
	PATTACH_ABSORIGIN_FOLLOW, // Create at absorigin, and update to follow the entity
	PATTACH_CUSTOMORIGIN, // Create at a custom origin, but don't follow
	PATTACH_POINT, // Create on attachment point, but don't follow
	PATTACH_POINT_FOLLOW, // Create on attachment point, and update to follow the entity

	PATTACH_WORLDORIGIN, // Used for control points that don't attach to an entity

	PATTACH_ROOTBONE_FOLLOW, // Create at the root bone of the entity, and update to follow

	MAX_PATTACH_TYPES,
};

struct te_tf_particle_effects_colors_t
{
	Vector m_vecColor1;
	Vector m_vecColor2;
};

struct te_tf_particle_effects_control_point_t
{
	ParticleAttachment_t m_eParticleAttachment;
	Vector m_vecOffset;
};

// Particle parsing methods
// Parse the particle manifest file & register the effects within it
// Only needs to be called once per game, unless tools change particle definitions
void ParseParticleEffects(bool bLoadSheets, bool bPrecache);
void ParseParticleEffectsMap(const char* pMapName, bool bLoadSheets);

class IFileList;
void ReloadParticleEffects();

void DispatchParticleEffect(const char* pszParticleName, Vector vecOrigin, QAngle vecAngles,
							CBaseEntity* pEntity = NULL);
void DispatchParticleEffect(int iEffectIndex, Vector vecOrigin, Vector vecStart, QAngle vecAngles,
							CBaseEntity* pEntity = NULL);
