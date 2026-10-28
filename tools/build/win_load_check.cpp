// tools/windows-cross's load check: LoadLibrary each DLL named on the command
// line (an absolute or relative path; its directory goes on the DLL search
// path first, so its siblings resolve as they do beside the engine), then
// GetProcAddress the entry point named after a '=' (dll=Symbol). Exit 0 when
// every DLL loads and every named entry point resolves; 1 otherwise, naming
// the first failure and its Win32 error.
#include <windows.h>
#include <stdio.h>
#include <string.h>

int main( int argc, char **argv )
{
	int failures = 0;
	for ( int i = 1; i < argc; i++ )
	{
		char path[MAX_PATH];
		strncpy( path, argv[i], sizeof( path ) - 1 );
		path[sizeof( path ) - 1] = 0;
		char *symbol = strchr( path, '=' );
		if ( symbol )
			*symbol++ = 0;
		char dir[MAX_PATH];
		strcpy( dir, path );
		char *slash = strrchr( dir, '\\' );
		if ( !slash )
			slash = strrchr( dir, '/' );
		if ( slash )
		{
			*slash = 0;
			SetDllDirectoryA( dir );
		}
		HMODULE module = LoadLibraryA( path );
		if ( !module )
		{
			printf( "FAIL load %s: error %lu\n", path, GetLastError() );
			failures++;
			continue;
		}
		if ( symbol && !GetProcAddress( module, symbol ) )
		{
			printf( "FAIL %s has no export %s: error %lu\n", path, symbol, GetLastError() );
			failures++;
			continue;
		}
		printf( "ok   %s%s%s\n", path, symbol ? " exports " : "", symbol ? symbol : "" );
	}
	return failures ? 1 : 0;
}
