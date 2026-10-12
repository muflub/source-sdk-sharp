//========= SourceSharp — ss_command_line.h ===================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//=============================================================================//

#include "ss_command_line.h"

#include <fcntl.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <unistd.h>

#include <string>

namespace ss
{

namespace
{
// /proc/self/cmdline, whole, then the launcher's SOURCESHARP_ARGS. Empty when
// neither can be read.
std::string ReadCmdline()
{
	std::string args;
	const int fd = open( "/proc/self/cmdline", O_RDONLY | O_CLOEXEC );
	if ( fd < 0 )
		return LaunchArgsBlock( getenv( kLaunchArgsVariable ) );

	char buf[ 4096 ];
	ssize_t n;
	while ( ( n = read( fd, buf, sizeof( buf ) ) ) > 0 )
		args.append( buf, (size_t)n );
	close( fd );
	return args + LaunchArgsBlock( getenv( kLaunchArgsVariable ) );
}

// The argument after the one at pArg, or NULL at the end of the block.
const char *NextArg( const char *pArgs, size_t nLen, const char *pArg )
{
	const char *pNext = pArg + strlen( pArg ) + 1;
	return pNext < pArgs + nLen ? pNext : nullptr;
}

const char *FindArg( const char *pArgs, size_t nLen, const char *pParam )
{
	if ( !pArgs || !pParam || !*pParam )
		return nullptr;

	for ( const char *pArg = nLen ? pArgs : nullptr; pArg; pArg = NextArg( pArgs, nLen, pArg ) )
	{
		if ( strcasecmp( pArg, pParam ) == 0 )
			return pArg;
	}
	return nullptr;
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void Copy( char *pOut, size_t nOut, const char *pText )
{
	if ( !pOut || !nOut )
		return;
	const size_t n = pText ? strnlen( pText, nOut - 1 ) : 0;
	if ( n )
		memcpy( pOut, pText, n );
	pOut[ n ] = 0;
}
#endif
}

const char kLaunchArgsVariable[] = "SOURCESHARP_ARGS";

std::string LaunchArgsBlock( const char *pValue )
{
	std::string block;
	for ( const char *p = pValue; p && *p; )
	{
		const char *pEnd = strchr( p, '\n' );
		const size_t n = pEnd ? (size_t)( pEnd - p ) : strlen( p );
		if ( n )
		{
			block.append( p, n );
			block += '\0';
		}
		p += n + ( pEnd ? 1 : 0 );
	}
	return block;
}

bool CommandLineHasIn( const char *pArgs, size_t nLen, const char *pParam )
{
	return FindArg( pArgs, nLen, pParam ) != nullptr;
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
bool CommandLineValueIn( const char *pArgs, size_t nLen, const char *pParam,
                         const char *pDefault, char *pOut, size_t nOut )
{
	const char *pArg = FindArg( pArgs, nLen, pParam );
	const char *pValue = pArg ? NextArg( pArgs, nLen, pArg ) : nullptr;
	if ( !pValue || pValue[0] == '-' || pValue[0] == '+' )
	{
		Copy( pOut, nOut, pDefault );
		return false;
	}

	Copy( pOut, nOut, pValue );
	return true;
}
#endif

bool CommandLineHas( const char *pParam )
{
	const std::string args = ReadCmdline();
	return CommandLineHasIn( args.data(), args.size(), pParam );
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
bool CommandLineValue( const char *pParam, const char *pDefault, char *pOut, size_t nOut )
{
	const std::string args = ReadCmdline();
	return CommandLineValueIn( args.data(), args.size(), pParam, pDefault, pOut, nOut );
}
#endif

} // namespace ss
