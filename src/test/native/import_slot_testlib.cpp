// A library that looks symbols up through its own import of dlsym, as
// engine.so resolves a module's factory: import_slot_test patches that import.
#include <dlfcn.h>

extern "C" __attribute__((visibility("default"))) void* ImportSlotTestLookup(void* hModule, const char* pszName)
{
	return dlsym(hModule, pszName);
}
