//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the recipient filter's functions, as the table names them =//
//
// plan_server_native_v2 T4a. fwd_recipient_filter.cpp writes these and the
// libraries' generated tables (tools/engine-forwarders' HAND row
// RecipientFilter) lay them out, in this order. They are the library's own:
// its version script exports none of them.
//=============================================================================//
#pragma once

class IManagedRecipientFilter;

// How many bytes Construct builds in: at most the words a managed block
// reserves for it (kManagedPhysicsCallbackWords), which managed holds this to.
int SSFwd_RecipientFilter_SizeOf();

// Builds, in pMemory (SizeOf bytes, pointer-aligned: the block's reserved
// words), the IRecipientFilter IEngineSound::EmitSound calls back, over its
// managed side, and answers it as that interface. NULL, and nothing built,
// without memory or a target.
void* SSFwd_RecipientFilter_Construct(void* pMemory, IManagedRecipientFilter* pTarget);
