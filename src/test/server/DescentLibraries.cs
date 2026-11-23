using System.Collections.Concurrent;
using SourceSharp.MapGen.Descent;

namespace SourceSharp.Tests;

/// <summary>
/// Each depth's library built and dressed once per test run, shared by every
/// class that takes this fixture: dressing a depth takes tens of seconds, and
/// a cache per class (a class fixture alone) dressed the same depths once for
/// each of the 17 classes, all at once, which on CI's four cores made single
/// facts run 10 to 44 minutes (PR #43). The fixture stays a class fixture (the
/// test project allows one serial collection only, SS0062); its caches are the
/// process's. The facts that hold a seed to the same bytes build their own,
/// never from here.
/// </summary>
public sealed class DescentLibraries
{
    private static readonly ConcurrentDictionary<(int Depth, ulong Seed), Lazy<IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)>>> dressed = new();

    /// <summary>Depth <paramref name="depth"/>'s rooms for <paramref name="seed"/> as built, as dressed, and their theme (<see cref="DescentRooms.Dressed"/>).</summary>
    public IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)> Dressed(int depth, ulong seed) =>
        dressed.GetOrAdd((depth, seed), k => new(() => DescentRooms.Dressed(k.Depth, k.Seed))).Value;

    /// <summary>Depth <paramref name="depth"/>'s dressed rooms for <paramref name="seed"/> (<see cref="DescentRooms.Rooms"/>).</summary>
    public IReadOnlyList<Room> Rooms(int depth, ulong seed) => [.. Dressed(depth, seed).Select(r => r.Dressed)];

    private static readonly ConcurrentDictionary<int, Lazy<IReadOnlyList<Room>>> built = new();
    private static readonly ConcurrentDictionary<int, Lazy<IReadOnlyList<Host.Contracts.RoomInfo>>> pools = new();

    /// <summary>Depth <paramref name="depth"/>'s rooms at seed 0 as the shapes built them, undressed (names, kits, roles and tags are the dressed rooms').</summary>
    public IReadOnlyList<Room> Built(int depth) =>
        built.GetOrAdd(depth, d => new(() => DescentRooms.Built(d, 0))).Value;

    /// <summary>Depth <paramref name="depth"/>'s rooms as the host reads them, from their manifest.</summary>
    public IReadOnlyList<Host.Contracts.RoomInfo> Pool(int depth) =>
        pools.GetOrAdd(depth, d => new(() => DescentLevel.ReadManifest(LibraryManifestWriter.Write(DescentRooms.LibraryName(d), Built(d))).Rooms)).Value;

    /// <summary>Act I's depth <paramref name="depth"/> at seed 0, as the review's facts read it.</summary>
    public IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)> Of(int depth) => Dressed(depth, 0);
}
