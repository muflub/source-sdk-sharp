//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
extern alias client;
using RandomSeeds = client::SourceSharp.Abi.RandomSeeds;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>CTempEnts::EjectBrass</c>'s decision half — docs/plan-audit.md D10.
///
/// <para>
/// The one helper of the legacy pool's seventeen that writes into a slot
/// somebody else reserved: its model is a <c>model_t *</c> out of
/// <c>m_pShells[]</c> with no precache index, so native resolves it and builds
/// the object and only the decision is C#'s. That shape is what these test —
/// the record, not the reservation.
/// </para>
///
/// <para>
/// Three facts read <c>c_te_legacytempents.cpp</c> and <c>public/const.h</c>
/// OFF DISK, which is the pattern <see cref="TempEntSpawnTests"/> established:
/// a constant a port copied and a constant a port derived look identical in a
/// green suite, and the difference is whether the two files still agree.
/// </para>
/// </summary>
public class TempEntEjectBrassTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>
    /// A native side that accepts every write and remembers it — and, unlike
    /// <see cref="TempEntSpawnTests"/>'s, refuses every CREATE, because nothing
    /// on this path is supposed to make one.
    /// </summary>
    private sealed class WritingPool : EngineEffectsForwarder
    {
        public WritingPool(IEngineEffects inner) : base(inner) { }

        public List<TempEntSpawn> Spawned { get; } = new();
        public int Creates { get; private set; }

        public override int TempEntPoolCapacity() => 500;

        public override bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
        {
            Creates++;
            return true;
        }

        public override bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
        {
            Spawned.Add(spawn);
            return true;
        }
    }

    private sealed record Rigging(TempEntSpawns Spawns, WritingPool Native,
                                  ClientTempEntPool Pool, Clock Clock);

    private static Rigging Rig()
    {
        var engine = TestServices.Fake();
        var native = new WritingPool(engine);
        var clock = new Clock { Now = 100f };

        var pool = new ClientTempEntPool(new RecordingLog(), native, clock, new TempEntPoolTests.NoSimulation());

        return new Rigging(new TempEntSpawns(new RecordingLog(), pool, engine, engine, clock, new RandomSeeds(1)), native, pool, clock);
    }

    /// <summary>The slot native would have reserved, reserved here the same way
    /// native's <c>OnTempEntPoolAlloc</c> does.</summary>
    private static int Reserved(Rigging rig) => rig.Pool.Reserve(TempEntPriority.Low);

    private static string? StockFile(params string[] parts)
    {
        string path = RepoLayout.Sdk(parts);

        return File.Exists(path) ? path : null;
    }

    private static bool Between(float value, float low, float high)
        => value >= MathF.Min(low, high) && value <= MathF.Max(low, high);

    // ---- the slot ----------------------------------------------------------

    /// <summary>
    /// It writes into the slot it was given and does NOT create. The create is
    /// native's — it has the model — and a managed create here would build a
    /// second object into a slot that already holds one.
    /// </summary>
    [Fact]
    public void ItWritesIntoTheGivenSlotAndBuildsNothing()
    {
        var rig = Rig();
        int slot = Reserved(rig);

        Assert.True(rig.Spawns.EjectBrass(slot, 0, Vector.Zero, Vector.Zero),
            "the record is written");
        Assert.True(rig.Native.Spawned.Count == 1, "exactly one record");
        Assert.True(rig.Native.Creates == 0,
            "and nothing was built -- native already did that, with the model");
    }

    /// <summary>
    /// A slot the pool has not reserved is refused rather than written. The
    /// write would land on whatever the native table holds at that index, which
    /// is the previous tenant of a recycled slot.
    /// </summary>
    [Fact]
    public void AnUnreservedSlotIsRefused()
    {
        var rig = Rig();

        Assert.False(rig.Spawns.EjectBrass(7, 0, Vector.Zero, Vector.Zero),
            "a slot this pool never handed out is not written to");
        Assert.True(rig.Native.Spawned.Count == 0, "and nothing reached native");
    }

    // ---- the decision ------------------------------------------------------

    [Fact]
    public void AShotgunShellSoundsDifferentFromARifleCase()
    {
        var rig = Rig();

        rig.Spawns.EjectBrass(Reserved(rig), 2, Vector.Zero, Vector.Zero);
        rig.Spawns.EjectBrass(Reserved(rig), 1, Vector.Zero, Vector.Zero);
        rig.Spawns.EjectBrass(Reserved(rig), 0, Vector.Zero, Vector.Zero);

        Assert.True(rig.Native.Spawned[0].HitSound == BounceSounds.ShotShell,
            "type 2 is the shotgun's");
        Assert.True(rig.Native.Spawned[1].HitSound == BounceSounds.Shell,
            "and every other type is a cartridge case");
        Assert.True(rig.Native.Spawned[2].HitSound == BounceSounds.Shell,
            "including type 0");
    }

    /// <summary>
    /// The two bounce constants read out of <c>public/const.h</c>.
    ///
    /// <para>
    /// This is the check that would otherwise be a copy agreeing with itself.
    /// <c>BOUNCE_SHELL</c> is 0x20 and <c>TE_BOUNCE_SHELL</c> is 1 — two
    /// vocabularies four lines apart in one header, and picking the wrong one
    /// gives a casing that lands in silence, which no assertion about the
    /// record's own value could tell apart.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBounceConstantsAreConstHs()
    {
        string? path = StockFile("src", "public", "const.h");
        Assert.True(path is not null, "public/const.h is found next to the tests");

        string text = File.ReadAllText(path!);

        Assert.True(text.Contains("#define BOUNCE_SHELL\t0x20"),
            "BOUNCE_SHELL is still 0x20 in public/const.h");
        Assert.True(text.Contains("#define BOUNCE_SHOTSHELL 0x80"),
            "BOUNCE_SHOTSHELL is still 0x80 in public/const.h");

        Assert.True(BounceSounds.Shell == 0x20, "and this port's Shell agrees");
        Assert.True(BounceSounds.ShotShell == 0x80, "and its ShotShell agrees");
    }

    /// <summary>
    /// COLLIDEWORLD and not COLLIDEALL, which is the flag a mapper would
    /// notice: a case bounces off the map and passes through people, so a
    /// firefight does not end with brass resting on everyone's shoulders.
    /// </summary>
    [Fact]
    public void ACaseBouncesOffTheWorldAndThroughPeople()
    {
        var rig = Rig();
        rig.Spawns.EjectBrass(Reserved(rig), 0, Vector.Zero, Vector.Zero);

        int flags = rig.Native.Spawned[0].Flags;

        Assert.True((flags & (int)TempEntFlags.CollideWorld) != 0, "it hits the map");
        Assert.True((flags & (int)TempEntFlags.CollideAll) == 0, "and not entities");
    }

    /// <summary>
    /// GRAVITY and not SLOWGRAVITY, which is this helper alone among the ones
    /// that fall: brass is heavy, and stock says so by the flag it picks.
    /// </summary>
    [Fact]
    public void ACaseFallsAtFullGravity()
    {
        var rig = Rig();
        rig.Spawns.EjectBrass(Reserved(rig), 0, Vector.Zero, Vector.Zero);

        int flags = rig.Native.Spawned[0].Flags;

        Assert.True((flags & (int)TempEntFlags.Gravity) != 0, "full gravity");
        Assert.True((flags & (int)TempEntFlags.SlowGravity) == 0,
            "and not the half-rate one every sprite helper in this file uses");
    }

    [Fact]
    public void ACaseTumblesAndFadesOut()
    {
        var rig = Rig();
        rig.Spawns.EjectBrass(Reserved(rig), 0, Vector.Zero, Vector.Zero);

        var spawn = rig.Native.Spawned[0];

        Assert.True((spawn.Flags & (int)TempEntFlags.Rotate) != 0, "it tumbles");
        Assert.True((spawn.Flags & (int)TempEntFlags.FadeOut) != 0, "and fades");
        Assert.True(spawn.RenderAmt == 255,
            "from full opacity -- FTENT_FADEOUT ramps the alpha down FROM this, so a "
            + "zero would be a case invisible the moment it leaves the gun");
    }

    /// <summary>
    /// The case FACES the way the gun does and is THROWN the way it is aimed,
    /// and they are two different arguments. A port that used one for both
    /// looks right at every call site that passes the same angles twice —
    /// which is what the pool's own dev command does — and wrong everywhere
    /// real.
    /// </summary>
    [Fact]
    public void ACaseFacesTheGunAndIsThrownWhereItIsAimed()
    {
        var rig = Rig();

        // Thrown along +X, facing +Y.
        var thrown = new Vector(0f, 0f, 0f);
        var facing = new Vector(0f, 90f, 0f);

        rig.Spawns.EjectBrass(Reserved(rig), 0, thrown, facing);

        var spawn = rig.Native.Spawned[0];

        Assert.True(spawn.Angles.Y == 90f, "it is facing the way the GUN is");

        // Thrown along +X at 150..200, scattered by up to 64 on each axis.
        Assert.True(Between(spawn.Velocity.X, 150f - 64f, 200f + 64f),
            "and thrown along the direction it was AIMED, not the one it faces");
    }

    /// <summary>
    /// The Z scatter is 0..64 where X and Y are −64..64, so a case is never
    /// thrown downwards harder than gravity is already taking it. Asserted over
    /// enough draws that a symmetric range would have to be very unlucky to
    /// pass — and asserted on the SIGN rather than on a mean, because a mean
    /// over a symmetric range is near zero and so is a mean over a range that
    /// is merely shifted.
    /// </summary>
    [Fact]
    public void ACaseIsNeverThrownDownwards()
    {
        var rig = Rig();

        for (int i = 0; i < 200; i++)
        {
            rig.Spawns.EjectBrass(Reserved(rig), 0, Vector.Zero, Vector.Zero);
        }

        // Aimed flat, so the direction contributes nothing to Z and every Z is
        // the scatter alone.
        Assert.True(rig.Native.Spawned.All(s => s.Velocity.Z >= 0f),
            "no case is thrown downwards over 200 draws");
        Assert.True(rig.Native.Spawned.Any(s => s.Velocity.Z > 1f),
            "and the scatter is not simply zero, which would pass the line above");
    }

    /// <summary>
    /// One second plus nought to one more — stock's "Add an extra 0-1 secs of
    /// life". Both ends checked, because a lifetime of a flat second and a
    /// lifetime of one-to-two look the same in any single draw.
    /// </summary>
    [Fact]
    public void ACaseLivesOneSecondPlusUpToOneMore()
    {
        var rig = Rig();

        for (int i = 0; i < 200; i++)
        {
            rig.Spawns.EjectBrass(Reserved(rig), 0, Vector.Zero, Vector.Zero);
        }

        Assert.True(rig.Native.Spawned.All(s => Between(s.Die, rig.Clock.Now + 1f,
                                                        rig.Clock.Now + 2f)),
            "every case dies between one and two seconds from now");
        Assert.True(rig.Native.Spawned.Any(s => s.Die > rig.Clock.Now + 1.1f),
            "and the extra second is real rather than always zero");
    }

    /// <summary>
    /// The whole decision, held to the body it was ported from — which is no
    /// longer on disk, and that is the point of the shape below.
    ///
    /// <para>
    /// <b>RESIDUE WAVE, 2026-09.</b> This fact used to read
    /// <c>CTempEnts::EjectBrass</c>'s stock body out of
    /// <c>c_te_legacytempents.cpp</c> and assert the managed numbers against
    /// it. That body was the <c>#else</c> arm of an <c>#ifdef SOURCESHARP</c>
    /// and <b>was never compiled</b> — <c>SOURCESHARP</c> is defined in every
    /// build there is. The residue wave deleted it, along with 5,282 other
    /// lines of dead arm, with every object's symbol sets unchanged. The
    /// deleted text is at <c>74b071771:src/game/client/c_te_legacytempents.cpp</c>.
    /// </para>
    ///
    /// <para>
    /// <b>The subject is genuinely gone and there is no other copy</b> — no
    /// file in <c>src/</c> spells <c>RandomFloat(-1024,1024)</c> any more. So
    /// the reference is TRANSCRIBED here, with its provenance, and the
    /// agreement is checked in the one direction that still has two sides: each
    /// stock literal against the managed source that claims to implement it.
    /// That still catches what the original caught — a port whose constants
    /// drifted — because the behaviour facts above are all written from the
    /// same understanding as the code and would move with it.
    /// </para>
    ///
    /// <para>
    /// And the other half is an ABSENCE: stock's body must NOT be back in
    /// <c>c_te_legacytempents.cpp</c>. If it returns, its guard has probably
    /// returned with it, this transcription is no longer the only copy, and the
    /// two should be diffed before either is trusted. Without this half the
    /// fact would pass over an empty managed file just as happily as over a
    /// correct one, which is the failure mode a rewrite of this kind has.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNumbersAgreeWithStocksTranscribedBody()
    {
        // ---- PRESENT: the managed side still spells every transcribed number.
        string? managed = StockFile("src", "sourcesharp", "managed",
                                    "SourceSharp.Client", "TempEnts", "TempEntSpawns.cs");
        Assert.True(managed is not null,
            "TempEntSpawns.cs is found next to the tests -- otherwise every "
            + "assertion below would pass vacuously on an empty string");

        string port = File.ReadAllText(managed!);
        int mAt = port.IndexOf("public bool EjectBrass(", StringComparison.Ordinal);
        Assert.True(mAt >= 0, "TempEntSpawns.EjectBrass is still in the file");

        // To the next member, so nothing below is read out of a sibling helper.
        int mEnd = port.IndexOf("\n    }\n", mAt, StringComparison.Ordinal);
        string mBody = mEnd > mAt ? port[mAt..mEnd] : port[mAt..];

        // Each pair is (what stock's deleted body said, what the port must say).
        // The stock column is the transcription; the port column is what is
        // actually checked against the tree.
        var pairs = new (string Stock, string Port, string Why)[]
        {
            ("random->RandomFloat(-1024,1024)",
             "_random.Float(-1024f, 1024f)",
             "the tumble is still +/-1024 degrees a second"),
            ("random->RandomFloat( 150.0f, 200.0f )",
             "_random.Float(150f, 200f)",
             "the throw is still 150..200 units a second"),
            ("random->RandomFloat(-64,64)",
             "_random.Float(-64f, 64f)",
             "the horizontal scatter is still +/-64"),
            ("random->RandomFloat(  0,64)",
             "_random.Float(0f, 64f)",
             "and the vertical scatter is still 0..64 -- ASYMMETRIC, which is the "
             + "one a tidier port would have made symmetric"),
            ("gpGlobals->curtime + 1.0f + random->RandomFloat( 0.0f, 1.0f )",
             "_clock.Now + 1f + _random.Float(0f, 1f)",
             "and the lifetime is still one second plus nought to one"),
            ("FTENT_COLLIDEWORLD | FTENT_FADEOUT | FTENT_GRAVITY | FTENT_ROTATE",
             "TempEntFlags.CollideWorld | TempEntFlags.FadeOut\n                           | TempEntFlags.Gravity | TempEntFlags.Rotate",
             "and the four flags are still those four"),
        };

        foreach (var pair in pairs)
        {
            Assert.True(mBody.Contains(pair.Port, StringComparison.Ordinal),
                pair.Why + " -- stock's deleted body said `" + pair.Stock + "`");
        }

        // ---- ABSENT: stock's body has not come back. Since C2 (lane c2-fx) the
        // entry point is the thunk's: c_te_legacytempents.cpp left the build and
        // shell_legacy_tempents_client.cpp holds CTempEnts::EjectBrass.
        string body = ThunkEjectBrassBody();

        foreach (var pair in pairs)
        {
            Assert.False(body.Contains(pair.Stock, StringComparison.Ordinal),
                "stock's deleted arm is back in CTempEnts::EjectBrass (`" + pair.Stock
                + "`). It was the #else of an #ifdef SOURCESHARP and was never compiled. "
                + "Check whether the GUARD came back with it -- that would be a much "
                + "larger revert -- and diff it against the transcription above before "
                + "trusting either.");
        }
    }

    /// <summary>
    /// And the call that replaced it is there — now UNCONDITIONALLY, which is
    /// a stronger claim than the one this fact used to make.
    ///
    /// <para>
    /// It used to assert the call sat behind <c>#ifdef SOURCESHARP</c>, because
    /// while stock's <c>#else</c> arm existed the guard was what chose between
    /// them. The residue wave deleted the dead arm and the guard with it, so the
    /// successor property is that there is no conditional left in this body AT
    /// ALL: exactly one path, and it goes to C#. Asserting the guard's presence
    /// now would be asserting that a dead arm came back.
    /// </para>
    ///
    /// <para>
    /// Both halves, because either alone can be satisfied by an empty body: the
    /// call must be PRESENT, and no preprocessor conditional may be.
    /// </para>
    /// </summary>
    [Fact]
    public void TheArmedSideCallsTheManagedDecision()
    {
        string body = ThunkEjectBrassBody();

        Assert.True(body.Contains("pFx->TempEntEjectBrass( in, type );", StringComparison.Ordinal),
            "the body hands the decision to C# -- otherwise nothing calls the port");

        // LINE-INITIAL, AND NOT IN A COMMENT. The body's own prose names the
        // guard that used to be here, so a raw substring scan would read the
        // explanation as the fact -- the hazard this directory exists to catch.
        // A preprocessor directive is line-initial by construction, so that is
        // what is looked for.
        foreach (string line in body.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            Assert.False(trimmed.StartsWith("#", StringComparison.Ordinal),
                "CTempEnts::EjectBrass's body carries the directive `" + trimmed
                + "`. Since the residue wave it has exactly one path and it goes to "
                + "C#; a conditional here means a second arm has been reintroduced.");
        }
    }

    /// <summary>
    /// CTempEnts::EjectBrass's body in the thunk that replaced
    /// c_te_legacytempents.cpp (C2): from its definition to the next one.
    /// </summary>
    private static string ThunkEjectBrassBody()
    {
        string? path = StockFile("src", "sourcesharp", "game", "client", "shell_legacy_tempents_client.cpp");
        Assert.True(path is not null, "shell_legacy_tempents_client.cpp is found next to the tests");

        string text = File.ReadAllText(path!);
        int at = text.IndexOf("void CTempEnts::EjectBrass(", StringComparison.Ordinal);
        Assert.True(at >= 0, "CTempEnts::EjectBrass is in the thunk -- the ENTRY POINT "
                           + "stays; it is the decision that moved");

        int end = text.IndexOf("void CTempEnts::AttachTentToPlayer(", at, StringComparison.Ordinal);
        Assert.True(end > at, "the thunk's AttachTentToPlayer still follows EjectBrass, so the body has an end");
        return text[at..end];
    }
}
