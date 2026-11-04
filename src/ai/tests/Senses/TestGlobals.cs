using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>The engine's globals as a fact holds them: a host frame and a tick it moves by hand.</summary>
internal sealed unsafe class TestGlobals : IEngineEnvironment, IDisposable
{
    private readonly CGlobalVarsRaw* _raw = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));

    public TestGlobals(int maxClients = 8)
    {
        _raw->interval_per_tick = 0.015f;
        _raw->maxClients = maxClients;
    }

    public int Frame
    {
        get => _raw->framecount;
        set => _raw->framecount = value;
    }

    public nint GetInterface(string name) => 0;

    public Globals Globals => new(_raw);

    public void Dispose() => NativeMemory.Free(_raw);
}
