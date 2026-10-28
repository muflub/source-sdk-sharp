//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Defines the more complete set of operations on the string_t defined
// 			These should be used instead of direct manipulation to allow more
//			flexibility in future ports or optimization.

#pragma once

#ifndef NO_STRING_T

struct string_t
{
public:
	bool operator!=( const string_t &rhs ) const	{ return ( pszValue != rhs.pszValue );	}

	const char *ToCStr() const						{ return ( pszValue ) ? pszValue : ""; 	}
	
protected:
	const char *pszValue;
};

struct castable_string_t : public string_t // string_t is used in unions, hence, no constructor allowed
{
	castable_string_t()							{ pszValue = NULL; }
	castable_string_t( const char *pszFrom )	{ pszValue = (pszFrom && *pszFrom) ? pszFrom : 0; }
};

// Purpose: The correct way to specify the NULL string as a constant.

#define NULL_STRING			castable_string_t()

// Purpose: Given a string_t, make a C string. By convention the result string 
// 			pointer should be considered transient and should not be stored.

#define STRING( string_t_obj )	(string_t_obj).ToCStr()

// Purpose: Given a C string, obtain a string_t

#define MAKE_STRING( c_str )	castable_string_t( c_str )

inline void NetworkVarConstruct( string_t &x ) { x = NULL_STRING; }

#else	// NO_STRING_T

typedef const char *string_t;
#define NULL_STRING				0
#define STRING( c_str )			( c_str )
#define MAKE_STRING( c_str )	( c_str )

#endif	// NO_STRING_T
