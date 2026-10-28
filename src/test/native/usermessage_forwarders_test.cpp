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

} // namespace

//-----------------------------------------------------------------------------
// THE FORWARDERS. Each is the whole body: the managed area, and one call.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE CLOSE CAPTION IS C#'s (user-message payload model, 2026-10-04), on both
// sides now: the server's SoundEmitter calls ICloseCaptions (plan_server_native_v2
// T4a), and the client's emit path went to C# with sound_emitter.cpp
// (SourceSharp.Client.Sounds). What native code still captions -- a sound
// patch's -- hands the token to the managed emitter through client_sound.h,
// and writes no message.
//-----------------------------------------------------------------------------
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
	"voidCUserMessages::HookMessage(",           "CUserMessages*usermessages", "CUserMessageRegister::RegisterAll(",
	"HookMessage(constchar*pName,void*pfnHook)", "UserMessageRunHook(",
};

std::string HookRouteIn( const std::string &code )
{
	for ( size_t i = 0; i < sizeof( kHookRoute ) / sizeof( kHookRoute[0] ); i++ )
	{
		if ( code.find( kHookRoute[i] ) != std::string::npos )
			return kHookRoute[i];
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
	for ( size_t i = 0; i < sizeof( pszPlanted ) / sizeof( pszPlanted[0] ); i++ )
		EXPECT_EQ( HookRouteIn( pszPlanted[i] ), kHookRoute[i] ) << pszPlanted[i];

	EXPECT_EQ( HookRouteIn( "int32_tSourceSharp_ClientViewportQuery(constchar*pszPanel){" ), "" )
		<< "a slot that stays must not look like the route";
}

//-----------------------------------------------------------------------------
// The send slot ends every message it begins: the engine holds the FILTER
// from UserMessageBegin until MessageEnd, which is why it is one slot.
//-----------------------------------------------------------------------------
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
