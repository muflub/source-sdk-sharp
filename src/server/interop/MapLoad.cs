namespace SourceSharp.Abi;

/// <summary>
/// One map load, as managed code receives it.
///
/// <para>
/// <b>A <c>ref struct</c>, and that is the enforcement rather than a
/// convention.</b> A <c>ref struct</c> cannot be boxed, stored in a field,
/// captured by a lambda, put in a collection or held across an <c>await</c> —
/// so "this does not outlive the call" is a compile error at the point somebody
/// tries, instead of a note in a code review that a later edit walks past.
/// </para>
///
/// <para>
/// <b>The failure it prevents is the quiet one.</b> <see cref="Entities"/> is
/// the map's whole entity lump, borrowed out of engine memory that is valid for
/// the duration of <c>LevelInit</c> and no longer. Read one frame later, when
/// the engine has reused the buffer, it does not fault — it parses. A map with
/// most of its entities and a corrupt tail loads, and the symptom appears
/// somewhere else entirely.
/// </para>
///
/// <para>
/// <b>Three strings copied, one span borrowed, and the split is decided rather
/// than averaged.</b> <see cref="MapName"/>, <see cref="OldLevel"/> and
/// <see cref="Landmark"/> are short and are <i>kept</i> — the map name is read
/// all level — and copying a dozen bytes to remove a lifetime question is not a
/// trade worth reasoning about. The lump is hundreds of kilobytes and is
/// consumed inside the call, so it is the only field that borrows.
/// </para>
///
/// <para>
/// <b>Anything that must outlive the call copies explicitly, at the point that
/// needs it.</b> <c>load.Entities.ToArray()</c> is one call, and the cost is
/// then visible at the site that chose it rather than paid on every map load by
/// everybody.
/// </para>
///
/// <para>
/// <see cref="OldLevel"/> and <see cref="Landmark"/> are empty on a load that is
/// not a transition: the engine passes null there, and an empty string is the
/// same answer without a null check at every reader.
/// </para>
/// </summary>
public readonly ref struct MapLoad
{
    /// <summary>The map, as the engine named it.</summary>
    public string MapName { get; }

    /// <summary>The level being left, on a transition. Empty otherwise.</summary>
    public string OldLevel { get; }

    /// <summary>The landmark being transitioned through. Empty otherwise.</summary>
    public string Landmark { get; }

    /// <summary>
    /// The map's entity lump, BORROWED. Valid for this call only — see the type
    /// summary. Copy it with <c>ToArray()</c> if something must keep it.
    /// </summary>
    public ReadOnlySpan<byte> Entities { get; }

    public MapLoad(string mapName, ReadOnlySpan<byte> entities,
                   string oldLevel = "", string landmark = "")
    {
        MapName = mapName;
        OldLevel = oldLevel;
        Landmark = landmark;
        Entities = entities;
    }

    /// <summary>Whether this load carries a landmark, i.e. is a level transition.</summary>
    public bool HasLandmark => Landmark.Length > 0;
}
