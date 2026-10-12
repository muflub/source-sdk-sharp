//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>One connection, in a form that outlives the assembly that parsed it.</summary>
/// <param name="Owner">The entity whose output carries it, as a bare handle value.</param>
public readonly record struct SavedConnection(
    uint Owner,
    string OutputName,
    string Target,
    string TargetInput,
    string? Parameter,
    float Delay,
    int TimesToFire,
    int IdStamp);

/// <summary>
/// The I/O half of one side's reload record: what was in flight, and what was
/// connected.
/// </summary>
public sealed class SavedIo
{
    public SavedIo(IReadOnlyList<SavedIoEvent> events, IReadOnlyList<SavedConnection> connections)
    {
        Events = events;
        Connections = connections;
    }

    /// <summary>An empty record, which is what a side with no I/O saves.</summary>
    public static SavedIo Empty { get; } =
        new(Array.Empty<SavedIoEvent>(), Array.Empty<SavedConnection>());

    public IReadOnlyList<SavedIoEvent> Events { get; }

    public IReadOnlyList<SavedConnection> Connections { get; }

    /// <summary>How much is in the record. For a report and a test.</summary>
    public int Count => Events.Count + Connections.Count;
}
