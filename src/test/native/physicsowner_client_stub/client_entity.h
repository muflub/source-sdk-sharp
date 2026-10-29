// A stand-in for the client's client_entity.h, for physicsowner_client_test:
// physicsowner.h's client arm includes the header and names nothing in it,
// so the test compiles the client arm without the client's entity class. What
// the real one brings in that the arm does use comes in here: the entity
// interface it calls through, and the masks.
#pragma once
#include "const.h"
#include "bspflags.h"
#include "mathlib/mathlib.h"
#include "igameentity.h"
