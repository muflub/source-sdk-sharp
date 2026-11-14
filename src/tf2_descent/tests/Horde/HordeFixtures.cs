using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules;

namespace TF2.Descent.Tests.Horde;

/// <summary>Levels the horde facts are built on: rooms 512 units square in a row along X, a door between each pair.</summary>
internal static class HordeFixtures
{
    public static readonly TF2DescentRules TheRules = new();

    public const float RoomSize = 512;

    public static AiVector At(float x, float y, float z = 0) => new(x, y, z);

    /// <summary>Room <paramref name="id"/> of a row: its X span is [id × 512, (id + 1) × 512], with markers at the given offsets.</summary>
    public static HordeRoom RowRoom(int id, int markers)
    {
        float x0 = id * RoomSize;
        var m = ImmutableArray.CreateBuilder<PackMarker>();
        for (int k = 0; k < markers; k++)
            m.Add(new PackMarker(At(x0 + 128 + 256 * (k % 2), 128 + 64 * k), 90 * k));
        return new HordeRoom(id, At(x0, 0, 0), At(x0 + RoomSize, RoomSize, 256), m.ToImmutable());
    }

    /// <summary>The door between rooms <paramref name="a"/> and a + 1 of a row: on their shared wall, facing +X.</summary>
    public static HordeDoor RowDoor(int a) => new(a, a + 1, At((a + 1) * RoomSize, RoomSize / 2), At(1, 0, 0), 64);

    /// <summary><paramref name="rooms"/> rooms in a row, each with <paramref name="markers"/> markers.</summary>
    public static HordeLevel Row(int rooms, int markers = 2, ulong seed = 7, int depth = 3, HordeBoss? boss = null) =>
        HordeLevel.Create(seed, depth,
            Enumerable.Range(0, rooms).Select(i => RowRoom(i, markers)),
            Enumerable.Range(0, rooms - 1).Select(RowDoor),
            boss);

    /// <summary>The middle of room <paramref name="id"/> of a row.</summary>
    public static AiVector Centre(int id) => At(id * RoomSize + RoomSize / 2, RoomSize / 2);
}
