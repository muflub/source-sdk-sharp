//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <utldict.h>
#include <utlvector.h>
#include <bitbuf.h>

// Client dispatch function for usermessages
typedef void (*pfnUserMsgHook)(bf_read &msg);

class CUserMessage
{
	public:
		// byte size of message, or -1 for variable sized
		int				size;	
		const char		*name;
		// Client only dispatch function for message
		CUtlVector<pfnUserMsgHook>	clienthooks;
};

// Purpose: Interface for registering and dispatching usermessages
// Shred code creates same ordered list on client/server
class CUserMessages
{
public:
	
	CUserMessages();
	~CUserMessages();
	
	bool	IsValidIndex( int index );

	// Server only
	void	Register( const char *name, int size );

private:

	CUtlDict< CUserMessage*, int >	m_UserMessages;
};
