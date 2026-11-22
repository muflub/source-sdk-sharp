using System.Collections.Concurrent;
using SourceSharp.MapGen.Descent;

namespace SourceSharp.Tests;

/// <summary>
/// Each depth's library built and dressed once per test class (a class
/// fixture: the test project allows one serial collection only, SS0062):
/// dressing a depth takes tens of seconds, and a class's facts asked for the
/// same depths again and again (the slow tier's Descent classes took 1,442 s,
/// 812 s and 662 s, at 0408b1aa6). The facts that hold a seed to the same
/// bytes build their own, never from here.
/// </summary>
public sealed class DescentLibraries
{
    private readonly ConcurrentDictionary<(int Depth, ulong Seed), Lazy<IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)>>> dressed = new();

    /// <summary>Depth <paramref name="depth"/>'s rooms for <paramref name="seed"/> as built, as dressed, and their theme (<see cref="DescentRooms.Dressed"/>).</summary>
    public IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)> Dressed(int depth, ulong seed) =>
        dressed.GetOrAdd((depth, seed), k => new(() => DescentRooms.Dressed(k.Depth, k.Seed))).Value;

    /// <summary>Depth <paramref name="depth"/>'s dressed rooms for <paramref name="seed"/> (<see cref="DescentRooms.Rooms"/>).</summary>
    public IReadOnlyList<Room> Rooms(int depth, ulong seed) => [.. Dressed(depth, seed).Select(r => r.Dressed)];

    private readonly ConcurrentDictionary<int, Lazy<IReadOnlyList<Room>>> built = new();
    private readonly ConcurrentDictionary<int, Lazy<IReadOnlyList<Host.Contracts.RoomInfo>>> pools = new();

    /// <summary>Depth <paramref name="depth"/>'s rooms at seed 0 as the shapes built them, undressed (names, kits, roles and tags are the dressed rooms').</summary>
    public IReadOnlyList<Room> Built(int depth) =>
        built.GetOrAdd(depth, d => new(() => DescentRooms.Built(d, 0))).Value;

    /// <summary>Depth <paramref name="depth"/>'s rooms as the host reads them, from their manifest.</summary>
    public IReadOnlyList<Host.Contracts.RoomInfo> Pool(int depth) =>
        pools.GetOrAdd(depth, d => new(() => DescentLevel.ReadManifest(LibraryManifestWriter.Write(DescentRooms.LibraryName(d), Built(d))).Rooms)).Value;

    /// <summary>Act I's depth <paramref name="depth"/> at seed 0, as the review's facts read it.</summary>
    public IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)> Of(int depth) => Dressed(depth, 0);
}
