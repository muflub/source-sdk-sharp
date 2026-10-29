//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include "interface.h"

// Purpose: Engine Center Print Interface
abstract_class ICenterPrint
{
public:
	virtual void SetTextColor(int r, int g, int b, int a) = 0;
	virtual void Print(char* text) = 0;
	virtual void Print(wchar_t * text) = 0;
	virtual void ColorPrint(int r, int g, int b, int a, char* text) = 0;
	virtual void ColorPrint(int r, int g, int b, int a, wchar_t* text) = 0;
	virtual void Clear() = 0;
};

#define VCENTERPRINT_INTERFACE_VERSION "VCENTERPRINT002"
