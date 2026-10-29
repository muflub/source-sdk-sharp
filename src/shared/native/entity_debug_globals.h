// Process-wide debug and policy flags that stock declared as static members of
// CBaseEntity and C_BaseEntity. Each is reached through one accessor.
#pragma once

// The entity I/O single-stepper behind ent_pause / ent_step. Paused, the event queue
// fires only as many outputs as steps allow.
inline bool s_bDebugPause = false;

inline bool& EntityDebug_PausedRef()
{
	return s_bDebugPause;
}

inline int s_nDebugSteps = 1;

inline int& EntityDebug_StepsRef()
{
	return s_nDebugSteps;
}

inline void EntityDebug_Pause(bool bPause)
{
	EntityDebug_PausedRef() = bPause;
}

inline bool EntityDebug_IsPaused()
{
	return EntityDebug_PausedRef();
}

inline void EntityDebug_SetSteps(int n)
{
	EntityDebug_StepsRef() = n;
}

// Consumes one step and answers whether any remain after it, as stock's Debug_Step did.
inline bool EntityDebug_Step()
{
	int& nSteps = EntityDebug_StepsRef();
	if (nSteps > 0)
	{
		nSteps--;
	}
	return (nSteps > 0);
}

// May the queue fire right now? Not paused, or paused with steps left.
inline bool EntityDebug_ShouldStep()
{
	return (!EntityDebug_PausedRef() || EntityDebug_StepsRef() > 0);
}

// When set, SOLID_BBOX entities get a fully accurate trigger-vs-bbox check (passed to
// engine->SolidMoved and TriggerMoved). False is the legacy ep1 behaviour.
inline bool s_bAccurate = true;

inline bool& EntityDebug_AccurateTriggerBboxChecksRef()
{
	return s_bAccurate;
}

inline bool EntityDebug_AccurateTriggerBboxChecks()
{
	return EntityDebug_AccurateTriggerBboxChecksRef();
}

inline void EntityDebug_SetAccurateTriggerBboxChecks(bool b)
{
	EntityDebug_AccurateTriggerBboxChecksRef() = b;
}

// Suppresses touch callbacks across a level transition, for overlaps entities were already
// in. Always false on the client.
inline bool s_bDisableTouchFuncs = false;

inline bool& EntityDebug_TouchFuncsDisabledRef()
{
	return s_bDisableTouchFuncs;
}

inline bool EntityDebug_TouchFuncsDisabled()
{
	return EntityDebug_TouchFuncsDisabledRef();
}

inline void EntityDebug_SetTouchFuncsDisabled(bool b)
{
	EntityDebug_TouchFuncsDisabledRef() = b;
}
