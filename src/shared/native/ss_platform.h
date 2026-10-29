// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// The platform calls our native code makes that have no single spelling across Linux,
// Windows and macOS. System headers only, so it can be tested free of Source headers.
#pragma once

#include <stdlib.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <intrin.h>
#include <process.h>
#include <direct.h>
// <windows.h> macros rename methods such as GetClassName and CreateEvent in
// every later declaration; this header is included by headers, so it must not
// leak them. Win32 code names the explicit ...A functions.
#undef GetClassName
#undef GetProp
#undef SetProp
#undef GetObject
#undef CreateEvent
#undef GetCurrentTime
#undef PostMessage
#undef SendMessage
#undef CreateFont
#undef DrawText
#undef GetMessage
#undef CopyFile
#undef DeleteFile
#else
#include <dlfcn.h>
#include <execinfo.h>
#include <sys/mman.h>
#include <unistd.h>
#endif

// The caller's return address. Level 0 only.
#ifdef _MSC_VER
#define SS_RETURN_ADDRESS() _ReturnAddress()
#else
#define SS_RETURN_ADDRESS() __builtin_return_address(0)
#endif

#ifdef _WIN32
#define ss_strcasecmp _stricmp
#else
#include <strings.h>
#define ss_strcasecmp strcasecmp
#endif

// The module that holds an address: its load base, its path ("" when unknown) and the
// nearest exported symbol at or before it (NULL when unknown, always on Windows).
// False when no loaded module holds the address.
struct SSModuleInfo
{
	const void* pBase;
	const char* pszSymbol;
	char szPath[1024];
};

inline bool SS_ModuleOfAddress(const void* pAddress, SSModuleInfo* pInfo)
{
	if (!pInfo)
		return false;
	pInfo->pBase = NULL;
	pInfo->pszSymbol = NULL;
	pInfo->szPath[0] = '\0';
	if (!pAddress)
		return false;
#ifdef _WIN32
	HMODULE hModule = NULL;
	if (!::GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
							  (LPCSTR)pAddress, &hModule) ||
		!hModule)
		return false;
	pInfo->pBase = (const void*)hModule;
	if (::GetModuleFileNameA(hModule, pInfo->szPath, (DWORD)sizeof(pInfo->szPath)) == 0)
		pInfo->szPath[0] = '\0';
	return true;
#else
	Dl_info info;
	if (!dladdr(pAddress, &info) || !info.dli_fbase)
		return false;
	pInfo->pBase = info.dli_fbase;
	pInfo->pszSymbol = info.dli_sname;
	if (info.dli_fname)
	{
		strncpy(pInfo->szPath, info.dli_fname, sizeof(pInfo->szPath) - 1);
		pInfo->szPath[sizeof(pInfo->szPath) - 1] = '\0';
	}
	return true;
#endif
}

// The file name part of a module path; Windows paths may use either separator.
inline const char* SS_ModuleBaseName(const char* pszPath)
{
	const char* pszName = strrchr(pszPath, '/');
#ifdef _WIN32
	const char* pszBack = strrchr(pszPath, '\\');
	if (pszBack && (!pszName || pszBack > pszName))
		pszName = pszBack;
#endif
	return pszName ? pszName + 1 : pszPath;
}

// Up to nMax return addresses of the calling thread's stack, innermost first; returns how many.
inline int SS_Backtrace(void** pFrames, int nMax)
{
	if (!pFrames || nMax <= 0)
		return 0;
#ifdef _WIN32
	return (int)::CaptureStackBackTrace(0, (DWORD)nMax, pFrames, NULL);
#else
	return backtrace(pFrames, nMax);
#endif
}

// Protection bits are mprotect's (read 1, write 2, execute 4) on every platform;
// Windows' PAGE_* constants are translated at the edge.
enum
{
	SS_PAGE_READ = 1,
	SS_PAGE_WRITE = 2,
	SS_PAGE_EXEC = 4,
};

inline size_t SS_PageSize()
{
#ifdef _WIN32
	SYSTEM_INFO si;
	::GetSystemInfo(&si);
	return (size_t)si.dwPageSize;
#else
	const long n = sysconf(_SC_PAGESIZE);
	return n > 0 ? (size_t)n : 4096;
#endif
}

#ifdef _WIN32
inline int SS_PageBitsFromWindows(DWORD dwProtect)
{
	switch (dwProtect & 0xFF)
	{
		case PAGE_READONLY:
			return SS_PAGE_READ;
		case PAGE_READWRITE:
		case PAGE_WRITECOPY:
			return SS_PAGE_READ | SS_PAGE_WRITE;
		case PAGE_EXECUTE:
			return SS_PAGE_EXEC;
		case PAGE_EXECUTE_READ:
			return SS_PAGE_READ | SS_PAGE_EXEC;
		case PAGE_EXECUTE_READWRITE:
		case PAGE_EXECUTE_WRITECOPY:
			return SS_PAGE_READ | SS_PAGE_WRITE | SS_PAGE_EXEC;
		default:
			return 0;
	}
}

inline DWORD SS_PageBitsToWindows(int nBits)
{
	const bool bExec = (nBits & SS_PAGE_EXEC) != 0;
	if (nBits & SS_PAGE_WRITE)
		return bExec ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE;
	if (nBits & SS_PAGE_READ)
		return bExec ? PAGE_EXECUTE_READ : PAGE_READONLY;
	return bExec ? PAGE_EXECUTE : PAGE_NOACCESS;
}
#endif

// The protection of the mapping that holds pAddress, or -1 when no mapping does.
inline int SS_PageProtection(const void* pAddress)
{
#if defined(_WIN32)
	MEMORY_BASIC_INFORMATION mbi;
	if (::VirtualQuery(pAddress, &mbi, sizeof(mbi)) == 0 || mbi.State != MEM_COMMIT)
		return -1;
	return SS_PageBitsFromWindows(mbi.Protect);
#else
	FILE* pMaps = fopen("/proc/self/maps", "r");
	if (!pMaps)
		return -1;
	const uintptr_t address = (uintptr_t)pAddress;
	int nProtection = -1;
	char szLine[512];
	while (fgets(szLine, sizeof(szLine), pMaps))
	{
		unsigned long lo = 0, hi = 0;
		char szPerms[8] = {0};
		if (sscanf(szLine, "%lx-%lx %7s", &lo, &hi, szPerms) != 3)
			continue;
		if (address < lo || address >= hi)
			continue;
		nProtection = 0;
		if (szPerms[0] == 'r')
			nProtection |= SS_PAGE_READ;
		if (szPerms[1] == 'w')
			nProtection |= SS_PAGE_WRITE;
		if (szPerms[2] == 'x')
			nProtection |= SS_PAGE_EXEC;
		break;
	}
	fclose(pMaps);
	return nProtection;
#endif
}

// Sets the protection of the page holding pAddress. True on success.
inline bool SS_PageProtect(const void* pAddress, int nProtection)
{
	const size_t nPage = SS_PageSize();
	const uintptr_t page = (uintptr_t)pAddress & ~(uintptr_t)(nPage - 1);
#ifdef _WIN32
	DWORD dwOld = 0;
	return ::VirtualProtect((LPVOID)page, nPage, SS_PageBitsToWindows(nProtection), &dwOld) != 0;
#else
	int nProt = 0;
	if (nProtection & SS_PAGE_READ)
		nProt |= PROT_READ;
	if (nProtection & SS_PAGE_WRITE)
		nProt |= PROT_WRITE;
	if (nProtection & SS_PAGE_EXEC)
		nProt |= PROT_EXEC;
	return mprotect((void*)page, nPage, nProt) == 0;
#endif
}

// The process id, and the working directory (NULL when it does not fit the buffer).
inline int SS_GetPid()
{
#ifdef _WIN32
	return (int)::GetCurrentProcessId();
#else
	return (int)getpid();
#endif
}

// setenv and unsetenv. On Windows a variable set to the empty string is removed.
inline int SS_SetEnv(const char* pszName, const char* pszValue, int bOverwrite)
{
#ifdef _WIN32
	if (!bOverwrite && getenv(pszName))
		return 0;
	return _putenv_s(pszName, pszValue) == 0 ? 0 : -1;
#else
	return setenv(pszName, pszValue, bOverwrite);
#endif
}

inline int SS_UnsetEnv(const char* pszName)
{
#ifdef _WIN32
	return _putenv_s(pszName, "") == 0 ? 0 : -1;
#else
	return unsetenv(pszName);
#endif
}

// SS_BIND_IMPORT(name): on Windows, defines the __imp_<name> import pointer for a
// function this module defines but the engine's headers declare dllimport, so calls
// bind locally rather than to tier0.lib or vstdlib.lib. C linkage only; use at file
// scope after the definition, with C4273 disabled. A no-op on Linux.
#ifdef _WIN32
#define SS_BIND_IMPORT(name) extern "C" const void* const __imp_##name = (const void*)&name;
// The same for an overloaded name: Type is the C-linkage overload's
// function-pointer type, which picks it.
#define SS_BIND_IMPORT_AS(name, Type) extern "C" const void* const __imp_##name = (const void*)static_cast<Type>(&name);
#else
#define SS_BIND_IMPORT(name)
#define SS_BIND_IMPORT_AS(name, Type)
#endif

inline char* SS_GetCwd(char* pOut, size_t nOut)
{
#ifdef _WIN32
	return _getcwd(pOut, (int)nOut);
#else
	return getcwd(pOut, nOut);
#endif
}
