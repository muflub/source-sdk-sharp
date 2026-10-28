//========= Copyright � 1996-2008, Valve LLC, All rights reserved. ============
// Declare common types used by the Steamworks SDK.

#pragma once

#include "steamtypes.h"
#include "steamuniverse.h"

// Josh: Fixes building gcsdk which passes in stuff from protobuf.
#if defined( PLATFORM_64BITS ) && defined( LINUX )
#include <stdint.h>
#define INT64_DIFFERENT_FROM_INT64_T
#endif

// Steam account types
enum EAccountType
{
	k_EAccountTypeInvalid = 0,
	k_EAccountTypeIndividual = 1, // single user account
	k_EAccountTypeMultiseat = 2, // multiseat (e.g. cybercafe) account
	k_EAccountTypeGameServer = 3, // game server account
	k_EAccountTypeAnonGameServer = 4, // anonymous game server account
	k_EAccountTypePending = 5, // pending
	k_EAccountTypeContentServer = 6, // content server
	k_EAccountTypeClan = 7,
	k_EAccountTypeChat = 8,
	k_EAccountTypeConsoleUser = 9, // Fake SteamID for local PSN account on PS3 or Live account on 360, etc.
	k_EAccountTypeAnonUser = 10,

	// Max of 16 items in this field
	k_EAccountTypeMax
};

const unsigned int k_unSteamAccountIDMask = 0xFFFFFFFF;
const unsigned int k_unSteamAccountInstanceMask = 0x000FFFFF;
const unsigned int k_unSteamUserDefaultInstance = 1; // fixed instance for all individual users

// Special flags for Chat accounts - they go in the top 8 bits
// of the steam ID's "instance", leaving 12 for the actual instances
enum EChatSteamIDInstanceFlags
{
	k_EChatAccountInstanceMask = 0x00000FFF, // top 8 bits are flags

	k_EChatInstanceFlagClan = ( k_unSteamAccountInstanceMask + 1 ) >> 1, // top bit
	k_EChatInstanceFlagLobby = ( k_unSteamAccountInstanceMask + 1 ) >> 2, // next one down, etc
	k_EChatInstanceFlagMMSLobby = ( k_unSteamAccountInstanceMask + 1 ) >> 3, // next one down, etc

	// Max of 8 flags
};

#pragma pack( push, 1 )

// Steam ID structure (64 bits total)
class CSteamID
{
public:
	// Purpose: Constructor
	CSteamID()
	{
		m_steamid.m_comp.m_unAccountID = 0;
		m_steamid.m_comp.m_EAccountType = k_EAccountTypeInvalid;
		m_steamid.m_comp.m_EUniverse = k_EUniverseInvalid;
		m_steamid.m_comp.m_unAccountInstance = 0;
	}

	// Purpose: Constructor
	// Input  : unAccountID -	32-bit account ID
	//			eUniverse -		Universe this account belongs to
	//			eAccountType -	Type of account
	CSteamID( uint32 unAccountID, EUniverse eUniverse, EAccountType eAccountType )
	{
		Set( unAccountID, eUniverse, eAccountType );
	}

	// Purpose: Constructor
	// Input  : unAccountID -	32-bit account ID
	//			unAccountInstance - instance
	//			eUniverse -		Universe this account belongs to
	//			eAccountType -	Type of account
	CSteamID( uint32 unAccountID, unsigned int unAccountInstance, EUniverse eUniverse, EAccountType eAccountType )
	{
		InstancedSet( unAccountID, unAccountInstance, eUniverse, eAccountType );
	}

	// Purpose: Constructor
	// Input  : ulSteamID -		64-bit representation of a Steam ID
	// Note:	Will not accept a uint32 or int32 as input, as that is a probable mistake.
	//			See the stubbed out overloads in the private: section for more info.
	CSteamID( uint64 ulSteamID ) { SetFromUint64( ulSteamID ); }
#ifdef INT64_DIFFERENT_FROM_INT64_T
	CSteamID( uint64_t ulSteamID ) { SetFromUint64( (uint64)ulSteamID ); }
#endif

	// Purpose: Sets parameters for steam ID
	// Input  : unAccountID -	32-bit account ID
	//			eUniverse -		Universe this account belongs to
	//			eAccountType -	Type of account
	void Set( uint32 unAccountID, EUniverse eUniverse, EAccountType eAccountType )
	{
		m_steamid.m_comp.m_unAccountID = unAccountID;
		m_steamid.m_comp.m_EUniverse = eUniverse;
		m_steamid.m_comp.m_EAccountType = eAccountType;

		if ( eAccountType == k_EAccountTypeClan || eAccountType == k_EAccountTypeGameServer )
		{
			m_steamid.m_comp.m_unAccountInstance = 0;
		}
		else
		{
			m_steamid.m_comp.m_unAccountInstance = k_unSteamUserDefaultInstance;
		}
	}

	// Purpose: Sets parameters for steam ID
	// Input  : unAccountID -	32-bit account ID
	//			eUniverse -		Universe this account belongs to
	//			eAccountType -	Type of account
	void InstancedSet( uint32 unAccountID, uint32 unInstance, EUniverse eUniverse, EAccountType eAccountType )
	{
		m_steamid.m_comp.m_unAccountID = unAccountID;
		m_steamid.m_comp.m_EUniverse = eUniverse;
		m_steamid.m_comp.m_EAccountType = eAccountType;
		m_steamid.m_comp.m_unAccountInstance = unInstance;
	}

	// Purpose: Initializes a steam ID from its 64-bit representation
	// Input  : ulSteamID -		64-bit representation of a Steam ID
	void SetFromUint64( uint64 ulSteamID ) { m_steamid.m_unAll64Bits = ulSteamID; }

	// Purpose: Clear all fields, leaving an invalid ID.
	void Clear()
	{
		m_steamid.m_comp.m_unAccountID = 0;
		m_steamid.m_comp.m_EAccountType = k_EAccountTypeInvalid;
		m_steamid.m_comp.m_EUniverse = k_EUniverseInvalid;
		m_steamid.m_comp.m_unAccountInstance = 0;
	}

	// Purpose: create an anonymous game server login to be filled in by the AM
	void CreateBlankAnonLogon( EUniverse eUniverse )
	{
		m_steamid.m_comp.m_unAccountID = 0;
		m_steamid.m_comp.m_EAccountType = k_EAccountTypeAnonGameServer;
		m_steamid.m_comp.m_EUniverse = eUniverse;
		m_steamid.m_comp.m_unAccountInstance = 0;
	}

	bool IsValid() const;

	// this set of functions is hidden, will be moved out of class
	explicit CSteamID( const char *pchSteamID, EUniverse eDefaultUniverse = k_EUniverseInvalid );
	const char *Render() const; // renders this steam ID to string
	static const char *Render(
		uint64 ulSteamID ); // static method to render a uint64 representation of a steam ID to a string

	void SetFromString( const char *pchSteamID, EUniverse eDefaultUniverse );
	// SetFromString allows many partially-correct strings, constraining how
	// we might be able to change things in the future.
	// SetFromStringStrict requires the exact string forms that we support
	// and is preferred when the caller knows it's safe to be strict.
	// Returns whether the string parsed correctly.
	bool SetFromStringStrict( const char *pchSteamID, EUniverse eDefaultUniverse );

	inline bool operator==( const CSteamID &val ) const
	{
		return m_steamid.m_unAll64Bits == val.m_steamid.m_unAll64Bits;
	}

	// DEBUG function
	bool BValidExternalSteamID() const;

private:
	// These are defined here to prevent accidental implicit conversion of a u32AccountID to a CSteamID.
	// If you get a compiler error about an ambiguous constructor/function then it may be because you're
	// passing a 32-bit int to a function that takes a CSteamID. You should explicitly create the SteamID
	// using the correct Universe and account Type/Instance values.
	CSteamID( uint32 );
	CSteamID( int32 );

	// 64 bits total
	union SteamID_t
	{
		struct SteamIDComponent_t
		{
#ifdef VALVE_BIG_ENDIAN
			EUniverse m_EUniverse : 8; // universe this account belongs to
			unsigned int m_EAccountType
				: 4; // type of account - can't show as EAccountType, due to signed / unsigned difference
			unsigned int m_unAccountInstance : 20; // dynamic instance ID
			uint32 m_unAccountID : 32; // unique account identifier
#else
			uint32 m_unAccountID : 32; // unique account identifier
			unsigned int m_unAccountInstance : 20; // dynamic instance ID
			unsigned int m_EAccountType
				: 4; // type of account - can't show as EAccountType, due to signed / unsigned difference
			EUniverse m_EUniverse : 8; // universe this account belongs to
#endif
		} m_comp;

		uint64 m_unAll64Bits;
	} m_steamid;
};

inline bool CSteamID::IsValid() const
{
	if ( m_steamid.m_comp.m_EAccountType <= k_EAccountTypeInvalid ||
		 m_steamid.m_comp.m_EAccountType >= k_EAccountTypeMax )
		return false;

	if ( m_steamid.m_comp.m_EUniverse <= k_EUniverseInvalid || m_steamid.m_comp.m_EUniverse >= k_EUniverseMax )
		return false;

	if ( m_steamid.m_comp.m_EAccountType == k_EAccountTypeIndividual )
	{
		if ( m_steamid.m_comp.m_unAccountID == 0 ||
			 m_steamid.m_comp.m_unAccountInstance != k_unSteamUserDefaultInstance )
			return false;
	}

	if ( m_steamid.m_comp.m_EAccountType == k_EAccountTypeClan )
	{
		if ( m_steamid.m_comp.m_unAccountID == 0 || m_steamid.m_comp.m_unAccountInstance != 0 )
			return false;
	}

	if ( m_steamid.m_comp.m_EAccountType == k_EAccountTypeGameServer )
	{
		if ( m_steamid.m_comp.m_unAccountID == 0 )
			return false;
		// Any limit on instances?  We use them for local users and bots
	}
	return true;
}

#if defined( INCLUDED_STEAM2_USERID_STRUCTS )
#include "steamcommon.h" // TSteamGlobalUserID

// Purpose: Initializes a steam ID from a Steam2 ID structure
// Input:	pTSteamGlobalUserID -	Steam2 ID to convert
//			eUniverse -				universe this ID belongs to
inline CSteamID SteamIDFromSteam2UserID( TSteamGlobalUserID *pTSteamGlobalUserID, EUniverse eUniverse )
{
	uint32 unAccountID = pTSteamGlobalUserID->m_SteamLocalUserID.Split.Low32bits * 2 +
		pTSteamGlobalUserID->m_SteamLocalUserID.Split.High32bits;

	return CSteamID( unAccountID, k_unSteamUserDefaultInstance, eUniverse, k_EAccountTypeIndividual );
}

bool SteamIDFromSteam2String( const char *pchSteam2ID, EUniverse eUniverse, CSteamID *pSteamIDOut );

#endif

// generic invalid CSteamID

// This steamID comes from a user game connection to an out of date GS that hasnt implemented the protocol
// to provide its steamID

// This steamID comes from a user game connection to an sv_lan GS

// This steamID can come from a user game connection to a GS that has just booted but hasnt yet even initialized
// its steam3 component and started logging on.

// This steamID can come from a user game connection to a GS that isn't using the steam authentication system but still
// wants to support the "Join Game" option in the friends list

// Purpose: encapsulates an appID/modID pair
class CGameID
{
public:
	enum EGameIDType
	{
		k_EGameIDTypeApp = 0,
		k_EGameIDTypeGameMod = 1,
		k_EGameIDTypeShortcut = 2,
		k_EGameIDTypeP2P = 3,
	};

	CGameID()
	{
		m_gameID.m_nType = k_EGameIDTypeApp;
		m_gameID.m_nAppID = k_uAppIdInvalid;
		m_gameID.m_nModID = 0;
	}

	explicit CGameID( uint64 ulGameID ) { m_ulGameID = ulGameID; }
#ifdef INT64_DIFFERENT_FROM_INT64_T
	CGameID( uint64_t ulGameID ) { m_ulGameID = (uint64)ulGameID; }
#endif

	explicit CGameID( int32 nAppID )
	{
		m_ulGameID = 0;
		m_gameID.m_nAppID = nAppID;
	}

	explicit CGameID( uint32 nAppID )
	{
		m_ulGameID = 0;
		m_gameID.m_nAppID = nAppID;
	}

	// Not validating anything .. use IsValid()
	explicit CGameID( uint32 nAppID, uint32 nModID, CGameID::EGameIDType nType )
	{
		m_gameID.m_nAppID = nAppID;
		m_gameID.m_nModID = nModID;
		m_gameID.m_nType = nType;
	}

	CGameID( const CGameID &that ) { m_ulGameID = that.m_ulGameID; }

	// Hidden functions used only by Steam
	explicit CGameID( const char *pchGameID );

	void Set( uint64 ulGameID ) { m_ulGameID = ulGameID; }

	bool IsValid() const
	{
		// each type has it's own invalid fixed point:
		switch ( m_gameID.m_nType )
		{
			case k_EGameIDTypeApp:
				return m_gameID.m_nAppID != k_uAppIdInvalid;

			case k_EGameIDTypeGameMod:
				return m_gameID.m_nAppID != k_uAppIdInvalid && ( m_gameID.m_nModID & 0x80000000 );

			case k_EGameIDTypeShortcut:
				return m_gameID.m_nAppID == k_uAppIdInvalid && ( m_gameID.m_nModID & 0x80000000 ) &&
					m_gameID.m_nModID >=
					( 5000 | 0x80000000 ); // k_unMaxExpectedLocalAppId - shortcuts are pushed beyond that range

			case k_EGameIDTypeP2P:
				return m_gameID.m_nAppID == k_uAppIdInvalid && ( m_gameID.m_nModID & 0x80000000 );

			default:
				return false;
		}
	}

	void Reset() { m_ulGameID = 0; }

	// Internal stuff.  Use the accessors above if possible

	struct GameID_t
	{
#ifdef VALVE_BIG_ENDIAN
		unsigned int m_nModID : 32;
		unsigned int m_nType : 8;
		unsigned int m_nAppID : 24;
#else
		unsigned int m_nAppID : 24;
		unsigned int m_nType : 8;
		unsigned int m_nModID : 32;
#endif
	};

	union
	{
		uint64 m_ulGameID;
		GameID_t m_gameID;
	};
};

#pragma pack( pop )

// Define compile time assert macros to let us validate the structure sizes.
#define VALVE_COMPILE_TIME_ASSERT( pred ) typedef char compile_time_assert_type[( pred ) ? 1 : -1];

#if defined( __linux__ ) || defined( __FreeBSD__ )
// The 32-bit version of gcc has the alignment requirement for uint64 and double set to
// 4 meaning that even with #pragma pack(8) these types will only be four-byte aligned.
// The 64-bit version of gcc has the alignment requirement for these types set to
// 8 meaning that unless we use #pragma pack(4) our structures will get bigger.
// The 64-bit structure packing has to match the 32-bit structure packing for each platform.
#define VALVE_CALLBACK_PACK_SMALL
#else
#define VALVE_CALLBACK_PACK_LARGE
#endif

#if defined( VALVE_CALLBACK_PACK_SMALL )
#pragma pack( push, 4 )
#elif defined( VALVE_CALLBACK_PACK_LARGE )
#pragma pack( push, 8 )
#else
#error ???
#endif

typedef struct ValvePackingSentinel_t
{
	uint32 m_u32;
	uint64 m_u64;
	uint16 m_u16;
	double m_d;
} ValvePackingSentinel_t;

#pragma pack( pop )

#if defined( VALVE_CALLBACK_PACK_SMALL )
VALVE_COMPILE_TIME_ASSERT( sizeof( ValvePackingSentinel_t ) == 24 )
#elif defined( VALVE_CALLBACK_PACK_LARGE )
VALVE_COMPILE_TIME_ASSERT( sizeof( ValvePackingSentinel_t ) == 32 )
#else
#error ???
#endif
