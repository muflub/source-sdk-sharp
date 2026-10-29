// The per-entity side table of data objects, keyed by an opaque pointer.
#pragma once

// Whether a data object of `type` (shareddefs.h's GROUNDLINK..VPHYSICSWATCHER) hangs
// off pKey. Out-of-range types assert and answer false.
bool EntityDataObject_Has(int type, const void* pKey);
