//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

abstract_class IVModeManager
{
public:
	virtual void Init() = 0;
	// HL2 will ignore, TF2 will change modes.
	virtual void SwitchMode( bool commander, bool force ) = 0;
	virtual void LevelInit( const char *newmap ) = 0;
	virtual void LevelShutdown() = 0;
};

extern IVModeManager *modemanager;
