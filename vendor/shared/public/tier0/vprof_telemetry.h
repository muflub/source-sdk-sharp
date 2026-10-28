//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Real-Time Hierarchical Telemetry Profiling

#pragma once

// Kill all tmZone() macros, etc.
#include "tmapi_dummy.h"

#define TelemetrySetLockName( _ctx, _location, _description ) 

#define TM_ZONE_DEFAULT( context ) 
#define TM_ZONE_DEFAULT_PARAM( context, string_param ) 
