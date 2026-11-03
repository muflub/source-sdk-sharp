extern alias client;

using System.Runtime.InteropServices;

using AbiGlobals = SourceSharp.Abi.Globals;
using AbiGlobalsRaw = SourceSharp.Abi.CGlobalVarsRaw;
using ClientAbi = client::SourceSharp.Abi;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;

namespace TF2.Tests;

/// <summary>
/// The prediction's declared inputs for a fact: the tick, in a globals block
/// the stubbed environment answers, and the spy convars bound as the client's
/// registry binds them. Both prediction suites take them from here, so the
/// numbers agree.
/// </summary>
internal static class PredictionDeps
{
    /// <summary>The tick the replay's clock steps in.</summary>
    public const float Tick = 0.015f;

    private static readonly nint Block = BuildGlobals();

    /// <summary>The shared globals block, for fixtures that answer it themselves.</summary>
    public static nint SharedGlobals() => Block;

    private static nint BuildGlobals()
    {
        nint block = Marshal.AllocHGlobal(Marshal.SizeOf<AbiGlobalsRaw>());
        Marshal.WriteInt32(block, (int)Marshal.OffsetOf<AbiGlobalsRaw>(nameof(AbiGlobalsRaw.interval_per_tick)),
                           BitConverter.SingleToInt32Bits(Tick));
        return block;
    }

    /// <summary>The client's engine double, answering the globals.</summary>
    public static ClientEngine Engine()
    {
        var engine = new ClientEngine();
        engine.Returns("Environment.Globals", new AbiGlobals(Block));
        return engine;
    }

    /// <summary>The console behind the convars: declared defaults, settable values.</summary>
    private sealed class StubConsoleBackend : ClientAbi.IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;

        public string Get(int id) => _values[id];

        public string? ValueOf(string name) => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    /// <summary>The spy convars, bound to their declared defaults.</summary>
    public static ClientAbi.ConVarRegistry SpyConVars()
    {
        var convars = new ClientAbi.ConVarRegistry(new StubConsoleBackend());
        convars.RegisterTypes(
        [
            typeof(client::TF2.Shared.TFSpyCloakConsumeRateConVar),
            typeof(client::TF2.Shared.TFSpyCloakRegenRateConVar),
            typeof(client::TF2.Shared.TFSpyInvisTimeConVar),
            typeof(client::TF2.Shared.TFSpyInvisUnstealthTimeConVar),
        ]);
        return convars;
    }
}
