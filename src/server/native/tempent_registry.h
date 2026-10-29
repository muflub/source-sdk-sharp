//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimEffectsServer: the temp entity SendTables and ServerClasses C# plans, built in
// the window before the engine walks the server class list (shell_tempents.cpp). One
// object, the DLL tier's.

#pragma once

#include "ishim_areas.h"

#include <set>
#include <string>

class SendProp;
class SendTable;
class ServerClass;

// The temp entity tables built from the managed plan. Leaked on purpose: the engine
// reads a SendTable for the life of the process.
enum
{
	SOURCESHARP_TE_MAX_TABLES = 64
};

struct SourceSharpTeBuilt_t
{
	SendProp *pProps;
	int nProps;
	SendTable *pTable;
	ServerClass *pClass; // NULL for a table registered without one
	const char *pClassName; // interned: what a managed reload asks for again
	uint32_t nShapeHash;
};

class CServerTempEnts final : public IShimEffectsServer
{
public:
	CServerTempEnts() = default;
	CServerTempEnts( const CServerTempEnts & ) = delete;
	CServerTempEnts &operator=( const CServerTempEnts & ) = delete;

	// Stores one table; a class built by an earlier load answers its first handle.
	int32_t TempEntBuild( const void *pBlock, int32_t nBlockBytes, SourceSharpTeBuiltTable_t *pOut ) override;

	// Closes the window, once managed has pushed its tables: a ServerClass built after the
	// engine's one walk of the class list is never seen.
	void CloseRegistration();

	// Whether a ServerClass was built here from a C# [TempEntity], by identity.
	bool IsTempEntServerClass( const ServerClass *pClass ) const;

	// For the debug dump. The table and the ServerClass built for handle i are NULL when
	// the plan asked for no ServerClass or i is out of range.
	int BuiltCount() const { return m_nBuilt; }

	bool RegistrationClosed() const { return m_bClosed; }

	SendTable *BuiltTable( int i ) const;
	ServerClass *BuiltClass( int i ) const;

private:
	const char *InternName( const char *pName );

	SourceSharpTeBuilt_t m_Built[SOURCESHARP_TE_MAX_TABLES] = {};
	int m_nBuilt = 0;
	bool m_bClosed = false;

	// Names the engine keeps by pointer. A std::set node never moves, so c_str() is stable.
	std::set<std::string> m_Names;
};
