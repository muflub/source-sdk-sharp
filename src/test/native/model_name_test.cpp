//========= SourceSharp — tests for "is this the map's own model?" ==========//
//
// The rule decides whether a model name is LOOKED UP in the level's string
// table or PRECACHED from disk, and getting it wrong is silent in the way this
// codebase keeps meeting: both arms answer -1 for a name they cannot resolve,
// so a brush sent to the precache arm and a studio model sent to the lookup arm
// report the identical "no model" and neither says which arm it took.
//
// Both hierarchies need the rule — CStandaloneEntity::SetModelName has always
// had it, EntityInitPhysics needed it the moment the SDK arm started applying a
// recorded model name — and a comparison written down twice is a comparison
// that drifts. It is one function now, and this is what it promises.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/shell_model_name.h"

namespace
{

//-----------------------------------------------------------------------------
// The map's own submodels, as vbsp numbers them and as every brush entity in a
// .vmf carries them.
//-----------------------------------------------------------------------------
TEST(ModelName, AStarNumberIsTheMapsOwnModel)
{
	EXPECT_TRUE(SourceSharp_IsBrushModelName("*23"));
	EXPECT_TRUE(SourceSharp_IsBrushModelName("*0"));
	EXPECT_TRUE(SourceSharp_IsBrushModelName("*127"));
}

//-----------------------------------------------------------------------------
// And a path is a file, which has to be precached before it has an index.
//-----------------------------------------------------------------------------
TEST(ModelName, APathIsAFileOnDisk)
{
	EXPECT_FALSE(SourceSharp_IsBrushModelName("models/props_junk/wood_crate001a.mdl"));
	EXPECT_FALSE(SourceSharp_IsBrushModelName("models/error.mdl"));
}

//-----------------------------------------------------------------------------
// The star has to be FIRST. A path with one in the middle is still a path, and
// a rule written as "contains a star" would send it to the level's string table
// and report a missing model for a file that is sitting right there.
//-----------------------------------------------------------------------------
TEST(ModelName, OnlyALeadingStarCounts)
{
	EXPECT_FALSE(SourceSharp_IsBrushModelName("models/*23"));
	EXPECT_FALSE(SourceSharp_IsBrushModelName(" *23"));
}

//-----------------------------------------------------------------------------
// Neither of the two empties is a brush. An entity with no model reaches the
// precache arm, which is the one that can name what it failed to load.
//-----------------------------------------------------------------------------
TEST(ModelName, NothingIsNotABrushModel)
{
	EXPECT_FALSE(SourceSharp_IsBrushModelName(""));
	EXPECT_FALSE(SourceSharp_IsBrushModelName(0));
}

//-----------------------------------------------------------------------------
// A bare star is a brush name as far as this rule goes. It is not a valid one —
// the lookup will answer -1 and the caller will report it — but that is the
// lookup's answer to give, and treating it as a file instead would send the
// engine looking for a model called "*".
//-----------------------------------------------------------------------------
TEST(ModelName, ABareStarStillTakesTheLookupArm)
{
	EXPECT_TRUE(SourceSharp_IsBrushModelName("*"));
}

} // namespace
