//========= Copyright Valve Corporation, All rights reserved. ============//
// This is the null header file used to remove Telemetry calls.

#define TMERR_DISABLED 1
#define TMPRINTF_TOKEN_NONE 0


#define tmEndTryLock(...)
#define tmEndTryLockEx(...)
#define tmSetLockState(...)
#define tmSetLockStateEx(...)






#define tmSendCallStack( ... ) TMPRINTF_TOKEN_NONE












#define tmZone(...) 
#define tmZoneFiltered(...)









#define tmDynamicString(...) ""











#define tmLockName(...)
#define tmMessage(...)



#define tmTryLock(...)
#define tmTryLockEx(...)
    












 









