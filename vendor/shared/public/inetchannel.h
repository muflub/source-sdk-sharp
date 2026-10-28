//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier0/platform.h"
#include "inetchannelinfo.h"
#include "tier1/bitbuf.h"

class	INetMessage;
class	INetChannelInfo;

abstract_class INetChannel : public INetChannelInfo
{
public:
	virtual	~INetChannel( void ) {};

	virtual void unused_SetDataRate() = 0;
	virtual bool	RegisterMessage(INetMessage *msg) = 0;
	virtual void unused_SetTimeout() = 0;
	virtual void unused_SetDemoRecorder() = 0;
	virtual void unused_SetChallengeNr() = 0;
	
	virtual void	Reset( void ) = 0;
	virtual void	Clear( void ) = 0;
	virtual void	Shutdown(const char *reason) = 0;
	
	virtual void unused_ProcessPlayback() = 0;
	virtual void unused_ProcessPacket() = 0;
			
	virtual bool	SendNetMsg(INetMessage &msg, bool bForceReliable = false, bool bVoice = false ) = 0;
	virtual void unused_SendData() = 0;
	virtual void unused_SendFile() = 0;
	virtual void unused_DenyFile() = 0;
	virtual void unused_RequestFile_OLD() = 0;	// get rid of this function when we version the 
	virtual void unused_SetChoked() = 0;
	virtual void unused_SendDatagram() = 0;		
	virtual void unused_Transmit() = 0;

	virtual void unused_GetRemoteAddress() = 0;
	virtual void unused_GetMsgHandler() = 0;
	virtual void unused_GetDropNumber() = 0;
	virtual void unused_GetSocket() = 0;
	virtual void unused_GetChallengeNr() = 0;
	virtual void unused_GetSequenceData() = 0;
	virtual void unused_SetSequenceData() = 0;
		
	virtual void unused_UpdateMessageStats() = 0;
	virtual void unused_CanPacket() = 0;
	virtual bool	IsOverflowed( void ) const = 0;
	virtual void unused_IsTimedOut() = 0;
	virtual void unused_HasPendingReliableData() = 0;

	virtual void unused_SetFileTransmissionMode() = 0;
	virtual void unused_SetCompressionMode() = 0;
	virtual void unused_RequestFile() = 0;
	virtual float	GetTimeSinceLastReceived( void ) const = 0;	// get time since last received packet in seconds

	virtual void unused_SetMaxBufferSize() = 0;

	virtual void unused_IsNull() = 0;
	virtual void unused_GetNumBitsWritten() = 0;
	virtual void unused_SetInterpolationAmount() = 0;
	virtual void unused_SetRemoteFramerate() = 0;

	// Max # of payload bytes before we must split/fragment the packet
	virtual void unused_SetMaxRoutablePayloadSize() = 0;
	virtual void unused_GetMaxRoutablePayloadSize() = 0;

	virtual int		GetProtocolVersion() = 0;
};
