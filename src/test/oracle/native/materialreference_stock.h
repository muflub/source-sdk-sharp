// Upstream's MaterialSystemUtil.h a second time, with its two classes renamed,
// for materialreference_oracle_test: the header is re-read past its guard with
// the names defined to Stock*, so the test can hold upstream's classes and
// ours in one binary. materialreference_stock.cpp compiles upstream's bodies
// under the same names.
#pragma once

#include "materialsystem/MaterialSystemUtil.h"   // ours first: its guard is set

#undef MATERIALSYSTEMUTIL_H
#define CMaterialReference StockMaterialReference
#define CTextureReference StockTextureReference
#include "materialsystem/MaterialSystemUtil.h"
#undef CMaterialReference
#undef CTextureReference
