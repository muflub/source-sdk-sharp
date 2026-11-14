using SourceSharp.Abi;

using TF2.Server.Commands;
using TF2.Tests.Combat;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// <see cref="TFSetChargeCommand"/>: the press stages the meter through the
/// medigun service's own setter — clamp included — and nothing else moves.
/// </summary>
public sealed class TFSetChargeCommandTests
{
    private const int Seat = 3;
    private static readonly EntityHandle Player = new((uint)Seat);

    private readonly RecordingLog _log = new();
    private readonly RigMediguns _mediguns = new();

    private TFSetChargeCommand Command(string cheats = "1")
        => new(_mediguns, DeclaredConVars.Holding<SourceSharp.Shared.SvCheatsConVar>(cheats), _log);

    [Fact]
    public void WithoutCheatsThePressLeavesTheMeter()
    {
        var command = Command(cheats: "0");

        Assert.True(command.Handle(Player, "tf2_setcharge", ["1"]));

        Assert.Empty(_mediguns.Sets);
    }

    [Fact]
    public void APressStagesTheMeterThroughTheSetter()
    {
        var command = Command();

        Assert.True(command.Handle(Player, "tf2_setcharge", ["0.75"]));

        Assert.Equal(0.75f, _mediguns.ChargeLevel(Seat));
        Assert.Equal(["charge 3 0.75"], _mediguns.Sets);
    }

    [Fact]
    public void AnOffScaleLevelRidesTheSettersClamp()
    {
        var command = Command();

        Assert.True(command.Handle(Player, "tf2_setcharge", ["1.7"]));

        Assert.Equal(1f, _mediguns.ChargeLevel(Seat));
    }

    [Fact]
    public void AForeignVerbPassesThroughUnacted()
    {
        var command = Command();

        Assert.False(command.Handle(Player, "tf2_equip", ["medic", "primary", "412"]));

        Assert.Empty(_mediguns.Sets);
    }

    [Fact]
    public void ABareArgumentWarnsAndLeavesTheMeter()
    {
        var command = Command();

        Assert.True(command.Handle(Player, "tf2_setcharge", []));

        Assert.Empty(_mediguns.Sets);
        Assert.NotEmpty(_log.Lines.Where(l => l.Warning).ToList());
    }
}
