// Finds the slot through which a loaded module calls an imported function, and
// writes it: the GameUI factory swap (plan_gameui_managed.md G4) changes one
// slot of engine's import of the symbol lookup for one lookup. Linux reads the
// module's PLT and GOT relocations (R_X86_64_JUMP_SLOT, R_X86_64_GLOB_DAT);
// Windows its import address table. The store lifts the page's protection
// when the slot is read-only (RELRO, a protected IAT) and puts it back.
#pragma once

#include <stddef.h>
#include <stdint.h>
#include <string.h>

#include "vtable_slot_patch.h"

#if defined(_WIN32)
#include <windows.h>
#else
#include <elf.h>
#include <link.h>
#endif

namespace ImportSlot
{

// Whether pszText ends in pszSuffix (a module path ends in its file name).
inline bool EndsWith(const char* pszText, const char* pszSuffix)
{
	if (!pszText || !pszSuffix)
		return false;
	const size_t nText = strlen(pszText), nSuffix = strlen(pszSuffix);
	return nText >= nSuffix && strcmp(pszText + nText - nSuffix, pszSuffix) == 0;
}

#if !defined(_WIN32)

struct Search
{
	const char* pszModule;
	const char* pszSymbol;
	void** pSlot;
};

// A dynamic entry's address: glibc relocates some in place, so one already
// above the load base is taken as it is.
inline uintptr_t DynamicAddress(uintptr_t base, uintptr_t value)
{
	return value < base ? base + value : value;
}

inline int FindInObject(struct dl_phdr_info* pInfo, size_t, void* pData)
{
	Search* pSearch = static_cast<Search*>(pData);
	if (!EndsWith(pInfo->dlpi_name, pSearch->pszModule))
		return 0;
	const uintptr_t base = pInfo->dlpi_addr;
	const ElfW(Dyn)* pDynamic = nullptr;
	for (int i = 0; i < pInfo->dlpi_phnum; i++)
		if (pInfo->dlpi_phdr[i].p_type == PT_DYNAMIC)
			pDynamic = reinterpret_cast<const ElfW(Dyn)*>(base + pInfo->dlpi_phdr[i].p_vaddr);
	if (!pDynamic)
		return 0;

	uintptr_t jmprel = 0, rela = 0, symtab = 0, strtab = 0;
	size_t jmprelSize = 0, relaSize = 0;
	for (const ElfW(Dyn)* d = pDynamic; d->d_tag != DT_NULL; d++)
	{
		switch (d->d_tag)
		{
			case DT_JMPREL:
				jmprel = DynamicAddress(base, d->d_un.d_ptr);
				break;
			case DT_PLTRELSZ:
				jmprelSize = d->d_un.d_val;
				break;
			case DT_RELA:
				rela = DynamicAddress(base, d->d_un.d_ptr);
				break;
			case DT_RELASZ:
				relaSize = d->d_un.d_val;
				break;
			case DT_SYMTAB:
				symtab = DynamicAddress(base, d->d_un.d_ptr);
				break;
			case DT_STRTAB:
				strtab = DynamicAddress(base, d->d_un.d_ptr);
				break;
			default:
				break;
		}
	}
	if (!symtab || !strtab)
		return 0;

	const ElfW(Sym)* pSymbols = reinterpret_cast<const ElfW(Sym)*>(symtab);
	const char* pStrings = reinterpret_cast<const char*>(strtab);
	const uintptr_t tables[2] = {jmprel, rela};
	const size_t sizes[2] = {jmprelSize, relaSize};
	for (int t = 0; t < 2; t++)
	{
		const ElfW(Rela)* pRela = reinterpret_cast<const ElfW(Rela)*>(tables[t]);
		for (size_t i = 0; pRela && i < sizes[t] / sizeof(ElfW(Rela)); i++)
		{
			const unsigned type = ELF64_R_TYPE(pRela[i].r_info);
			if (type != R_X86_64_JUMP_SLOT && type != R_X86_64_GLOB_DAT)
				continue;
			if (strcmp(pStrings + pSymbols[ELF64_R_SYM(pRela[i].r_info)].st_name, pSearch->pszSymbol) == 0)
			{
				pSearch->pSlot = reinterpret_cast<void**>(base + pRela[i].r_offset);
				return 1;
			}
		}
	}
	return 1;
}

// The slot through which the loaded module whose path ends in pszModule calls
// pszSymbol, or null when no such module or import is found.
inline void** Find(const char* pszModule, const char* pszSymbol)
{
	Search search = {pszModule, pszSymbol, nullptr};
	dl_iterate_phdr(FindInObject, &search);
	return search.pSlot;
}

#else

// The import address table entry through which the loaded module pszModule
// calls pszSymbol (any DLL's export of that name), or null.
inline void** Find(const char* pszModule, const char* pszSymbol)
{
	HMODULE hModule = GetModuleHandleA(pszModule);
	if (!hModule)
		return nullptr;
	const BYTE* pBase = reinterpret_cast<const BYTE*>(hModule);
	const IMAGE_DOS_HEADER* pDos = reinterpret_cast<const IMAGE_DOS_HEADER*>(pBase);
	const IMAGE_NT_HEADERS* pNt = reinterpret_cast<const IMAGE_NT_HEADERS*>(pBase + pDos->e_lfanew);
	const IMAGE_DATA_DIRECTORY& dir = pNt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
	if (!dir.VirtualAddress)
		return nullptr;
	for (const IMAGE_IMPORT_DESCRIPTOR* pImport =
			 reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(pBase + dir.VirtualAddress);
		 pImport->Name; pImport++)
	{
		const IMAGE_THUNK_DATA* pNames = reinterpret_cast<const IMAGE_THUNK_DATA*>(pBase + pImport->OriginalFirstThunk);
		IMAGE_THUNK_DATA* pAddresses =
			reinterpret_cast<IMAGE_THUNK_DATA*>(const_cast<BYTE*>(pBase) + pImport->FirstThunk);
		for (; pNames->u1.AddressOfData; pNames++, pAddresses++)
		{
			if (IMAGE_SNAP_BY_ORDINAL(pNames->u1.Ordinal))
				continue;
			const IMAGE_IMPORT_BY_NAME* pName =
				reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(pBase + pNames->u1.AddressOfData);
			if (strcmp(reinterpret_cast<const char*>(pName->Name), pszSymbol) == 0)
				return reinterpret_cast<void**>(&pAddresses->u1.Function);
		}
	}
	return nullptr;
}

#endif

// Stores pValue in the slot, lifting the page's protection for the store when
// it is read-only and putting it back after. False when the page is unmapped
// or could not be made writable (nothing is stored then).
inline bool Write(void** pSlot, void* pValue)
{
	const int nProtection = VTableSlotPatch::PageProtection(pSlot);
	if (nProtection < 0)
		return false;
	if (nProtection & SS_PAGE_WRITE)
	{
		*pSlot = pValue;
		return true;
	}
	return VTableSlotPatch::WriteSlot(pSlot, pValue, nProtection);
}

} // namespace ImportSlot
