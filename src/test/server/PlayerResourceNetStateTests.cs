using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The scoreboard's wire declaration (lane U cluster 14).
///
/// <para>
/// What this pins is the half a unit test CAN pin: that the declaration exists
/// under the classname both sides publish it by, that it fits a tier, that the
/// runs are the length the accessors index, and that the row writer packs the
/// flag word the way the readers unpack it. It cannot show the two halves PAIR
/// — only a live join does that — and it does not try to.
/// </para>
///
/// <para>
/// The field NAMES the native reader asks for are pinned on the other side of
/// the boundary, by <c>sourcesharp/test/playerresource_pairing_test.cpp</c>,
/// because that is where the copy of them lives.
/// </para>
/// </summary>
public sealed class PlayerResourceNetStateTests
{
    [Fact]
    public void IsDeclaredForTheStockClassname()
    {
        Assert.Equal(EntityNames.PlayerManager, PlayerResourceNetState.NetLayout.ClassName);
        Assert.Equal("player_manager", EntityNames.PlayerManager);
    }

    /// <summary>
    /// Every run is <see cref="PlayerResourceNetState.Slots"/> long, counted off
    /// the generated table rather than off the declaration that produced it.
    ///
    /// The generator names an array's elements <c>Field.0</c> upward, so this
    /// counts the entries whose name begins with each run's own prefix.
    /// </summary>
    [Theory]
    [InlineData("Score")]
    [InlineData("Deaths")]
    [InlineData("Health")]
    [InlineData("UserId")]
    [InlineData("Team")]
    [InlineData("Flags")]
    [InlineData("Ping")]
    public void EveryRunIsOneSlotPerPlayer(string run)
    {
        int n = PlayerResourceNetState.NetLayout.Fields
            .Count(f => f.Name.StartsWith(run + ".", System.StringComparison.Ordinal));

        Assert.Equal(PlayerResourceNetState.Slots, n);
    }

    /// <summary>
    /// The seven runs are the whole table — nothing else crept in, and nothing
    /// was dropped. Counted rather than listed so that adding a seventh run is
    /// a red test rather than a silent widening of the wire format.
    /// </summary>
    [Fact]
    public void TheTableIsExactlyTheSevenRuns()
    {
        // Seven since the scoreboard's ping (plan_client_ui U3), stock's m_iPing.
        Assert.Equal(7 * PlayerResourceNetState.Slots,
                     PlayerResourceNetState.NetLayout.Fields.Count);
    }

    /// <summary>
    /// It fits a tier with room to spare, and BOTH bounds matter.
    ///
    /// <para>
    /// 340 bytes lands in the 512 tier at two thirds full, which leaves room
    /// for a run to be added without the whole thing moving to 1,024 and
    /// doubling its payload words. The first version of this class sat at
    /// 1,020 of 1,024 — 99.6%, which <c>debug-api nettable</c> flagged as
    /// <c>nearCeiling</c> — because it declared 102 rows for a build whose
    /// MAX_PLAYERS is 33.
    /// </para>
    ///
    /// <para>
    /// The lower bound is the one that catches the mistake going the other way:
    /// a declaration that shrank past 256 would mean a run was dropped, and the
    /// scoreboard would lose a column with nothing else complaining.
    /// </para>
    /// </summary>
    [Fact]
    public void FitsTheFiveHundredAndTwelveByteTierWithRoomOver()
    {
        // THE DECLARATION, not the blob. Every blob now opens with the engine
        // block (plan_entity.md §3.2), and the bounds below are about how many
        // scoreboard rows this class declares -- so what is measured is what the
        // class itself asks for. The blob it lands in is a separate fact, and it
        // is the one below.
        int declared = PlayerResourceNetState.NetLayout.Size - EngineBlock.SizeBytes;

        Assert.True(declared <= 512,
            $"the declaration is {declared} bytes, "
            + "which no longer fits the 512-byte tier — check that the slot count "
            + "still matches the engine's MAX_PLAYERS before raising this");

        Assert.True(declared > 256,
            $"the declaration is down to {declared} "
            + "bytes; a run has been dropped, or the slot count has collapsed");
    }

    /// <summary>
    /// And which tier the whole blob lands in, said out loud.
    ///
    /// <para>
    /// THE BLOCK MOVED THIS CLASS UP A TIER, and that is a real cost of putting
    /// the engine block at the front of every blob rather than something to hide
    /// behind a relative bound. The declaration is comfortably inside 512; the
    /// declaration plus a 352-byte block is not, so player_manager is a
    /// 1024-byte entity now. Written down here because the next person to wonder
    /// why deserves to find the answer rather than rediscover it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWholeBlobLandsInTheThousandAndTwentyFourByteTier()
    {
        int blob = PlayerResourceNetState.NetLayout.Size;

        Assert.True(blob > 512, $"the blob is {blob} bytes, which would fit 512 after all");
        Assert.True(blob <= 1024, $"the blob is {blob} bytes and no longer fits the 1024 tier");
    }

    /// <summary>
    /// The slot count is the engine's bound, not shareddefs.h's HL2MP branch.
    ///
    /// <para>
    /// This is the assertion the first live run would have saved. shareddefs.h
    /// gives MAX_PLAYERS as 101 under <c>HL2MP</c> and 33 otherwise, and this
    /// mod is an HL2MP mod whose two projects do not define HL2MP — so both
    /// DLLs compile the 33, and every consumer of <c>IGameResources</c> loops
    /// <c>1..33</c>. Declaring more rows than that networks bytes nobody reads.
    /// </para>
    ///
    /// <para>
    /// Pinned against <c>Engine.MaxPlayerSlots</c>, which is the same fact
    /// already written down once — so this is a comparison rather than a second
    /// copy, and moving the mod to a real 101-player build moves both together.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSlotCountIsMaxPlayersArraySafe()
    {
        Assert.Equal(EngineLimits.MaxPlayerSlots + 1, PlayerResourceNetState.Slots);
    }

    /// <summary>
    /// Slot zero is a row and slot <c>Slots</c> is not. Consumers index by
    /// ENTITY index and loop 1..MAX_PLAYERS, so the top index they can reach is
    /// MAX_PLAYERS — which is <c>Slots - 1</c>.
    /// </summary>
    [Fact]
    public void TheRangeIsEveryEntityIndexAPlayerCanHave()
    {
        Assert.True(PlayerResourceNetState.InRange(0));
        Assert.True(PlayerResourceNetState.InRange(PlayerResourceNetState.Slots - 1));
        Assert.False(PlayerResourceNetState.InRange(PlayerResourceNetState.Slots));
        Assert.False(PlayerResourceNetState.InRange(-1));
    }

    /// <summary>The three flag bits are distinct, and each is one bit.</summary>
    [Fact]
    public void TheFlagBitsDoNotOverlap()
    {
        int all = PlayerResourceNetState.FlagConnected
                | PlayerResourceNetState.FlagAlive
                | PlayerResourceNetState.FlagValid;

        Assert.Equal(7, all);
        Assert.NotEqual(PlayerResourceNetState.FlagConnected, PlayerResourceNetState.FlagAlive);
        Assert.NotEqual(PlayerResourceNetState.FlagAlive, PlayerResourceNetState.FlagValid);
    }

    /// <summary>
    /// A state with storage of its own and no entity behind it — the client's
    /// own arrangement, and the only one a test can have.
    /// </summary>
    private static PlayerResourceNetState Detached()
    {
        var state = new PlayerResourceNetState();
        state.BindLocal();
        return state;
    }

    [Fact]
    public void SetRowRoundTripsEveryField()
    {
        var state = Detached();

        state.SetRow(7, score: 12, deaths: 3, health: 64, userId: 22, team: 2,
                     connected: true, alive: true, valid: true);

        Assert.Equal(12, state.Score[7]);
        Assert.Equal(3, state.Deaths[7]);
        Assert.Equal(64, state.Health[7]);
        Assert.Equal(22, state.UserId[7]);
        Assert.Equal(2, state.Team[7]);
        Assert.True(state.IsConnected(7));
        Assert.True(state.IsAlive(7));
        Assert.True(state.IsValid(7));
    }

    /// <summary>
    /// One row written does not touch its neighbours. A stride computed wrongly
    /// would show up here and nowhere else in a unit test.
    /// </summary>
    [Fact]
    public void WritingOneRowLeavesTheNeighboursAlone()
    {
        var state = Detached();

        state.SetRow(7, 12, 3, 64, 22, 2, true, true, true);

        foreach (int slot in new[] { 6, 8 })
        {
            Assert.Equal(0, state.Score[slot]);
            Assert.Equal(0, state.Deaths[slot]);
            Assert.Equal(0, state.Health[slot]);
            Assert.Equal(0, state.UserId[slot]);
            Assert.Equal(0, state.Team[slot]);
            Assert.False(state.IsConnected(slot));
        }
    }

    /// <summary>
    /// The three flags are genuinely independent. Set alone, each reads back
    /// alone — which is the packing bug a shared byte invites.
    /// </summary>
    [Fact]
    public void EachFlagIsIndependent()
    {
        var state = Detached();

        state.SetRow(3, 0, 0, 0, 0, 0, connected: true, alive: false, valid: false);
        Assert.True(state.IsConnected(3));
        Assert.False(state.IsAlive(3));
        Assert.False(state.IsValid(3));

        state.SetRow(3, 0, 0, 0, 0, 0, connected: false, alive: true, valid: false);
        Assert.False(state.IsConnected(3));
        Assert.True(state.IsAlive(3));
        Assert.False(state.IsValid(3));

        state.SetRow(3, 0, 0, 0, 0, 0, connected: false, alive: false, valid: true);
        Assert.False(state.IsConnected(3));
        Assert.False(state.IsAlive(3));
        Assert.True(state.IsValid(3));
    }

    /// <summary>
    /// Health is never sent negative. Stock clamped for the same reason: a dead
    /// player's health is an overkill amount below zero, and a bar drawn from it
    /// runs backwards.
    /// </summary>
    [Fact]
    public void NegativeHealthIsSentAsZero()
    {
        var state = Detached();

        state.SetRow(4, 0, 0, health: -250, userId: 0, team: 0,
                     connected: true, alive: false, valid: true);

        Assert.Equal(0, state.Health[4]);
    }

    /// <summary>
    /// A value too wide for its run is clamped, not wrapped. A wrap turns a
    /// score of 32,768 into -32,768 and draws as an enormous deficit.
    /// </summary>
    [Fact]
    public void AnOversizeScoreClampsRatherThanWraps()
    {
        var state = Detached();

        state.SetRow(5, score: 40000, deaths: -40000, health: 0, userId: 0, team: 0,
                     connected: true, alive: true, valid: true);

        Assert.Equal(short.MaxValue, state.Score[5]);
        Assert.Equal(short.MinValue, state.Deaths[5]);
    }

    /// <summary>A slot outside the runs is ignored rather than throwing into native frames.</summary>
    [Fact]
    public void AnOutOfRangeSlotIsIgnored()
    {
        var state = Detached();

        state.SetRow(PlayerResourceNetState.Slots, 1, 1, 1, 1, 1, true, true, true);
        state.SetRow(-1, 1, 1, 1, 1, 1, true, true, true);

        Assert.False(state.IsConnected(PlayerResourceNetState.Slots));
        Assert.False(state.IsConnected(-1));
    }

    [Fact]
    public void ClearAllEmptiesEveryRow()
    {
        var state = Detached();

        for (int slot = 0; slot < PlayerResourceNetState.Slots; slot++)
            state.SetRow(slot, 9, 9, 9, 9, 1, true, true, true);

        state.ClearAll();

        for (int slot = 0; slot < PlayerResourceNetState.Slots; slot++)
        {
            Assert.Equal(0, state.Score[slot]);
            Assert.False(state.IsConnected(slot));
            Assert.False(state.IsValid(slot));
        }
    }
}
