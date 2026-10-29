//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: insulates client DLL from dependencies on vphysics

#pragma once

struct objectparams_t;
struct solid_t;

// HACKHACK: Make this part of IClientSystem somehow???
extern bool PhysicsDLLInit( CreateInterfaceFn physicsFactory );
extern void PhysicsReset();
extern void PhysicsSimulate();
