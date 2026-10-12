//========= SourceSharp — the console carriers, ported into server.so =======//
//
// plan_server_novalve.md P3. convar_port.cpp replaced tier1's convar.cpp for
// server.so: ConCommandBase, ConCommand, ConVar, ConVarRef and
// ConVar_Register. These pin tier1's behaviour for each, against a fake ICvar
// that records what the engine would have been told. This binary links no
// convar.cpp: the port is the only definition, as it is in server.so.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier1/convar.h"
#include "icvar.h"

#include <algorithm>
#include <map>
#include <string>
#include <vector>

ICvar *g_pCVar = NULL;
ICvar *cvar = NULL;

namespace
{

class CFakeCvar final : public ICvar
{
public:
	std::map< std::string, ConVar * > m_Vars;
	std::vector< ConCommandBase * > m_Registered;
	std::vector< std::string > m_GlobalChanges;
	int m_nUnregisteredId = -2;
	bool m_bMaterialThreadAllowed = true;
	std::vector< std::string > m_Queued;

	virtual bool Connect( CreateInterfaceFn ) { return true; }
	virtual void Disconnect() {}
	virtual void *QueryInterface( const char * ) { return NULL; }
	virtual InitReturnVal_t Init() { return INIT_OK; }
	virtual void Shutdown() {}

	virtual CVarDLLIdentifier_t AllocateDLLIdentifier() { return 7; }
	virtual void RegisterConCommand( ConCommandBase *p ) { m_Registered.push_back( p ); }
	virtual void UnregisterConCommand( ConCommandBase * ) {}
	virtual void UnregisterConCommands( CVarDLLIdentifier_t id ) { m_nUnregisteredId = id; }
	virtual const char *GetCommandLineValue( const char * ) { return NULL; }
	virtual ConCommandBase *FindCommandBase( const char * ) { return NULL; }
	virtual const ConCommandBase *FindCommandBase( const char * ) const { return NULL; }
	virtual ConVar *FindVar( const char *name )
	{
		std::map< std::string, ConVar * >::iterator it = m_Vars.find( name );
		return it == m_Vars.end() ? NULL : it->second;
	}
	virtual const ConVar *FindVar( const char *name ) const { return const_cast< CFakeCvar * >( this )->FindVar( name ); }
	virtual ConCommand *FindCommand( const char * ) { return NULL; }
	virtual const ConCommand *FindCommand( const char * ) const { return NULL; }
	virtual ConCommandBase *GetCommands( void ) { return NULL; }
	virtual const ConCommandBase *GetCommands( void ) const { return NULL; }
	virtual void InstallGlobalChangeCallback( FnChangeCallback_t ) {}
	virtual void RemoveGlobalChangeCallback( FnChangeCallback_t ) {}
	virtual void CallGlobalChangeCallbacks( ConVar *var, const char *pOld, float )
	{
		m_GlobalChanges.push_back( std::string( var->GetName() ) + ":" + pOld + "->" + var->GetString() );
	}
	virtual void InstallConsoleDisplayFunc( IConsoleDisplayFunc * ) {}
	virtual void RemoveConsoleDisplayFunc( IConsoleDisplayFunc * ) {}
	virtual void ConsoleColorPrintf( const Color &, const char *, ... ) const {}
	virtual void ConsolePrintf( const char *, ... ) const {}
	virtual void ConsoleDPrintf( const char *, ... ) const {}
	virtual void RevertFlaggedConVars( int ) {}
	virtual void InstallCVarQuery( ICvarQuery * ) {}
	virtual void PublishToVXConsole() {}
	virtual bool IsMaterialThreadSetAllowed() const { return m_bMaterialThreadAllowed; }
	virtual void QueueMaterialThreadSetValue( ConVar *, const char *p ) { m_Queued.push_back( p ); }
	virtual void QueueMaterialThreadSetValue( ConVar *, int ) { m_Queued.push_back( "int" ); }
	virtual void QueueMaterialThreadSetValue( ConVar *, float ) { m_Queued.push_back( "float" ); }
	virtual bool HasQueuedMaterialThreadConVarSets() const { return false; }
	virtual int ProcessQueuedMaterialThreadConVarSets() { return 0; }
	virtual ICVarIteratorInternal *FactoryInternalIterator( void ) { return NULL; }
};

CFakeCvar *g_pFake = NULL;

// ConCommandBase's registration statics are protected: a derived class reaches them.
struct Registry : ConCommandBase
{
	static ConCommandBase *&Waiting() { return s_pConCommandBases; }
	static IConCommandBaseAccessor *&Accessor() { return s_pAccessor; }
};

class ConVarPort : public ::testing::Test
{
protected:
	virtual void SetUp()
	{
		g_pFake = new CFakeCvar;
		g_pCVar = cvar = g_pFake;
	}
	virtual void TearDown()
	{
		g_pCVar = cvar = NULL;
		delete g_pFake;
		g_pFake = NULL;
	}
};

}

TEST_F( ConVarPort, TheIntIsParsedFromTheTextNotTruncatedFromTheFloat )
{
	ConVar v( "ss_t_b", "0x10", FCVAR_UNREGISTERED );
	EXPECT_EQ( atoi( "0x10" ), v.GetInt() );
	ConVar w( "ss_t_b2", "-3.9", FCVAR_UNREGISTERED );
	EXPECT_EQ( -3, w.GetInt() );
}

TEST_F( ConVarPort, ALongerValueGrowsTheBuffer )
{
	ConVar v( "ss_t_e", "x", FCVAR_UNREGISTERED );
	std::string longer( 300, 'y' );
	v.SetValue( longer.c_str() );
	EXPECT_EQ( longer, v.GetString() );
	v.SetValue( "z" );
	EXPECT_STREQ( "z", v.GetString() );
}

TEST_F( ConVarPort, AFloatIsStoredAsPrintfFText )
{
	ConVar v( "ss_t_f", "0", FCVAR_UNREGISTERED );
	v.SetValue( 1.5f );
	EXPECT_STREQ( "1.500000", v.GetString() );
	v.SetValue( 7 );
	EXPECT_STREQ( "7", v.GetString() );
	EXPECT_FLOAT_EQ( 7.0f, v.GetFloat() );
}

TEST_F( ConVarPort, NeverAsStringKeepsTheTextAndStillTakesTheNumber )
{
	ConVar v( "ss_t_i", "1", FCVAR_UNREGISTERED | FCVAR_NEVER_AS_STRING );
	v.SetValue( "4" );
	EXPECT_EQ( 4, v.GetInt() );
	// convar.h's inline GetString answers this literal for such a variable.
	EXPECT_STREQ( "FCVAR_NEVER_AS_STRING", v.GetString() );
}

TEST_F( ConVarPort, AMaterialThreadVariableIsQueuedWhileItMayNotBeSet )
{
	g_pFake->m_bMaterialThreadAllowed = false;
	ConVar v( "ss_t_j", "1", FCVAR_UNREGISTERED | FCVAR_MATERIAL_THREAD_MASK );
	v.SetValue( "2" );
	EXPECT_STREQ( "1", v.GetString() );
	ASSERT_EQ( 1u, g_pFake->m_Queued.size() );
	EXPECT_EQ( "2", g_pFake->m_Queued[0] );
}

TEST_F( ConVarPort, AVariableBuiltAfterRegisterGoesStraightToTheAccessor )
{
	ConVar_Register( FCVAR_GAMEDLL, NULL );
	ConVar v( "ss_t_l", "1" );
	EXPECT_EQ( 1u, std::count( g_pFake->m_Registered.begin(), g_pFake->m_Registered.end(), (ConCommandBase *)&v ) );
	EXPECT_EQ( 7, v.GetDLLIdentifier() );

	ConVar_Unregister();
	EXPECT_EQ( 7, g_pFake->m_nUnregisteredId );
	EXPECT_EQ( -1, v.GetDLLIdentifier() );
	Registry::Accessor() = NULL;
}

namespace
{

class CRecordingAccessor : public IConCommandBaseAccessor
{
public:
	std::vector< ConCommandBase * > m_Seen;
	virtual bool RegisterConCommandBase( ConCommandBase *p ) { m_Seen.push_back( p ); return true; }
};

int g_nDispatched = 0;

class CCountingCallback : public ICommandCallback
{
public:
	virtual void CommandCallback( const CCommand & ) { g_nDispatched++; }
};

}

TEST_F( ConVarPort, RegisterHandsTheWaitingListToTheAccessorWithTheFlagsAdded )
{
	Registry::Waiting() = NULL;
	ConVar a( "ss_t_m1", "1" );
	ConVar b( "ss_t_m2", "1" );
	ConVar hidden( "ss_t_m3", "1", FCVAR_UNREGISTERED );

	CRecordingAccessor accessor;
	ConVar_Register( FCVAR_GAMEDLL, &accessor );

	ASSERT_EQ( 2u, accessor.m_Seen.size() );
	EXPECT_EQ( (ConCommandBase *)&b, accessor.m_Seen[0] );	// newest first
	EXPECT_EQ( (ConCommandBase *)&a, accessor.m_Seen[1] );
	EXPECT_TRUE( a.IsFlagSet( FCVAR_GAMEDLL ) );
	EXPECT_FALSE( hidden.IsFlagSet( FCVAR_GAMEDLL ) );
	EXPECT_EQ( NULL, Registry::Waiting() );

	ConVar_Unregister();
	Registry::Accessor() = NULL;
}

TEST_F( ConVarPort, RegisterTwiceIsOnce )
{
	CRecordingAccessor first, second;
	ConVar_Register( 0, &first );
	ConVar_Register( 0, &second );
	EXPECT_EQ( &first, Registry::Accessor() );
	ConVar_Unregister();
	Registry::Accessor() = NULL;
}

TEST_F( ConVarPort, ACommandDispatchesToItsInterface )
{
	g_nDispatched = 0;
	CCountingCallback cb;
	ConCommand c( "ss_t_cmd", &cb, "h", FCVAR_UNREGISTERED );
	EXPECT_TRUE( c.IsCommand() );
	EXPECT_FALSE( c.CanAutoComplete() );
	// CCommand's constructor is tier1's and not ported (server.so never builds
	// one); the callback does not read it.
	alignas( CCommand ) unsigned char mem[sizeof( CCommand )] = {};
	c.Dispatch( *reinterpret_cast< const CCommand * >( mem ) );
	EXPECT_EQ( 1, g_nDispatched );
}

TEST_F( ConVarPort, ARefToAKnownNameReadsIt )
{
	ConVar v( "ss_t_ref", "42", FCVAR_UNREGISTERED );
	g_pFake->m_Vars["ss_t_ref"] = &v;
	ConVarRef r( "ss_t_ref" );
	EXPECT_TRUE( r.IsValid() );
	EXPECT_EQ( 42, r.GetInt() );
}

TEST_F( ConVarPort, ARefToAnUnknownNameIsInvalidReadsZeroAndIgnoresWrites )
{
	// The server's spelling (SourceSharp_EngineVisualizeTraces): built empty,
	// then Init()ed; the by-name constructor left server.so (gc cull).
	ConVarRef r( (IConVar *)NULL );
	r.Init( "ss_t_missing", true );
	EXPECT_FALSE( r.IsValid() );
	EXPECT_EQ( 0, r.GetInt() );
	r.SetValue( 5 );
	EXPECT_EQ( 0, r.GetInt() );
	EXPECT_STREQ( "", r.GetName() );

	ConVarRef n( (IConVar *)NULL );
	EXPECT_FALSE( n.IsValid() );
}

TEST_F( ConVarPort, DevelopmentOnlyFollowsTheBuild )
{
	ConVar v( "ss_t_dev", "1", FCVAR_UNREGISTERED | FCVAR_DEVELOPMENTONLY );
#ifndef NDEBUG
	EXPECT_FALSE( v.IsFlagSet( FCVAR_DEVELOPMENTONLY ) );
#else
	EXPECT_TRUE( v.IsFlagSet( FCVAR_DEVELOPMENTONLY ) );
#endif
}
