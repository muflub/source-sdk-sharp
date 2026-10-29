//========= SourceSharp — tests for the edict network serial rule ============//
//
// This one exists because of an ss_barrel that aborted the server.
//
// Two console commands on dm_lockdown — ent_fire logic_relay Trigger, then
// ent_create ss_barrel — killed the process with
//
//     SV_PackEntity: SnagChangeFrameList mismatched number of props [38 vs 14]
//
// The relay frees a prop_physics (tier 0: 32 bytes, 14 props), the barrel takes
// its edict (tier 2: 128 bytes, 38 props), and the engine packed the barrel as
// a continuation of the dead prop — reusing the 14-prop ChangeFrameList for a
// 38-prop SendTable — because nothing had told it the slot changed occupants.
// The thing that tells it is edict->m_NetworkSerialNumber, which THE GAME
// writes: stock's CBaseEntity::SetRefEHandle mirrors the entity-list handle
// serial into the edict on every assignment, and the parallel hierarchy's
// override stored the handle and mirrored nothing.
//
// The rule under test is that mirror, extracted engine-free into
// standalone_netserial.h. What these tests pin is the property the engine
// depends on — consecutive occupants of one slot publish DIFFERENT edict
// serials, so a model of GetPreviouslySentPacket's serial comparison refuses
// the stale packed entity — plus the mask, the null guard, and the short
// truncation, each of which is a way the mirror could regress to almost
// working.
//
// The live provocation that proved the failure: the two commands above, on a
// build without the mirror, abort the engine deterministically. With it, the
// barrel takes the freed edict and packs against a fresh ChangeFrameList.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/standalone_netserial.h"

namespace
{

// The edict as this rule sees it: one short. edict_t's field is a short too
// (edict.h), and the truncation test below is what notices if the template is
// ever handed something wider without the mask.
struct FakeEdict
{
	short m_NetworkSerialNumber;
};

//-----------------------------------------------------------------------------
// A model of the engine's half of the contract, small enough to be obviously
// right: CFrameSnapshotManager::GetPreviouslySentPacket returns the previous
// PackedEntity for an edict index only while the serial it recorded still
// equals the serial the edict publishes now. One comparison — this is it.
//-----------------------------------------------------------------------------
bool EngineWouldReusePackedEntity(int nRecordedSerial, const FakeEdict& edict)
{
	return nRecordedSerial == edict.m_NetworkSerialNumber;
}

//-----------------------------------------------------------------------------
// THE REGRESSION. The dm_lockdown pair: a prop_physics dies, an ss_barrel takes
// its edict. The entity list hands the new occupant the slot's serial plus one
// (CBaseEntityList::RemoveEntityAtSlot increments on removal), and the mirror
// is what carries that difference to where the engine can see it. Without the
// mirror both occupants publish the same leftover serial, the engine model
// reuses the dead occupant's packed entity, and that reuse IS the crash.
//-----------------------------------------------------------------------------
TEST(StandaloneNetSerial, AReusedEdictRefusesTheDeadOccupantsPackedEntity)
{
	FakeEdict edict = {-1};

	// prop_physics attaches: the entity list assigned serial 731 to this slot.
	StandaloneNetSerial_Mirror(&edict, 731ul);
	const int nRecorded = edict.m_NetworkSerialNumber;

	// The engine packs it and records the serial. While the same occupant
	// stands, reuse is correct — that is the fast path working as designed.
	EXPECT_TRUE(EngineWouldReusePackedEntity(nRecorded, edict));

	// prop_physics dies (the entity list bumps the slot serial), ss_barrel
	// attaches to the reused edict with the bumped serial.
	StandaloneNetSerial_Mirror(&edict, 732ul);

	// The stale packed entity must be refused: its ChangeFrameList is 14 props
	// and the new occupant's table is 38.
	EXPECT_FALSE(EngineWouldReusePackedEntity(nRecorded, edict));
}

//-----------------------------------------------------------------------------
// The same slot, occupied twice at the SAME tier. No abort without the mirror —
// the prop counts match — just a new entity silently packed as a delta against
// a dead one's state. The serial rule is what makes this case loud-by-absence
// too, and it is why the fix is the mirror rather than a clamp at the guard.
//-----------------------------------------------------------------------------
TEST(StandaloneNetSerial, SameTierReuseIsRefusedAsWell)
{
	FakeEdict edict = {-1};

	StandaloneNetSerial_Mirror(&edict, 40ul);
	const int nRecorded = edict.m_NetworkSerialNumber;

	StandaloneNetSerial_Mirror(&edict, 41ul);

	EXPECT_FALSE(EngineWouldReusePackedEntity(nRecorded, edict));
}

//-----------------------------------------------------------------------------
// The mask is stock's ten bits (NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS, and
// a COMPILE_TIME_ASSERT beside the caller pins the restated constant to the
// real one). The value the engine compares tomorrow must equal the value
// EHANDLE props put on the wire today, and both truncate to these bits.
//-----------------------------------------------------------------------------
TEST(StandaloneNetSerial, TheSerialIsMaskedToStocksTenBits)
{
	EXPECT_EQ(0, StandaloneNetSerial_ForHandle(0ul));
	EXPECT_EQ(1023, StandaloneNetSerial_ForHandle(1023ul));
	EXPECT_EQ(0, StandaloneNetSerial_ForHandle(1024ul));
	EXPECT_EQ(5, StandaloneNetSerial_ForHandle(1029ul));

	// The entity list's serials are sixteen bits and INVALID_EHANDLE's is all
	// ones; both must land inside the mask rather than sign-extend the short.
	EXPECT_EQ(1023, StandaloneNetSerial_ForHandle(0xFFFFFFFFul));
}

//-----------------------------------------------------------------------------
// A serial that wrapped the mask looks reused when it is not — 1024 removals
// on one slot between two packs of it. Stock has the same window and it has
// never been the bug; what the test pins is that the wrap is the MASK's, i.e.
// consecutive serials stay distinct everywhere except the wrap itself.
//-----------------------------------------------------------------------------
TEST(StandaloneNetSerial, ConsecutiveSerialsStayDistinctUnderTheMask)
{
	for (unsigned long i = 0; i < 2048; i++)
	{
		EXPECT_NE(StandaloneNetSerial_ForHandle(i), StandaloneNetSerial_ForHandle(i + 1));
	}
}

//-----------------------------------------------------------------------------
// NULL is the server-only entity: no edict, nothing to publish, and the one
// call site runs for those too. Not crashing is the whole assertion.
//-----------------------------------------------------------------------------
TEST(StandaloneNetSerial, AServerOnlyEntityHasNoEdictToMirrorInto)
{
	StandaloneNetSerial_Mirror((FakeEdict*)0, 731ul);
}

//-----------------------------------------------------------------------------
// The write is a short's worth, through the mask — an edict field is a short
// (edict.h) and a wider store would be a different bug wearing this one's
// clothes. Ten masked bits always fit; the test is that the STORED value is
// the masked one, bit-exact, at the mask's edges.
//-----------------------------------------------------------------------------
TEST(StandaloneNetSerial, TheStoredValueIsTheMaskedValue)
{
	FakeEdict edict = {-1};

	StandaloneNetSerial_Mirror(&edict, 0xFFFFFFFFul);
	EXPECT_EQ(1023, edict.m_NetworkSerialNumber);

	StandaloneNetSerial_Mirror(&edict, 1024ul);
	EXPECT_EQ(0, edict.m_NetworkSerialNumber);
}

} // namespace
