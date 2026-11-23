using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Horde;

using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>Whose crosshair <c>tf2_descent_spawn_robot</c> aims by: a named slot's (the admin menu's), else who typed it.</summary>
public sealed class HordeDevAimerTests
{
    private static readonly EntityHandle Named = new((uint)((3 << 16) | 4));
    private static readonly EntityHandle Typist = new((uint)((5 << 16) | 2));

    [Fact]
    public void A_named_slot_aims_by_that_players_crosshair()
    {
        var engine = new RecordingEngine().Returns("Players.GetHandle", Named).Returns("Players.GetCommandClient", Typist);

        Assert.Equal(Named, HordeDev.Aimer(engine, 4));
        Assert.Equal(4, engine.AllOf("Players.GetHandle").Single().ArgAs<int>(0));
    }

    [Fact]
    public void With_no_slot_it_aims_by_who_typed_it()
    {
        var engine = new RecordingEngine().Returns("Players.GetHandle", Named).Returns("Players.GetCommandClient", Typist);

        Assert.Equal(Typist, HordeDev.Aimer(engine, 0));
    }
}
