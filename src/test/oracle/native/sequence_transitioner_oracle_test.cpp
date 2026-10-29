//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// Pristine game/shared/sequence_Transitioner.cpp (CSequenceTransitioner), on
// the client's pristine C_AnimationLayer (game/client/animationlayer.h, its
// GetFadeout) and pristine public/studio.cpp's CStudioHdr, run over a script
// of sequence changes: changes, restarts, a STUDIO_SNAP sequence, no-interp
// frames, fades running out (trims), a clock running backwards and
// RemoveAll, then a seeded random run of the same operations. After every
// operation the queue is written out (each entry's sequence, cycle, rate,
// weight, animtime and fade time, the floats as their bits), recorded as the
// golden src/test/oracle/managed/Fixtures/sequence-transitioner-stock.tsv
// that SequenceTransitionerOracleTests replays against the managed
// SequenceTransitioner (src/shared/managed/Bones/SequenceTransitioner.cs).
//
// This test asserts stock still produces exactly the golden; it changes only
// when the script does, and then on purpose (touch
// sequence_transitioner_golden.regen beside this file, run, delete the
// marker, and say why in the commit).
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cbase.h"
#include "sequence_Transitioner.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

// studio.cpp's activity table reaches the game's SetActivityForSequence, and
// only from CStudioHdr::CActivityToSequenceMapping::Initialize, which nothing
// in this script runs (the table is lazy). Linked so studio.cpp links; reached,
// it fails the test.
void SetActivityForSequence(CStudioHdr*, int)
{
	ADD_FAILURE() << "the script reached the activity table";
}

// The model cache's three answers (game/shared/studio_shared.cpp asks
// modelinfo). The script's model has no includes and no anim blocks, so
// studio.cpp never asks; reached, they fail the test.
virtualmodel_t* studiohdr_t::GetVirtualModel(void) const
{
	ADD_FAILURE() << "the script's model asked for a virtual model";
	return NULL;
}

const studiohdr_t* virtualgroup_t::GetStudioHdr(void) const
{
	ADD_FAILURE() << "the script's model asked for an include";
	return NULL;
}

byte* studiohdr_t::GetAnimBlock(int) const
{
	ADD_FAILURE() << "the script's model asked for an anim block";
	return NULL;
}

namespace
{
// The model: five sequences whose fades differ, so each of
// CheckForSequenceChange's fade-time rules picks a different value.
struct SeqSpec
{
	const char* pszLabel;
	int nFlags;
	float flFadeIn;
	float flFadeOut;
};

const SeqSpec kSequences[] = {
	{"ramp", STUDIO_LOOPING, 0.4f, 0.3f},
	{"steep", STUDIO_LOOPING, 0.25f, 0.2f},
	{"snap", STUDIO_LOOPING | STUDIO_SNAP, 0.2f, 0.2f},
	{"once", 0, 0.4f, 0.2f},
	{"instant", STUDIO_LOOPING, 0.0f, 0.0f},
};
const int kNumSequences = (int)(sizeof(kSequences) / sizeof(kSequences[0]));

// A studiohdr_t with only its local sequence table: no bones, no includes.
std::vector<unsigned char> BuildModel()
{
	const int nSeqBase = (int)sizeof(studiohdr_t);
	const int nLabelBase = nSeqBase + kNumSequences * (int)sizeof(mstudioseqdesc_t);
	std::vector<unsigned char> image(nLabelBase + 256, 0);

	studiohdr_t* pHdr = (studiohdr_t*)image.data();
	pHdr->length = (int)image.size();
	pHdr->numlocalseq = kNumSequences;
	pHdr->localseqindex = nSeqBase;

	int nLabel = nLabelBase;
	for (int i = 0; i < kNumSequences; i++)
	{
		mstudioseqdesc_t* pSeq = pHdr->pLocalSeqdesc(i);
		const int nSeqAt = nSeqBase + i * (int)sizeof(mstudioseqdesc_t);
		pSeq->baseptr = -nSeqAt;
		pSeq->szlabelindex = nLabel - nSeqAt;
		pSeq->szactivitynameindex = nLabel + (int)strlen(kSequences[i].pszLabel) - nSeqAt; // its terminator: ""
		strcpy((char*)image.data() + nLabel, kSequences[i].pszLabel);
		nLabel += (int)strlen(kSequences[i].pszLabel) + 1;
		pSeq->flags = kSequences[i].nFlags;
		pSeq->fadeintime = kSequences[i].flFadeIn;
		pSeq->fadeouttime = kSequences[i].flFadeOut;
	}
	return image;
}

std::string Bits(float f)
{
	uint32_t u;
	memcpy(&u, &f, 4);
	char b[16];
	snprintf(b, sizeof(b), "%08x", u);
	return b;
}

// %.9g round-trips a float, so the managed replay parses the same value.
std::string Num(float f)
{
	char b[32];
	snprintf(b, sizeof(b), "%.9g", f);
	return b;
}

std::string Queue(const CSequenceTransitioner& t)
{
	std::string out = std::to_string(t.m_animationQueue.Count());
	for (int i = 0; i < t.m_animationQueue.Count(); i++)
	{
		const C_AnimationLayer& l = t.m_animationQueue[i];
		out += "\t" + std::to_string((int)l.m_nSequence) + ":" + Bits(l.m_flCycle) + ":" + Bits(l.m_flPlaybackRate) +
			":" + Bits(l.m_flWeight) + ":" + Bits(l.m_flLayerAnimtime) + ":" + Bits(l.m_flLayerFadeOuttime);
	}
	return out;
}

struct Rng
{
	uint32_t s = 0x9E3779B9u;

	uint32_t Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	int Pick(int lo, int hi) { return lo + (int)(Next() % (uint32_t)(hi - lo + 1)); }

	bool Chance(int nPercent) { return Pick(0, 99) < nPercent; }
};

// The script runner: each operation is applied to stock and written out as
// `step \t op \t args \t queue`.
class CScript
{
public:
	explicit CScript(CStudioHdr* pHdr)
		: m_pHdr(pHdr)
	{
		Reset();
	}

	void Reset()
	{
		m_pT.reset(new CSequenceTransitioner);
		Line("reset", "");
	}

	void Check(int nSeq, bool bForce, bool bInterpolate)
	{
		m_pT->CheckForSequenceChange(m_pHdr, nSeq, bForce, bInterpolate);
		Line("check", std::to_string(nSeq) + " " + (bForce ? "1" : "0") + " " + (bInterpolate ? "1" : "0"));
	}

	void Update(int nSeq, float flCycle, float flRate, float flTime)
	{
		m_pT->UpdateCurrent(m_pHdr, nSeq, flCycle, flRate, flTime);
		Line("update", std::to_string(nSeq) + " " + Num(flCycle) + " " + Num(flRate) + " " + Num(flTime));
	}

	void RemoveAll()
	{
		m_pT->RemoveAll();
		Line("removeall", "");
	}

	// One StandardBlendingRules' worth: the change check, then the update.
	void Step(int nSeq, float flTime, bool bForce = false, bool bInterpolate = true, float flCycle = 0.25f,
			  float flRate = 1.0f)
	{
		Check(nSeq, bForce, bInterpolate);
		Update(nSeq, flCycle, flRate, flTime);
	}

	std::string m_Out;

private:
	void Line(const char* pszOp, const std::string& args)
	{
		m_Out += std::to_string(m_nStep++) + "\t" + pszOp + "\t" + args + "\t" + Queue(*m_pT) + "\n";
	}

	CStudioHdr* m_pHdr;
	std::unique_ptr<CSequenceTransitioner> m_pT;
	int m_nStep = 0;
};

std::string RunScenario()
{
	std::vector<unsigned char> image = BuildModel();
	CStudioHdr hdr((const studiohdr_t*)image.data(), NULL);

	std::string out =
		"# CSequenceTransitioner (pristine sequence_Transitioner.cpp, client C_AnimationLayer) over a scripted run.\n"
		"# model: seq | label | flags | fade in | fade out\n";
	for (int i = 0; i < kNumSequences; i++)
	{
		out += "model\t" + std::to_string(i) + "\t" + kSequences[i].pszLabel + "\t" +
			std::to_string(kSequences[i].nFlags) + "\t" + Num(kSequences[i].flFadeIn) + "\t" +
			Num(kSequences[i].flFadeOut) + "\n";
	}
	out += "# step | op (reset, check seq force interp, update seq cycle rate time, removeall) | args |"
		   " count, then per entry seq:cycle:rate:weight:animtime:fadeouttime (float bits)\n";

	CScript s(&hdr);

	// A check before anything was drawn pushes nothing: the tail's animtime is 0.
	s.Check(0, false, true);
	s.Check(1, true, true);

	// A change: ramp to steep fades ramp over min(0.3, 0.25), trimmed after.
	s.Reset();
	s.Step(0, 1.0f);
	s.Step(0, 1.05f);
	s.Step(1, 1.1f);
	for (int i = 1; i <= 8; i++)
		s.Step(1, 1.1f + 0.04f * i);

	// The reverse: steep to ramp fades over min(0.2, 0.4).
	s.Reset();
	s.Step(1, 2.0f);
	s.Step(0, 2.1f, false, true, 0.75f, 0.5f);
	for (int i = 1; i <= 6; i++)
		s.Step(0, 2.1f + 0.05f * i, false, true, 0.75f + 0.01f * i, 0.5f);

	// A restart: the same sequence, forced, queues the one that was playing.
	s.Reset();
	s.Step(0, 3.0f, false, true, 0.9f);
	s.Step(0, 3.02f, true, true, 0.0f);
	s.Step(0, 3.1f, false, true, 0.1f);
	s.Step(0, 3.2f, true, true, 0.0f);
	s.Step(0, 3.25f, false, true, 0.05f);

	// A STUDIO_SNAP sequence drops every queued entry.
	s.Reset();
	s.Step(0, 4.0f);
	s.Step(1, 4.05f);
	s.Step(3, 4.1f);
	s.Step(2, 4.12f);
	s.Step(2, 4.2f);
	// ...and leaving it is an ordinary change, over min(0.2, fade in).
	s.Step(0, 4.3f);
	s.Step(0, 4.4f);

	// A no-interp frame (EF_NOINTERP) snaps the same way.
	s.Reset();
	s.Step(0, 5.0f);
	s.Step(1, 5.05f);
	s.Step(3, 5.1f, false, false);
	s.Step(3, 5.2f);
	// ...and so does a forced restart on one.
	s.Step(3, 5.3f, true, false);

	// Several fades at once, trimmed one by one.
	s.Reset();
	s.Step(0, 6.0f);
	s.Step(1, 6.02f);
	s.Step(3, 6.04f);
	s.Step(0, 6.06f);
	s.Step(1, 6.08f);
	for (int i = 1; i <= 12; i++)
		s.Step(1, 6.08f + 0.03f * i);

	// A zero fade (instant's fade in): the old entry's weight is 0 at once,
	// so the next update trims it.
	s.Reset();
	s.Step(0, 7.0f);
	s.Step(4, 7.05f);
	s.Step(4, 7.1f);
	s.Step(0, 7.2f);
	s.Step(0, 7.25f);

	// The clock behind the change: GetFadeout's "shouldn't happen" arm, 1.
	s.Reset();
	s.Step(0, 8.0f);
	s.Step(1, 8.0f);
	s.Step(1, 7.9f);
	s.Step(1, 8.1f);

	// RemoveAll mid-fade (OnNewModel), then play on.
	s.Reset();
	s.Step(0, 9.0f);
	s.Step(1, 9.05f);
	s.Step(3, 9.1f);
	s.RemoveAll();
	s.Step(3, 9.15f);
	s.Step(0, 9.2f);
	s.RemoveAll();
	s.Check(1, false, true);
	s.Update(1, 0.5f, 1.0f, 9.3f);

	// An update at time 0: the tail then reads as never drawn.
	s.Reset();
	s.Step(0, 0.0f);
	s.Step(1, 0.0f);
	s.Step(1, 0.1f);
	s.Step(0, 0.2f);

	// Update without a check (a second update in one frame), and a check
	// without an update (ClientSideAnimationChanged's own check).
	s.Reset();
	s.Step(0, 10.0f);
	s.Update(1, 0.3f, 1.0f, 10.05f);
	s.Check(3, false, true);
	s.Check(3, false, true);
	s.Update(3, 0.3f, 1.0f, 10.1f);
	s.Step(0, 10.15f);

	// The seeded run: every operation, mostly paired as the game pairs them.
	s.Reset();
	Rng r;
	float flTime = 20.0f;
	for (int i = 0; i < 3000; i++)
	{
		const int nOp = r.Pick(0, 99);
		const int nSeq = r.Pick(0, kNumSequences - 1);
		const float flCycle = (float)r.Pick(0, 1000) / 1000.0f;
		const float rates[] = {1.0f, 0.5f, 2.0f, -1.0f, 0.0f};
		const float flRate = rates[r.Pick(0, 4)];

		if (r.Chance(3))
			flTime -= (float)r.Pick(1, 50) / 1000.0f; // the clock behind
		else
			flTime += (float)r.Pick(0, 120) / 1000.0f;

		if (nOp < 70)
			s.Step(r.Chance(60) ? nSeq : 0, flTime, r.Chance(10), !r.Chance(8), flCycle, flRate);
		else if (nOp < 82)
			s.Check(nSeq, r.Chance(15), !r.Chance(10));
		else if (nOp < 96)
			s.Update(nSeq, flCycle, flRate, flTime);
		else if (nOp < 98)
			s.RemoveAll();
		else
			s.Reset();
	}

	return out + s.m_Out;
}

std::string RepoPath(const char* pszRel)
{
	const char* pszRepo = getenv("SOURCESHARP_REPO");
	return std::string(pszRepo ? pszRepo : ".") + "/" + pszRel;
}
} // namespace

TEST(SequenceTransitionerOracle, StockProducesTheGolden)
{
	const std::string run = RunScenario();
	const std::string golden = RepoPath("src/test/oracle/managed/Fixtures/sequence-transitioner-stock.tsv");
	if (std::ifstream(RepoPath("src/test/oracle/native/sequence_transitioner_golden.regen")).good())
		std::ofstream(golden) << run;

	std::ifstream in(golden);
	ASSERT_TRUE(in.good()) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	ASSERT_GT(ss.str().size(), 100000u) << "the golden is empty or truncated";
	ASSERT_TRUE(ss.str() == run)
		<< "pristine CSequenceTransitioner no longer writes the golden; do NOT regenerate it to pass";
}

// The script exercises what it claims to: every arm of the subject is reached
// at least once, read off the recorded queues rather than assumed.
TEST(SequenceTransitionerOracle, TheScriptReachesEveryArm)
{
	const std::string run = RunScenario();
	std::istringstream in(run);
	std::string line;
	int nMaxQueue = 0, nSnaps = 0, nTrims = 0, nWeights = 0, nRemoveAll = 0, nPrev = -1;
	while (std::getline(in, line))
	{
		if (line.empty() || line[0] == '#' || line.rfind("model\t", 0) == 0)
			continue;
		std::istringstream f(line);
		std::string step, op, args, count;
		std::getline(f, step, '\t');
		std::getline(f, op, '\t');
		std::getline(f, args, '\t');
		std::getline(f, count, '\t');
		const int n = atoi(count.c_str());
		nMaxQueue = n > nMaxQueue ? n : nMaxQueue;
		if (op == "check" && nPrev > 1 && n == 1)
			nSnaps++;
		if (op == "update" && nPrev > n)
			nTrims++;
		if (op == "update" && n > 1)
			nWeights++;
		if (op == "removeall" && nPrev > 0)
			nRemoveAll++;
		nPrev = n;
	}
	EXPECT_GE(nMaxQueue, 4) << "no run held several fades at once";
	EXPECT_GT(nSnaps, 0) << "no snap or no-interp change cleared a queue";
	EXPECT_GT(nTrims, 0) << "no fade ran out";
	EXPECT_GT(nWeights, 0) << "no update weighted a fading entry";
	EXPECT_GT(nRemoveAll, 0) << "no RemoveAll emptied a queue";
}
