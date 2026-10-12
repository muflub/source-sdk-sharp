using System.Collections.Generic;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The driver behind <c>tools/defect-teleport-gate</c>'s two arms.
///
/// <para>
/// The gate's verdict is a rendered measurement and has to be live. What can be
/// held here is the property the whole comparison RESTS on: that the two arms
/// differ in whether the move was declared a jump AND IN NOTHING ELSE. A
/// control arm that reached a different code path, a different entity or a
/// different distance would produce a difference, and it would be a difference
/// about nothing — which is the shape this project has already found in a gate
/// whose "before" and "after" were two different runs.
/// </para>
/// </summary>
public class TeleportProbeCommandTests
{
    private static readonly EntityHandle Subject = new(0x4001);

    private static (TeleportProbeCommand Command, RecordingEngine Engine) Probe()
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("Entities.FindAllByName", new List<EntityHandle> { Subject });
        engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        engine.Returns("Entities.GetIndex", 42);

        return (new TeleportProbeCommand(new RecordingLog(), engine), engine);
    }

    [Fact]
    public void TheMoveArmWritesTheOriginAndDoesNotTeleport()
    {
        var (command, engine) = Probe();

        command.Execute("move probe_subject 500 0 8");

        var write = engine.Calls.Last(c => c.Member == "Entities.SetOrigin");
        Assert.Equal(new Vector(500f, 0f, 8f), write.ArgAs<Vector>(1));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Teleport");
    }

    [Fact]
    public void TheJumpArmTeleportsAndDoesNotWriteTheOrigin()
    {
        var (command, engine) = Probe();

        command.Execute("jump probe_subject 500 0 8");

        var jump = engine.Calls.Last(c => c.Member == "Entities.Teleport");
        Assert.Equal(new Vector(500f, 0f, 8f), jump.ArgAs<Vector>(1));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetOrigin");
    }

    /// <summary>
    /// THE PREMISE OF THE WHOLE COMPARISON: both arms send the same entity to
    /// the same place. If they did not, the gate would be measuring the
    /// difference between two journeys rather than between two declarations of
    /// one.
    /// </summary>
    [Fact]
    public void BothArmsSendTheSameEntityToTheSamePlace()
    {
        var (move, moveEngine) = Probe();
        var (jump, jumpEngine) = Probe();

        move.Execute("move probe_subject 500 0 8");
        jump.Execute("jump probe_subject 500 0 8");

        Assert.Equal(
            moveEngine.Calls.Last(c => c.Member == "Entities.SetOrigin").ArgAs<Vector>(1),
            jumpEngine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<Vector>(1));

        Assert.Equal(
            moveEngine.Calls.Last(c => c.Member == "Entities.SetOrigin").ArgAs<EntityHandle>(0),
            jumpEngine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void AnArmThatIsNeitherWordMovesNothing()
    {
        var (command, engine) = Probe();

        command.Execute("slide probe_subject 500 0 8");

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetOrigin");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Teleport");
    }

    [Fact]
    public void ADestinationThatIsNotThreeNumbersMovesNothing()
    {
        var (command, engine) = Probe();

        command.Execute("jump probe_subject 500 0");
        command.Execute("jump probe_subject 500 0 over-there");

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Teleport");
    }

    [Fact]
    public void ANameNothingAnswersToMovesNothing()
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("Entities.FindAllByName", new List<EntityHandle>());

        new TeleportProbeCommand(new RecordingLog(), engine).Execute("jump nobody 500 0 8");

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Teleport");
    }
}
