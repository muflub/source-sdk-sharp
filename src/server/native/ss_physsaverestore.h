//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the server's physics save/restore bookkeeping
//
// plan_server_novalve.md P5 (lane SP). game/shared/physics_saverestore.h's
// g_pPhysSaveRestoreManager is, on the server, asked one thing by one caller:
// physics.cpp's level shutdown says ForgetAllModels. The manager behind it kept
// no table (this build does not save; shell_physsaverestore_absent.cpp), so
// there is nothing to forget, and no engine reads the manager. The server's
// spelling is this free function, on no headers at all; the client keeps the
// Valve manager for its own callers.
//=============================================================================//

#pragma once

namespace ss
{

// Forget every physics object's model association. The server keeps none.
void PhysSaveRestore_ForgetAllModels();

} // namespace ss
