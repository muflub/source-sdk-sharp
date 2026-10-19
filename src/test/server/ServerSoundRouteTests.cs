using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server side's sound members reach the managed emitter's route
/// (plan_server_native_v2 T4a) where they used to cross to shell_sound.cpp
/// and shell_soundenvelope.cpp; with no route installed they make no sound.
/// </summary>
public sealed class ServerSoundRouteTests : IDisposable
{
    private sealed class Route : IServerSoundRoute
    {
        public readonly List<string> Calls = [];

        public void EntityEmitSound(EntityHandle entity, string soundName) => Calls.Add($"Emit {entity.Value:x} {soundName}");
        public void EntityStopSound(EntityHandle entity, string soundName) => Calls.Add($"Stop {entity.Value:x} {soundName}");
        public void EmitSoundAtPosition(string soundName, Vector origin) => Calls.Add($"AtPosition {soundName} {origin}");
        public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume)
        {
            Calls.Add($"World {soundName} {channel} {volume}");
            return true;
        }
        public void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch)
            => Calls.Add($"Ambient {entIndex} {soundName} {soundLevel} {flags} {pitch}");
        public int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel)
        {
            Calls.Add($"PatchCreate {channel} {soundName} {soundLevel}");
            return 41;
        }
        public void SoundPatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption)
            => Calls.Add($"PatchEmit {patch} {emitCaption}");
        public void SoundPatchStop(int patch) => Calls.Add($"PatchStop {patch}");
        public void SoundPatchState(int patch, out int entIndex, out bool filterActive)
        {
            Calls.Add($"PatchState {patch}");
            entIndex = 17;
            filterActive = true;
        }
        public void SoundPatchFree(int patch) => Calls.Add($"PatchFree {patch}");
        public void PrecacheSound(string soundName) => Calls.Add($"Precache {soundName}");
    }

    private readonly FakeShimRoot _root = new();

    public void Dispose() => _root.Dispose();

    private EngineSide Server(Route? route)
    {
        var side = new EngineSide(Side.Server);
        side.Initialize();
        Assert.True(side.AttachShims(_root.Bind(), out string why), why);
        side.ServerSounds = route;
        return side;
    }

    private static readonly EntityHandle Door = new(64u | (1u << 16));

    [Fact]
    public void AnEntitysEmitAndStopReachTheRoute()
    {
        var route = new Route();
        EngineSide side = Server(route);

        side.EntityEmitSound(Door, "Doors.Move1");
        side.EntityStopSound(Door, "Doors.Move1");

        Assert.Equal(["Emit 10040 Doors.Move1", "Stop 10040 Doors.Move1"], route.Calls);
    }

    [Fact]
    public void APrecacheReachesTheRouteAndAnEmptyNameDoesNot()
    {
        var route = new Route();
        EngineSide side = Server(route);

        side.PrecacheSound("Doors.Move1");
        side.PrecacheSound("");

        Assert.Equal(["Precache Doors.Move1"], route.Calls);
    }

    [Fact]
    public void ThePointTheWorldAndTheAmbientEmitsReachTheRoute()
    {
        var route = new Route();
        EngineSide side = Server(route);

        side.EmitSoundAtPosition("A", new Vector(1, 2, 3));
        Assert.True(side.EmitWorldSoundScript("B", default, 6, 0.5f));
        side.EmitAmbientSound(9, default, "C", 1f, 80, 4, 90);

        Assert.Equal(3, route.Calls.Count);
        Assert.StartsWith("AtPosition A", route.Calls[0], StringComparison.Ordinal);
        Assert.Equal(["World B 6 0.5", "Ambient 9 C 80 4 90"], route.Calls.Skip(1));
    }

    [Fact]
    public void TheServersPatchesAreTheRoutes()
    {
        var route = new Route();
        EngineSide side = Server(route);

        int patch = side.SoundPatchCreate(Door, 1, "Doors.Move1", 75);
        side.SoundPatchEmit(patch, 1f, 100f, 0, 0f, true);
        side.SoundPatchState(patch, out int entIndex, out bool active);
        side.SoundPatchStop(patch);
        side.SoundPatchFree(patch);

        Assert.Equal((41, 17, true), (patch, entIndex, active));
        Assert.Equal(["PatchCreate 1 Doors.Move1 75", "PatchEmit 41 True", "PatchState 41", "PatchStop 41", "PatchFree 41"],
                     route.Calls);
    }

    [Fact]
    public void WithNoRouteTheServerMakesNoSound()
    {
        EngineSide side = Server(null);

        side.EntityEmitSound(Door, "Doors.Move1");
        side.SoundPatchState(3, out int entIndex, out bool active);

        Assert.Equal(0, side.SoundPatchCreate(Door, 1, "Doors.Move1", 75));
        Assert.False(side.EmitWorldSoundScript("B", default, 6, 0.5f));
        Assert.Equal((-1, false), (entIndex, active));
    }

    [Fact]
    public void AnEmptyNameNeverReachesTheRoute()
    {
        var route = new Route();
        EngineSide side = Server(route);

        side.EntityEmitSound(Door, string.Empty);
        side.EntityStopSound(Door, string.Empty);
        side.SoundPatchCreate(Door, 1, string.Empty, 75);

        Assert.Empty(route.Calls);
    }
}
