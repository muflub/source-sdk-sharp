using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>A game that does or does not simulate on alternate ticks, for what reads the answer.</summary>
internal sealed class StubAlternateTicks : IAlternateTicks
{
    public bool Simulating { get; set; }
}

/// <summary>
/// CBaseEntity::IsSimulatingOnAlternateTicks (baseentity_shared.cpp), which reads
/// <c>if ( gpGlobals-&gt;maxClients != 1 ) return false; return sv_alternateticks.GetBool();</c>.
/// </summary>
public sealed unsafe class AlternateTicksTests
{
    private static (AlternateTicks Subject, SvAlternateTicksConVar ConVar) Rig(int maxClients, string convar, CGlobalVarsRaw* globals)
    {
        globals->maxClients = maxClients;
        var engine = new RecordingEngine();
        engine.Returns("Environment.Globals", new Globals(globals));
        var held = DeclaredConVars.Holding<SvAlternateTicksConVar>(convar);
        return (new AlternateTicks(engine, held), held);
    }

    [Fact]
    public void ASinglePlayerGameWithTheConVarOnSimulatesOnAlternateTicks()
    {
        CGlobalVarsRaw globals = default;
        Assert.True(Rig(1, "1", &globals).Subject.Simulating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(8)]
    public void AnyOtherMaxClientsDoesNotWhateverTheConVarSays(int maxClients)
    {
        CGlobalVarsRaw globals = default;
        Assert.False(Rig(maxClients, "1", &globals).Subject.Simulating);
    }

    [Fact]
    public void ASinglePlayerGameWithTheConVarOffDoesNot()
    {
        CGlobalVarsRaw globals = default;
        Assert.False(Rig(1, "0", &globals).Subject.Simulating);
    }

    [Fact]
    public void TheAnswerFollowsTheConVar()
    {
        CGlobalVarsRaw globals = default;
        var (subject, convar) = Rig(1, "0", &globals);
        Assert.False(subject.Simulating);

        convar.Hold("1");

        Assert.True(subject.Simulating);
    }

    [Fact]
    public void TheAnswerFollowsMaxClients()
    {
        CGlobalVarsRaw globals = default;
        var (subject, _) = Rig(1, "1", &globals);
        Assert.True(subject.Simulating);

        globals.maxClients = 4;

        Assert.False(subject.Simulating);
    }

    /// <summary>The client asks before the engine has handed its globals across.</summary>
    [Fact]
    public void WithNoGlobalsYetItDoesNot()
    {
        var engine = new RecordingEngine();
        engine.Returns("Environment.Globals", default(Globals));
        var subject = new AlternateTicks(engine, DeclaredConVars.Holding<SvAlternateTicksConVar>("1"));

        Assert.False(subject.Simulating);
    }

    /// <summary>The physics frame's clock answers the same question, not one of its own.</summary>
    [Fact]
    public void ThePhysicsClockAsksIt()
    {
        var engine = new RecordingEngine();
        var alternate = new StubAlternateTicks { Simulating = true };
        var clock = new SourceSharp.Server.EnginePhysicsFrameClock(engine, engine, alternate);
        Assert.True(clock.AlternateTicks);

        alternate.Simulating = false;

        Assert.False(clock.AlternateTicks);
    }
}
