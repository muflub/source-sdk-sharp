using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// IManagedSoundEmitter's binding (plan_server_native_v2 T4a): what the
/// standalone player's move helper hands it reaches the sink as values, and a
/// null point or name reaches nothing.
/// </summary>
public sealed unsafe class SoundEmitterBindingTests
{
    private sealed class Sink : IMoveSoundSink
    {
        public readonly List<string> Heard = [];

        public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch)
            => Heard.Add($"{entIndex} {origin} {channel} {sample} {volume} {soundLevel} {flags} {pitch}");

        public void MoveSoundScript(int entIndex, Vector origin, string soundName)
            => Heard.Add($"{entIndex} {origin} {soundName}");
    }

    private sealed class SystemSink : ISoundSystemSink
    {
        public readonly List<string> Precached = [];
        public bool InitAnswer;
        public int Inits;

        public bool SystemInit()
        {
            Inits++;
            return InitAnswer;
        }

        public void PrecacheScriptSound(string soundName) => Precached.Add(soundName);
    }

    private readonly Sink _sink = new();
    private readonly SystemSink _system = new();

    private SoundEmitterBinding Binding() => new(new Crossings(), _sink, _system);

    [Fact]
    public void SystemInitIsTheSystemsAnswerAsOneOrZero()
    {
        _system.InitAnswer = true;
        Assert.Equal((byte)1, Binding().ServerSoundSystemInit());
        _system.InitAnswer = false;
        Assert.Equal((byte)0, Binding().ServerSoundSystemInit());
        Assert.Equal(2, _system.Inits);
    }

    [Fact]
    public void AScriptPrecacheArrivesAsItsNameAndANullOneReachesNothing()
    {
        byte* name = (byte*)Marshal.StringToCoTaskMemUTF8("BaseEntity.EnterWater");
        try
        {
            var binding = Binding();
            binding.ServerPrecacheScriptSound(name);
            binding.ServerPrecacheScriptSound(null);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name);
        }

        Assert.Equal(["BaseEntity.EnterWater"], _system.Precached);
    }

    [Fact]
    public void AMoveSoundArrivesAsItsValues()
    {
        float* xyz = stackalloc float[3] { 1, 2, 3 };
        byte* sample = (byte*)Marshal.StringToCoTaskMemUTF8("player/pl_fallpain1.wav");
        try
        {
            Binding().ServerMoveSound(1, xyz, 4, sample, 0.7f, 70, 2, 95);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)sample);
        }

        Assert.Equal([$"1 {new Vector(1, 2, 3)} 4 player/pl_fallpain1.wav 0.7 70 2 95"], _sink.Heard);
    }

    [Fact]
    public void AMoveScriptArrivesAsItsValues()
    {
        float* xyz = stackalloc float[3] { 4, 5, 6 };
        byte* name = (byte*)Marshal.StringToCoTaskMemUTF8("Player.Swim");
        try
        {
            Binding().ServerMoveSoundScript(2, xyz, name);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name);
        }

        Assert.Equal([$"2 {new Vector(4, 5, 6)} Player.Swim"], _sink.Heard);
    }

    [Fact]
    public void ANullPointOrNameReachesNothing()
    {
        float* xyz = stackalloc float[3];
        byte* name = (byte*)Marshal.StringToCoTaskMemUTF8("Player.Swim");
        try
        {
            var binding = Binding();
            binding.ServerMoveSoundScript(2, null, name);
            binding.ServerMoveSoundScript(2, xyz, null);
            binding.ServerMoveSound(1, null, 0, name, 1f, 75, 0, 100);
            binding.ServerMoveSound(1, xyz, 0, null, 1f, 75, 0, 100);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name);
        }

        Assert.Empty(_sink.Heard);
    }
}
