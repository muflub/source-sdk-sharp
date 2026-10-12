//========= SourceSharp — the game-event listener is taken back ===============//
//
// THE BUG. shell_gameevents.cpp installs a file-scope CSourceSharpEventListener
// into the ENGINE's event manager with AddListener and never called
// RemoveListener anywhere. The engine's event manager holds that pointer
// for the life of the process -- it is set up once at engine start-up and
// its listener list is purged only by its own shutdown, not per map -- so
// the subscription correctly
// outlives every map change, and would just as correctly have outlived the DLL
// the vtable lives in.
//
// It USED TO BE the second object with that shape. The first was handled three
// lines away: SourceSharp_UnloadManagedSide opened with
// SourceSharp_RemoveFileSystemHook under the comment "an object the engine owns
// is pointing at a vtable that lives in this DLL, and this DLL is about to go
// away". That one is gone -- the file-open recorder it unhooked had no reader
// and was deleted -- so the game-event listener is now the only object of the
// shape, and this test is the only thing pinning the pattern. What this test
// pins is unchanged: the unhook exists, and it is called from that one path.
//
// WHY A SOURCE READ. shell_gameevents.cpp needs cbase.h and a live
// IGameEventManager2; there is no unit harness here that can call it. So this
// pins the shape and the ORDER, which is where the two ways to get it wrong
// live -- an unhook that is never called, and one called below the
// `g_bSourceSharpLoaded` early-out so the case that most needs it (a side that
// subscribed and then failed) skips it.
//
//=============================================================================//

#include <fstream>
#include <string>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

const char *kEvents    = "sourcesharp/game/shared/shell_gameevents.cpp";
const char *kGameSystem = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadRaw( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line; all += '\n';
	}
	return all;
}

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string Tight( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

int CountOf( const std::string &hay, const std::string &needle )
{
	if ( needle.empty() )
		return 0;

	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

std::string Events()     { return Tight( StripComments( ReadRaw( kEvents ) ) ); }
std::string GameSystem() { return Tight( StripComments( ReadRaw( kGameSystem ) ) ); }

} // namespace

//=============================================================================//
// 0. THE SUBJECTS ARE REACHABLE, and each is the file this test thinks it is.
//
// Every assertion below is a substring search, and a search over an empty
// string passes every negative asked of it. A file this test cannot open must
// fail HERE and by name.
//=============================================================================//

TEST( GameEventListenerLifetime, TheSourcesAreReadable )
{
	ASSERT_NE( (const char *)NULL, SrcRoot() );

	const std::string events = Events();
	ASSERT_FALSE( events.empty() ) << kEvents << " is empty or unreadable";
	ASSERT_EQ( 1, CountOf( events, "gameeventmanager->AddListener(" ) )
		<< "the AddListener this whole test is about is not in " << kEvents;

	const std::string sys = GameSystem();
	ASSERT_FALSE( sys.empty() ) << kGameSystem << " is empty or unreadable";
	ASSERT_EQ( 1, CountOf( sys, "voidSourceSharp_UnloadManagedSide(void){" ) )
		<< "SourceSharp_UnloadManagedSide is not DEFINED in " << kGameSystem
		<< " -- an anchor on the bare name would have matched a declaration";
}

//=============================================================================//
// 1. THE LISTENER IS TAKEN BACK.
//=============================================================================//

TEST( GameEventListenerLifetime, TheListenerIsRemovedFromTheEnginesManager )
{
	const std::string events = Events();
	ASSERT_FALSE( events.empty() );

	EXPECT_EQ( 1, CountOf( events, "gameeventmanager->RemoveListener(&g_SourceSharpEventListener);" ) )
		<< "nothing removes the listener. AddListener hands the ENGINE a pointer "
		   "to an object in this DLL and the engine keeps it for the whole "
		   "process -- it is set up once at engine start-up and purged only "
		   "by its own shutdown";

	EXPECT_EQ( 1, CountOf( events, "voidSourceSharp_StopListeningForGameEvents(void){" ) )
		<< "the unhook is no longer a function of its own, so nothing outside "
		   "this file can call it";
}

TEST( GameEventListenerLifetime, TheSubscribedNamesArePurgedWithIt )
{
	const std::string events = Events();
	ASSERT_FALSE( events.empty() );

	// The names list is what makes ListenForGameEvent skip a name it has
	// already added. Dropping the listener and KEEPING the names leaves a side
	// that can never re-subscribe, and it is silent -- it looks like C# stopped
	// raising events.
	EXPECT_EQ( 1, CountOf( events, "g_SubscribedEvents.Clear();" ) )
		<< "the subscribed-name list survives the unhook, so a re-subscription "
		   "would be skipped by the already-listening guard and the side would "
		   "receive no events at all";

	const size_t remove = events.find( "gameeventmanager->RemoveListener(&g_SourceSharpEventListener);" );
	const size_t purge  = events.find( "g_SubscribedEvents.Clear();" );
	ASSERT_NE( std::string::npos, remove );
	ASSERT_NE( std::string::npos, purge );
	EXPECT_LT( remove, purge )
		<< "the purge is above the removal, so the two are not the same "
		   "operation and one of them can be reached without the other";
}

//=============================================================================//
// 2. IT IS CALLED, FROM THE PATH WHERE THE DLL IS GOING AWAY, AND ABOVE THE
//    EARLY-OUT.
//
// Three separate ways to be wrong, and the second and third are the ones a
// "does the function exist" test would pass over.
//=============================================================================//

TEST( GameEventListenerLifetime, TheUnhookIsCalledFromTheUnloadPath )
{
	const std::string sys = GameSystem();
	ASSERT_FALSE( sys.empty() );

	EXPECT_EQ( 1, CountOf( sys, "SourceSharp_StopListeningForGameEvents();" ) )
		<< "the unhook exists and nothing calls it";

	const size_t unload = sys.find( "voidSourceSharp_UnloadManagedSide(void){" );
	const size_t call   = sys.find( "SourceSharp_StopListeningForGameEvents();" );
	ASSERT_NE( std::string::npos, unload );
	ASSERT_NE( std::string::npos, call );
	EXPECT_GT( call, unload )
		<< "the call is above SourceSharp_UnloadManagedSide, so it is in some "
		   "other function and the unload path does not unhook";
}

TEST( GameEventListenerLifetime, TheUnhookIsAboveTheLoadedEarlyOut )
{
	const std::string sys = GameSystem();
	ASSERT_FALSE( sys.empty() );

	const size_t unload  = sys.find( "voidSourceSharp_UnloadManagedSide(void){" );
	ASSERT_NE( std::string::npos, unload );

	const size_t call    = sys.find( "SourceSharp_StopListeningForGameEvents();", unload );
	const size_t earlyOut = sys.find( "if(!g_bSourceSharpLoaded)return;", unload );
	ASSERT_NE( std::string::npos, call )     << "no unhook inside SourceSharp_UnloadManagedSide";
	ASSERT_NE( std::string::npos, earlyOut ) << "SourceSharp_UnloadManagedSide no longer has its "
	                                            "g_bSourceSharpLoaded early-out; this test's "
	                                            "ordering assertion has lost its subject";

	EXPECT_LT( call, earlyOut )
		<< "the unhook sits BELOW `if (!g_bSourceSharpLoaded) return;`. The case "
		   "that most needs it is a side that got far enough for C# to subscribe "
		   "and then failed -- which is exactly the case that takes the early "
		   "out. SourceSharp_RemoveFileSystemHook had the same placement for "
		   "the same reason until it was deleted with the file-open recorder "
		   "it unhooked, which had no reader";
}

TEST( GameEventListenerLifetime, TheUnhookIsNotBehindClientDll )
{
	// Both sides subscribe -- the server to the authoritative events, the
	// client to the networked copies -- so an unhook compiled on one side only
	// leaves the other side's dangling pointer exactly where it was, and the
	// file it lives in is game/shared.
	const std::string raw = ReadRaw( kGameSystem );
	ASSERT_FALSE( raw.empty() );

	const size_t call = raw.find( "SourceSharp_StopListeningForGameEvents();" );
	ASSERT_NE( std::string::npos, call );

	// The #ifndef CLIENT_DLL block that guards the filesystem hook ends before
	// the call: find the nearest #endif above it and require an #ifdef/#ifndef
	// does not open between that #endif and the call.
	const size_t endif = raw.rfind( "#endif", call );
	ASSERT_NE( std::string::npos, endif );
	const std::string between = raw.substr( endif, call - endif );
	EXPECT_EQ( std::string::npos, between.find( "#ifndef CLIENT_DLL" ) )
		<< "the unhook is inside a #ifndef CLIENT_DLL, so the client keeps its "
		   "dangling listener";
	EXPECT_EQ( std::string::npos, between.find( "#ifdef CLIENT_DLL" ) )
		<< "the unhook is inside a #ifdef CLIENT_DLL, so the server keeps its "
		   "dangling listener";
}

//=============================================================================//
// 3. EACH SIDE FORWARDS THROUGH ITS MANAGED-BUILT AREA: the server's
//    IManagedEvents (plan_engineapi_vtables E12), the client's
//    IManagedEventsClient (plan_client_vtables CE3).
//=============================================================================//

namespace
{

// The text of FireGameEvent's two arms: from the #ifndef CLIENT_DLL to the
// #else, and from the #else to the #endif. Empty when the shape is not there.
void ForwardArms( std::string &strServer, std::string &strClient )
{
	const std::string events = Tight( StripComments( ReadRaw( kEvents ) ) );
	const size_t fire  = events.find( "virtualvoidFireGameEvent(IGameEvent*pEvent)OVERRIDE{" );
	const size_t open  = fire == std::string::npos ? fire : events.find( "#ifndefCLIENT_DLL", fire );
	const size_t other = open == std::string::npos ? open : events.find( "#else", open );
	const size_t close = other == std::string::npos ? other : events.find( "#endif", other );
	if ( close == std::string::npos )
		return;
	strServer = events.substr( open, other - open );
	strClient = events.substr( other, close - other );
}

} // namespace

TEST( GameEventListenerLifetime, TheServerArmAsksTheEventsArea )
{
	std::string server, client;
	ForwardArms( server, client );
	ASSERT_FALSE( server.empty() ) << "FireGameEvent has no #ifndef CLIENT_DLL / #else arms in " << kEvents;

	EXPECT_EQ( 1, CountOf( server, "if(IManagedEvents*pEvents=ManagedEvents())pEvents->OnGameEvent(pEvent->GetName(),(void*)pEvent);" ) )
		<< "the server arm does not forward through ManagedEvents() with one guard";
	EXPECT_EQ( 0, CountOf( server, "SourceSharp_GetManagedApi" ) )
		<< "the server arm still reads the ManagedApi struct, whose OnGameEvent it no longer fills";
}

TEST( GameEventListenerLifetime, TheClientArmAsksItsEventsArea )
{
	std::string server, client;
	ForwardArms( server, client );
	ASSERT_FALSE( client.empty() ) << "FireGameEvent has no #else arm in " << kEvents;

	EXPECT_EQ( 1, CountOf( client, "if(IManagedEventsClient*pEvents=ManagedEventsClient())pEvents->OnGameEvent(pEvent->GetName(),(void*)pEvent);" ) )
		<< "the client arm does not forward through ManagedEventsClient() with one guard";
	EXPECT_EQ( 0, CountOf( client, "SourceSharp_GetManagedApi" ) )
		<< "the client arm still reads the ManagedApi struct, whose OnGameEvent the Bootstrap no longer fills";
}
