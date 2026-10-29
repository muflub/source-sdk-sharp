// A hitbox trace's Ray_t and trace_t as flat arrays, for managed TestHitboxes.
// The indices are an ABI: managed BoneTraceFlat declares the same numbers.

#pragma once

// The ray, in: Ray_t's four vectors and two flags (as 0.0f / 1.0f).
enum StandaloneBoneRayFloat
{
	SS_BONE_RAY_START_X = 0,
	SS_BONE_RAY_DELTA_X = 3,
	SS_BONE_RAY_START_OFFSET_X = 6,
	SS_BONE_RAY_EXTENTS_X = 9,
	SS_BONE_RAY_IS_RAY = 12,
	SS_BONE_RAY_IS_SWEPT = 13,
	SS_BONE_RAY_FLOATS = 14,
};

// The trace's numbers, in and out.
enum StandaloneBoneTraceFloat
{
	SS_BONE_TRACE_START_X = 0,
	SS_BONE_TRACE_END_X = 3,
	SS_BONE_TRACE_PLANE_NORMAL_X = 6,
	SS_BONE_TRACE_PLANE_DIST = 9,
	SS_BONE_TRACE_FRACTION = 10,
	SS_BONE_TRACE_FLOATS = 11,
};

// The trace's integers and flags, in and out. SURFACE_IS_STUDIO says the hit
// named the surface "**studio**" (stock's TraceToStudio literal); native owns
// the string, so only the fact crosses.
enum StandaloneBoneTraceInt
{
	SS_BONE_TRACE_PLANE_TYPE = 0,
	SS_BONE_TRACE_CONTENTS = 1,
	SS_BONE_TRACE_ALL_SOLID = 2,
	SS_BONE_TRACE_START_SOLID = 3,
	SS_BONE_TRACE_HIT_GROUP = 4,
	SS_BONE_TRACE_HIT_BOX = 5,
	SS_BONE_TRACE_PHYSICS_BONE = 6,
	SS_BONE_TRACE_SURFACE_FLAGS = 7,
	SS_BONE_TRACE_SURFACE_PROPS = 8,
	SS_BONE_TRACE_SURFACE_IS_STUDIO = 9,
	SS_BONE_TRACE_INTS = 10,
};
