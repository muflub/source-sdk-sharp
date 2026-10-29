// Forced into every native test under MSVC (cmake/SourceSharpTests.cmake).
// The POSIX spellings the tests use for the file system, the environment and
// temporary paths, on the Windows CRT, so a test about this tree's sources or
// data runs the same on both platforms. A test whose subject is POSIX itself
// (fork, dlopen, ELF, /proc, nm) is LINUX_ONLY instead and never sees this.
#pragma once

#ifdef _MSC_VER

#include <direct.h>
#include <io.h>
#include <process.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>

#ifndef S_ISDIR
#define S_ISDIR(m) (((m) & _S_IFMT) == _S_IFDIR)
#endif
#ifndef S_ISREG
#define S_ISREG(m) (((m) & _S_IFMT) == _S_IFREG)
#endif
#ifndef S_IXUSR
#define S_IXUSR _S_IEXEC
#endif
#ifndef PATH_MAX
#define PATH_MAX _MAX_PATH
#endif
#include <stdint.h>
typedef intptr_t ssize_t;

inline int setenv(const char* pszName, const char* pszValue, int bOverwrite)
{
	if (!bOverwrite && getenv(pszName))
		return 0;
	return _putenv_s(pszName, pszValue) == 0 ? 0 : -1;
}

inline int unsetenv(const char* pszName)
{
	return _putenv_s(pszName, "") == 0 ? 0 : -1;
}

// mkdir(path, mode): the mode has no meaning to the CRT's _mkdir.
inline int ss_posix_mkdir(const char* pszPath, int)
{
	return _mkdir(pszPath);
}

#define mkdir(path, mode) ss_posix_mkdir(path, mode)

// mkdtemp: the template's trailing XXXXXX made unique, then the directory.
inline char* mkdtemp(char* pszTemplate)
{
	if (_mktemp_s(pszTemplate, strlen(pszTemplate) + 1) != 0)
		return NULL;
	return _mkdir(pszTemplate) == 0 ? pszTemplate : NULL;
}

inline char* realpath(const char* pszPath, char* pszResolved)
{
	return _fullpath(pszResolved, pszPath, _MAX_PATH);
}

#define getpid _getpid
#define getcwd _getcwd
#define unlink _unlink
#define rmdir _rmdir
#define chdir _chdir
#define access _access
#define popen _popen
#define pclose _pclose

#endif // _MSC_VER
