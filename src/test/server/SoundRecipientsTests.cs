using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Who a sound reaches (plan_server_native_v2 T4a): stock's CRecipientFilter,
/// CPASFilter and CPASAttenuationFilter (recipientfilter.cpp) as the managed
/// SoundEmitter builds them, against a recording engine.
/// </summary>
public sealed class SoundRecipientsTests
{
    private readonly SoundEmitterTests.FakeEngine _engine = new();
    private readonly SoundEmitterTests.Listeners _listeners = new();

    private SoundRecipients Recipients() => new(_engine, _listeners);

    [Fact]
    public void ASlotJoinsOnceAndOnlyInsideOneToMaxClients()
    {
        var filter = new RecipientFilter(4);

        foreach (int slot in new[] { 0, 1, 3, 3, 4, 5, -1 })
            filter.Add(slot);

        Assert.Equal([1, 3, 4], filter.Slots);
    }

    [Fact]
    public void BitNOfTheEnginesMaskIsPlayerNPlusOne()
    {
        var filter = new RecipientFilter(64);

        filter.AddFromBits([1u << 0 | 1u << 4, 1u << 5, 0, 0, 0, 0, 0, 0]);

        Assert.Equal([1, 5, 38], filter.Slots);
    }

    [Fact]
    public void ACopyHasTheFlagsAndTheListAndIsItsOwn()
    {
        var filter = new RecipientFilter(8).MakeReliable();
        filter.Add(2);

        RecipientFilter copy = filter.Copy();
        copy.Remove(2);

        Assert.True(copy.IsReliable);
        Assert.Equal([2], filter.Slots);
        Assert.Empty(copy.Slots);
    }

    [Fact]
    public void AllPlayersIsEveryConnectedSlot()
    {
        _listeners.Ears[2] = default;
        _listeners.Ears[5] = default;

        Assert.Equal([2, 5], Recipients().AllPlayers().Slots);
    }

    [Fact]
    public void APasOnAOnePlayerServerIsEveryoneAndDoesNotAskTheEngine()
    {
        _listeners.MaxClients = 1;
        _listeners.Ears[1] = default;

        RecipientFilter filter = Recipients().Pas(new Vector(1, 2, 3));

        Assert.Equal([1], filter.Slots);
        Assert.DoesNotContain(_engine.Asked, a => a.StartsWith("Multicast", StringComparison.Ordinal));
    }

    [Fact]
    public void APasOnAServerOfManyIsTheEnginesPas()
    {
        _engine.PasBits[0] = 1u << 2 | 1u << 6;

        RecipientFilter filter = Recipients().Pas(new Vector(1, 2, 3));

        Assert.Equal([3, 7], filter.Slots);
        Assert.Contains("Multicast True", _engine.Asked);
    }

    [Theory]
    [InlineData(75, 0.8f)]
    [InlineData(60, 2.0f)]
    [InlineData(50, 4.0f)]
    [InlineData(0, 4.0f)]
    public void ASoundLevelsAttenuationIsStocks(int level, float attenuation)
        => Assert.Equal(attenuation, SoundRecipients.SoundLevelToAttenuation(level), 5);

    [Fact]
    public void TheAttenuationDropsPlayersBeyondTwiceTheClipDistanceOverTheAttenuation()
    {
        // ATTN_NORM: 2 * 1000 / 0.8 = 2500 units.
        _engine.PasBits[0] = 0b1110;   // players 2, 3, 4
        _listeners.Ears[2] = new Vector(2400, 0, 0);
        _listeners.Ears[3] = new Vector(2600, 0, 0);
        // player 4 cannot say where it listens from: kept

        RecipientFilter filter = Recipients().PasAttenuation(default, SoundRecipients.AttnNorm);

        Assert.Equal([2, 4], filter.Slots);
    }

    [Fact]
    public void NothingIsCroppedOnAOnePlayerServerOrForNoAttenuation()
    {
        _listeners.Ears[1] = new Vector(99999, 0, 0);
        var recipients = Recipients();
        var filter = new RecipientFilter(8);
        filter.Add(1);

        recipients.Attenuate(filter, default, 0f);
        _listeners.MaxClients = 1;
        recipients.Attenuate(filter, default, 4f);

        Assert.Equal([1], filter.Slots);
    }

    [Fact]
    public void ALookupSoundsLevelDecidesHowFarItCarries()
    {
        _engine.Scripts["Loud"] = new SoundEmitterTests.Script { SoundLevel = 140 };   // 20 / 90: 9000 units
        _engine.PasBits[0] = 0b10;   // player 2
        _listeners.Ears[2] = new Vector(5000, 0, 0);

        Assert.Equal([2], Recipients().PasAttenuation(default, "Loud").Slots);
        Assert.Empty(Recipients().PasAttenuation(default, 75).Slots);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("00", true)]
    [InlineData("off", true)]   // atoi("off") is 0
    [InlineData("1", false)]
    [InlineData(" 2", false)]
    [InlineData("", false)]     // not reported yet
    [InlineData(null, false)]
    public void TheCloseCaptionRuleIsAtoiOfAReportedValue(string? value, bool removed)
        => Assert.Equal(removed, SoundRecipients.ShouldRemoveForCaptions(value));

    [Fact]
    public void RemovingNonCaptionersLeavesTheOthersInOrder()
    {
        _listeners.Captions[1] = "1";
        _listeners.Captions[2] = "0";
        var filter = new RecipientFilter(8);
        filter.Add(1);
        filter.Add(2);
        filter.Add(3);

        Recipients().RemoveIfNotCloseCaptioning(filter);

        Assert.Equal([1, 3], filter.Slots);
    }
}
