using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="ServiceRegistryTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public partial class ServiceRegistryTests
{
    //-------------------------------------------------------------------------
    // Declarations. These are real [ConVar]/[ConCommand] classes and so are
    // found by every other registry scan in this process too, which is harmless
    // — a scan with no container skips the ones that need one, and says so.
    //-------------------------------------------------------------------------

    [ConVar("ss_test_di_speed", "250", Help = "how fast the injected thing goes")]
    public sealed class DiSpeedConVar : FloatConVar { }

    /// <summary>A system a command can ask for.</summary>
    public interface ICounter { int Count { get; } void Add(); }

    public sealed class Counter : ICounter
    {
        public int Count { get; private set; }
        public void Add() => Count++;
    }

    [ConCommand("ss_test_di_bump", Help = "adds one to the counter")]
    public sealed class BumpCommand : IConCommand
    {
        private readonly ICounter _counter;
        private readonly DiSpeedConVar _speed;

        public BumpCommand(ICounter counter, DiSpeedConVar speed)
        {
            _counter = counter;
            _speed = speed;
        }

        /// <summary>What the convar read as when this was built.</summary>
        public float SpeedSeen => _speed.Value;

        public void Execute(string args) => _counter.Add();
    }

    [ConCommand("ss_test_di_plain")]
    public sealed class PlainCommand : IConCommand
    {
        public string Last { get; private set; } = string.Empty;
        public void Execute(string args) => Last = args;
    }
}
