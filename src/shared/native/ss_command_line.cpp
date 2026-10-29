// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md

#include "ss_command_line.h"
#include "ss_platform.h"

#include <stdlib.h>
#include <string.h>

#include <string>

#if defined(_WIN32)
#include <shellapi.h>
#else
#include <fcntl.h>
#endif

namespace ss
{

namespace
{
// The process's own argv as one NUL-separated block: /proc/self/cmdline,
// whole, on Linux; dyld's copy of argv on macOS; the command line the
// process was created with, split the way the CRT splits it, on Windows.
// Then the launcher's SOURCESHARP_ARGS. Empty when neither can be read.
std::string ReadCmdline()
{
	std::string args;
#if defined(_WIN32)
	int nArgs = 0;
	LPWSTR* ppArgs = ::CommandLineToArgvW(::GetCommandLineW(), &nArgs);
	if (ppArgs)
	{
		for (int i = 0; i < nArgs; ++i)
		{
			const int n = ::WideCharToMultiByte(CP_UTF8, 0, ppArgs[i], -1, NULL, 0, NULL, NULL);
			if (n <= 0)
				continue;
			std::string arg((size_t)n, '\0');
			::WideCharToMultiByte(CP_UTF8, 0, ppArgs[i], -1, &arg[0], n, NULL, NULL);
			args += arg; // n counts the terminator, so the NUL comes with it
		}
		::LocalFree(ppArgs);
	}
#else
	const int fd = open("/proc/self/cmdline", O_RDONLY | O_CLOEXEC);
	if (fd < 0)
		return LaunchArgsBlock(getenv(kLaunchArgsVariable));

	char buf[4096];
	ssize_t n;
	while ((n = read(fd, buf, sizeof(buf))) > 0)
		args.append(buf, (size_t)n);
	close(fd);
#endif
	return args + LaunchArgsBlock(getenv(kLaunchArgsVariable));
}

// The argument after the one at pArg, or NULL at the end of the block.
const char* NextArg(const char* pArgs, size_t nLen, const char* pArg)
{
	const char* pNext = pArg + strlen(pArg) + 1;
	return pNext < pArgs + nLen ? pNext : nullptr;
}

const char* FindArg(const char* pArgs, size_t nLen, const char* pParam)
{
	if (!pArgs || !pParam || !*pParam)
		return nullptr;

	for (const char* pArg = nLen ? pArgs : nullptr; pArg; pArg = NextArg(pArgs, nLen, pArg))
	{
		if (ss_strcasecmp(pArg, pParam) == 0)
			return pArg;
	}
	return nullptr;
}

#ifdef CLIENT_DLL // server gc cull: server.so never reaches this
void Copy(char* pOut, size_t nOut, const char* pText)
{
	if (!pOut || !nOut)
		return;
	const size_t n = pText ? strnlen(pText, nOut - 1) : 0;
	if (n)
		memcpy(pOut, pText, n);
	pOut[n] = 0;
}
#endif
}

const char kLaunchArgsVariable[] = "SOURCESHARP_ARGS";

std::string LaunchArgsBlock(const char* pValue)
{
	std::string block;
	for (const char* p = pValue; p && *p;)
	{
		const char* pEnd = strchr(p, '\n');
		const size_t n = pEnd ? (size_t)(pEnd - p) : strlen(p);
		if (n)
		{
			block.append(p, n);
			block += '\0';
		}
		p += n + (pEnd ? 1 : 0);
	}
	return block;
}

bool CommandLineHasIn(const char* pArgs, size_t nLen, const char* pParam)
{
	return FindArg(pArgs, nLen, pParam) != nullptr;
}

#ifdef CLIENT_DLL // server gc cull: server.so never reaches this
bool CommandLineValueIn(const char* pArgs, size_t nLen, const char* pParam, const char* pDefault, char* pOut,
						size_t nOut)
{
	const char* pArg = FindArg(pArgs, nLen, pParam);
	const char* pValue = pArg ? NextArg(pArgs, nLen, pArg) : nullptr;
	if (!pValue || pValue[0] == '-' || pValue[0] == '+')
	{
		Copy(pOut, nOut, pDefault);
		return false;
	}

	Copy(pOut, nOut, pValue);
	return true;
}
#endif

bool CommandLineHas(const char* pParam)
{
	const std::string args = ReadCmdline();
	return CommandLineHasIn(args.data(), args.size(), pParam);
}

#ifdef CLIENT_DLL // server gc cull: server.so never reaches this
bool CommandLineValue(const char* pParam, const char* pDefault, char* pOut, size_t nOut)
{
	const std::string args = ReadCmdline();
	return CommandLineValueIn(args.data(), args.size(), pParam, pDefault, pOut, nOut);
}
#endif

} // namespace ss
