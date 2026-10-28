//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: INetMessage interface

#pragma once

class INetMessage;
class INetChannel;

// typedef bool (INetMsgHandler::*PROCESSFUNCPTR)(INetMessage*);
// #define CASTPROCPTR( fn ) static_cast <bool (INetMsgHandler::*)(INetMessage*)> (fn)

class INetMessage
{
public:
	virtual ~INetMessage() {};

	// Use these to setup who can hear whose voice.
	// Pass in client indices (which are their ent indices - 1).

	virtual void SetNetChannel( INetChannel *netchan ) = 0; // netchannel this message is from/for
	virtual void SetReliable( bool state ) = 0; // set to true if it's a reliable message

	virtual bool Process() = 0; // calles the recently set handler to process this message

	virtual bool ReadFromBuffer( bf_read &buffer ) = 0; // returns true if parsing was OK
	virtual bool WriteToBuffer( bf_write &buffer ) = 0; // returns true if writing was OK

	virtual bool IsReliable() const = 0; // true, if message needs reliable handling

	virtual int GetType() const = 0; // returns module specific header tag eg svc_serverinfo
	virtual int GetGroup() const = 0; // returns net message group of this message
	virtual const char *GetName() const = 0; // returns network message name, eg "svc_serverinfo"
	virtual INetChannel *GetNetChannel() const = 0;
	virtual const char *ToString() const = 0; // returns a human readable string about message content

	virtual bool BIncomingMessageForProcessing( double dblNetTime, int numBytes ) = 0;

	virtual size_t GetSize() const = 0;
};
