//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// Purpose: Generic interface for routing messages to users
class IRecipientFilter
{
public:
	virtual			~IRecipientFilter() {}

	virtual bool	IsReliable( void ) const = 0;
	virtual bool	IsInitMessage( void ) const = 0;

	virtual int		GetRecipientCount( void ) const = 0;
	virtual int		GetRecipientIndex( int slot ) const = 0;
};
