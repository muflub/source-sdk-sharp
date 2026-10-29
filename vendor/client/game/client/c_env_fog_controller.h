//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#define CFogController C_FogController

// Class Fog Controller:
// Compares a set of integer inputs to the one main input
// Outputs true if they are all equivalant, false otherwise
class C_FogController : public C_BaseEntity
{
public:
	DECLARE_NETWORKCLASS();
	DECLARE_CLASS_GAMEROOT( C_FogController,
							C_BaseEntity ); // SourceSharp: the base is our entity face (vendor/README.md)

	C_FogController();

public:
	fogparams_t m_fog;
};
