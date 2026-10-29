// The native tests' <dirent.h> under MSVC (test builds only: cmake/
// SourceSharpTests.cmake puts this directory on their include path). The
// three calls the tests make, opendir/readdir/closedir, over FindFirstFile;
// d_name is the one field they read. "." and ".." are returned as POSIX
// returns them, so a walker that skips them works unchanged.
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <stdlib.h>
#include <string.h>

// d_type as POSIX spells it: a directory or a regular file, which is all
// the tests ask.
enum
{
	DT_UNKNOWN = 0,
	DT_DIR = 4,
	DT_REG = 8
};

struct dirent
{
	unsigned char d_type;
	char d_name[MAX_PATH];
};

struct DIR
{
	HANDLE hFind;
	WIN32_FIND_DATAA data;
	bool bFirst;
	dirent entry;
};

inline DIR* opendir(const char* pszPath)
{
	if (!pszPath || !*pszPath)
		return NULL;
	char szPattern[MAX_PATH];
	size_t n = strlen(pszPath);
	if (n + 3 > sizeof(szPattern))
		return NULL;
	memcpy(szPattern, pszPath, n);
	if (pszPath[n - 1] != '/' && pszPath[n - 1] != '\\')
		szPattern[n++] = '/';
	szPattern[n++] = '*';
	szPattern[n] = '\0';
	DIR* pDir = (DIR*)calloc(1, sizeof(DIR));
	if (!pDir)
		return NULL;
	pDir->hFind = FindFirstFileA(szPattern, &pDir->data);
	if (pDir->hFind == INVALID_HANDLE_VALUE)
	{
		free(pDir);
		return NULL;
	}
	pDir->bFirst = true;
	return pDir;
}

inline dirent* readdir(DIR* pDir)
{
	if (!pDir)
		return NULL;
	if (!pDir->bFirst && !FindNextFileA(pDir->hFind, &pDir->data))
		return NULL;
	pDir->bFirst = false;
	pDir->entry.d_type = (pDir->data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ? DT_DIR : DT_REG;
	strncpy(pDir->entry.d_name, pDir->data.cFileName, sizeof(pDir->entry.d_name) - 1);
	pDir->entry.d_name[sizeof(pDir->entry.d_name) - 1] = '\0';
	return &pDir->entry;
}

inline int closedir(DIR* pDir)
{
	if (!pDir)
		return -1;
	FindClose(pDir->hFind);
	free(pDir);
	return 0;
}
