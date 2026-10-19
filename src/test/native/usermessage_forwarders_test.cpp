//====== SourceSharp — the user-message forwarders ============================//
//
// plan_side_isolation §2.2a. The user-message registry, id assignment, send,
// dispatch and the handlers are C#'s (UserMessageRegistry,
// UserMessageSender, UserMessageReceiver, and the facts over them). What native
// code keeps is the entry points the engine and Valve's code reach, each one
// forward to managed, and the send slot that carries out C#'s decision against
// the engine. This file holds a fact for each forwarder -- that it reaches
// managed in one call -- and the behaviours of the Valve senders' send half
// carried over from usermessage_ordering_guard_test.cpp, and the fact that no
// native hook route is left (NoNativeHookRouteRemains, with a planted control).
//
// Read from the farm's source text, comments removed and whitespace dropped,
// with each needle a whole construct (a call with its arguments, a definition
// with its body's opening brace) so a longer name cannot satisfy it. Every
// needle was broken once and seen red (the commit records the mutations).
//
//=============================================================================//

#include <string>
#include <cstring>
#include <fstream>
#include <sys/stat.h>
#include <vector>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

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

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

std::string StripComments( const std::string &in )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' ) { bInBlock = false; i++; }
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' ) { bInBlock = true; i++; continue; }
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
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

std::string Code( const char *pszRel ) { return Tight( StripComments( Read( pszRel ) ) ); }

bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
}

// The brace-matched body that follows the first occurrence of pszHead.
std::string BodyAfter( const std::string &code, const char *pszHead )
{
	const size_t at = code.find( pszHead );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = code.find( '{', at + strlen( pszHead ) - 1 );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < code.size(); i++ )
	{
		if ( code[ i ] == '{' ) depth++;
		else if ( code[ i ] == '}' && --depth == 0 )
			return code.substr( open + 1, i - open - 1 );
	}
	return std::string();
}

const char *kGameInterface  = "game/server/gameinterface.cpp";
const char *kShellSend      = "sourcesharp/game/server/shell_usermessage_send.cpp";
const char *kShellGameDll   = "sourcesharp/game/server/shell_gamedll.cpp";
const char *kShellClientDll = "sourcesharp/game/client/shell_clientdll.cpp";
const char *kShellClientMsg = "sourcesharp/game/client/shell_usermessages_client.cpp";
const char *kClientSound    = "sourcesharp/game/client/client_sound.h";

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos; i = hay.find( needle, i + needle.size() ) )
		n++;
	return n;
}

} // namespace

//-----------------------------------------------------------------------------
// THE FORWARDERS. Each is the whole body: the managed area, and one call.
//-----------------------------------------------------------------------------

TEST( UserMessageForwarders, GetUserMessageInfoAsksTheManagedRegistry )
{
	const std::string body = BodyAfter( Code( kShellGameDll ),
		"boolGetUserMessageInfo(intmsg_type,char*name,intmaxnamelength,int&size)override{" );
	ASSERT_FALSE( body.empty() ) << "shell_gamedll.cpp no longer defines GetUserMessageInfo in a shape this reads.";

	EXPECT_EQ( body,
		"IManagedUserMessages*pMessages=ManagedUserMessages();"
		"returnpMessages&&pMessages->GetUserMessageInfo(msg_type,name,maxnamelength,&size);" )
		<< "GetUserMessageInfo is no longer one forward to the managed registry. The engine names and "
		<< "sizes every message it carries through it; an answer native code makes is a second registry.";
}

//-----------------------------------------------------------------------------
// THE CLOSE CAPTION IS C#'s (user-message payload model, 2026-10-04), on both
// sides now: the server's SoundEmitter calls ICloseCaptions (plan_server_native_v2
// T4a), and the client's emit path went to C# with sound_emitter.cpp
// (SourceSharp.Client.Sounds). What native code still captions -- a sound
// patch's -- hands the token to the managed emitter through client_sound.h,
// and writes no message.
//-----------------------------------------------------------------------------
TEST( UserMessageForwarders, TheCloseCaptionIsDecidedAndSentByManaged )
{
	const std::string code = Code( kClientSound );
	ASSERT_FALSE( code.empty() ) << kClientSound << " did not load.";

	EXPECT_EQ( 0u, CountOf( code, "CloseCaptionsWanted" ) )
		<< "client_sound.h asks the managed user messages about captions; whether one is wanted is "
		<< "the managed ClientSoundEmitter's.";
	EXPECT_EQ( 0u, CountOf( code, "pMessages->EmitCloseCaption(" ) )
		<< "client_sound.h hands the user messages a caption; the emitter decides it.";
	// The read is real: the caption crosses to the managed emitter, whole.
	EXPECT_EQ( 1u, CountOf( code, "pApi->ClientSoundEmitCloseCaption(recipients.m_n,iEntIndex,pszToken,flDuration,bWarnIfMissing?1:0);" ) );
	EXPECT_EQ( 0u, CountOf( code, "UserMessageBegin(" ) )
		<< "client_sound.h writes a user message; the CloseCaption payload is C#'s.";
	EXPECT_EQ( 0u, CountOf( code, "CUserMessagePayload" ) );
}

// One crossing a message, the payload copied out whole first (C9, user ruling
// 2026-10-12): the id and one BitBlock, nothing read back.
TEST( UserMessageForwarders, DispatchUserMessageHandsTheIdAndOneBitBlockToManaged )
{
	const std::string body = BodyAfter( Code( kShellClientDll ),
		"boolCShellClientDLL::DispatchUserMessage(intmsg_type,bf_read&msg_data){" );
	ASSERT_FALSE( body.empty() ) << "shell_clientdll.cpp no longer defines DispatchUserMessage in a shape this reads.";

	EXPECT_EQ( body,
		"IManagedUserMessagesClient*pMessages=ManagedUserMessagesClient();"
		"if(!pMessages)returnfalse;"
		"uint8_tblock[ss::kBitBlockHeaderBytes+kMaxUserMessageBlockBytes];"
		"constint32_tnBits=ss::BitBlockFromEngine(msg_data,block,sizeof(block));"
		"if(nBits<0)memcpy(block,&nBits,sizeof(nBits));"
		"returnpMessages->DispatchUserMessage(msg_type,block,sizeof(block));" )
		<< "the client's DispatchUserMessage is no longer one forward of (id, BitBlock) to C#.";
}

//-----------------------------------------------------------------------------
// NO NATIVE HOOK ROUTE (lane HC, 2026-10-04). The six stock HOOK_MESSAGE
// sites are #ifndef SOURCESHARP in the fork and their effects are C#'s
// handlers'; c_user_message_register.cpp, the last compiled caller of
// usermessages->HookMessage, left client.so. So nothing defines HookMessage
// or the `usermessages` object, and since client.so links (tools/nogc-relink
// with nothing collected, too), nothing compiled calls either. The two slots
// that carried hooks across are gone from the tables both sides are
// generated from.
//-----------------------------------------------------------------------------

// The constructs the route was made of, in Tight() spelling: a definition of
// the forwarder and of the object Valve code called it through, the walker
// that called it, the managed-built slot it forwarded to, and the shim slot C#
// ran each hook back through.
const char *const kHookRoute[] = {
	"voidCUserMessages::HookMessage(",
	"CUserMessages*usermessages",
	"CUserMessageRegister::RegisterAll(",
	"HookMessage(constchar*pName,void*pfnHook)",
	"UserMessageRunHook(",
};

std::string HookRouteIn( const std::string &code )
{
	for ( size_t i = 0; i < sizeof( kHookRoute ) / sizeof( kHookRoute[ 0 ] ); i++ )
	{
		if ( code.find( kHookRoute[ i ] ) != std::string::npos )
			return kHookRoute[ i ];
	}
	return std::string();
}

TEST( UserMessageForwarders, ThePlantedHookRouteIsSeen )
{
	// The control: the route as it stood at b6557013, one construct per
	// needle, each found on its own.
	const char *pszPlanted[] = {
		"voidCUserMessages::HookMessage(constchar*name,pfnUserMsgHookhook){}",
		"CUserMessages*usermessages=NULL;",
		"CUserMessageRegister::RegisterAll();",
		"virtualvoidHookMessage(constchar*pName,void*pfnHook)=0;",
		"virtualvoidUserMessageRunHook(void*pfnHook,void*pMsg)=0;",
	};
	for ( size_t i = 0; i < sizeof( pszPlanted ) / sizeof( pszPlanted[ 0 ] ); i++ )
		EXPECT_EQ( HookRouteIn( pszPlanted[ i ] ), kHookRoute[ i ] ) << pszPlanted[ i ];

	EXPECT_EQ( HookRouteIn( "int32_tSourceSharp_ClientViewportQuery(constchar*pszPanel){" ), "" )
		<< "a slot that stays must not look like the route";
}

TEST( UserMessageForwarders, NoNativeHookRouteRemains )
{
	const std::vector< std::string > sources = cmake_graph::ListedSources( cmake_graph::kClient );
	// An anti-vacuity floor, not a count: the list shrinks every phase of
	// plan_client_novalve (198 after lane c5-proxies evicted the proxy TUs; 149
	// after C8's cluster-8/16 evictions, lane c6-input, 2026-10-11).
	ASSERT_GT( sources.size(), 120u ) << "client_sourcesharp's source list read as " << sources.size();

	bool bSawClientMsg = false;
	for ( size_t i = 0; i < sources.size(); i++ )
	{
		EXPECT_EQ( sources[ i ].find( "c_user_message_register.cpp" ), std::string::npos )
			<< "client.so compiles " << sources[ i ] << " again: its RegisterAll calls usermessages->HookMessage";

		const std::string code = Code( sources[ i ].c_str() );
		const std::string hit = HookRouteIn( code );
		EXPECT_EQ( hit, "" ) << sources[ i ] << " holds " << hit << ", a piece of the native hook route";

		if ( sources[ i ] == kShellClientMsg )
		{
			bSawClientMsg = true;
			EXPECT_NE( code.find( "int32_tSourceSharp_ClientViewportQuery(" ), std::string::npos )
				<< "the proof of read: shell_usermessages_client.cpp keeps the viewport query's body";
		}
	}
	EXPECT_TRUE( bSawClientMsg ) << kShellClientMsg << " is not in client_sourcesharp's list";

	// The interfaces both sides are generated from, and the tables they are
	// generated from. Each must read (its live neighbour is there) and hold
	// neither slot.
	struct { const char *pszPath; const char *pszLive; } tables[] = {
		{ "sourcesharp/game/shared/ishim_areas_client.h",   "ClientViewportQuery(" },
		{ "sourcesharp/game/shared/imanaged_areas_client.h", "DispatchUserMessage(" },
		{ "../docs/engineapi-areas.tsv",                     "SourceSharp_ClientViewportQuery" },
		{ "../docs/managed-interfaces.tsv",                  "IManagedUserMessagesClientDispatchUserMessage" },
	};
	for ( size_t i = 0; i < sizeof( tables ) / sizeof( tables[ 0 ] ); i++ )
	{
		const std::string code = Code( tables[ i ].pszPath );
		EXPECT_NE( code.find( tables[ i ].pszLive ), std::string::npos ) << tables[ i ].pszPath << ": the proof of read";
		EXPECT_EQ( code.find( "UserMessageRunHook" ), std::string::npos ) << tables[ i ].pszPath;
		EXPECT_EQ( code.find( "HookMessage" ), std::string::npos ) << tables[ i ].pszPath;
	}
}

//-----------------------------------------------------------------------------
// The send slot ends every message it begins: the engine holds the FILTER
// from UserMessageBegin until MessageEnd, which is why it is one slot.
//-----------------------------------------------------------------------------
TEST( UserMessageForwarders, TheSendSlotEndsEveryMessageItBegins )
{
	const std::string body = BodyAfter( Code( kShellSend ),
		"staticvoidSourceSharp_SendThrough(IRecipientFilter&filter,int32_tnMsgId,constunsignedchar*pData,int32_tnBytes){" );
	ASSERT_FALSE( body.empty() );

	EXPECT_EQ( body,
		"bf_write*pBuf=engine->UserMessageBegin(&filter,nMsgId);"
		"if(pBuf&&nBytes>0)SourceSharp_AppendBits(pBuf,pData,nBytes*8);"
		"engine->MessageEnd();" )
		<< "the send slot no longer begins, appends and ends in one frame.";
}

//-----------------------------------------------------------------------------
// NO NATIVE SENDER IS LEFT (user-message payload model, 2026-10-04).
//
// The Valve-signature entry points native senders called -- UserMessageBegin
// with C#'s name lookup, the dropped-message sentinel, MessageWriteRawBits and
// MessageEnd -- went with the last native sender. Their send-half facts
// (a name C# refuses is dropped before the engine hears of it, MessageEnd does
// not end a dropped message, the sentinel is a class's field) went with them:
// there is no name lookup to refuse and no open message to track. The send
// slot above is the only way a message reaches the engine.
//-----------------------------------------------------------------------------

TEST( UserMessageForwarders, NoValveSenderEntryPointIsLeft )
{
	EXPECT_FALSE( Exists( kGameInterface ) )
		<< "game/server/gameinterface.cpp is back, with stock's UserMessageBegin and MessageEnd.";

	const std::string home = Code( kShellSend );
	ASSERT_FALSE( home.empty() );
	EXPECT_NE( home.find( "int32_tSourceSharp_UserMessageSend(" ), std::string::npos )
		<< "the send slot's body is not where this looks; the absence checks below would prove nothing.";

	EXPECT_EQ( std::string::npos, home.find( "voidUserMessageBegin(" ) );
	EXPECT_EQ( std::string::npos, home.find( "voidMessageEnd(" ) );
	EXPECT_EQ( std::string::npos, home.find( "voidMessageWriteRawBits(" ) );
	EXPECT_EQ( std::string::npos, home.find( "LookupUserMessage" ) )
		<< "a native message name is looked up again; every message is a C# payload.";
}
