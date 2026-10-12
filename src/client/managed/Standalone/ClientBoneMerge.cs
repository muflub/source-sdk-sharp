using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The console door onto EF_BONEMERGE's follower half (Phase 10).
///
/// <para>
/// The mechanism belongs to the entities that will ship with it — a weapon's
/// world model in a hand, a viewmodel — and those wait on the standalone
/// player. What exists NOW is the merge itself, and a capability that only a
/// future entity can exercise is invisible whether it works or not. This
/// command is what makes it visible: point any live standalone entity at any
/// client entity index and watch cs_attachments report its points following
/// the source's skeleton.
/// </para>
///
/// <para>
/// Selection is by classname filter rather than handle, because handles are
/// what the machine passes and a person at a console has a classname. What the
/// filter picks is decided here and tested; the command is a console name and
/// two writes.
/// </para>
/// </summary>
public static class ClientBoneMerge
{
    /// <summary>
    /// The low 16 bits of a handle are the entity's index — ENT_ENTRY_MASK,
    /// the same packing ResolveStandaloneClient unpacks on the native side.
    /// </summary>
    private const uint EntryMask = 0xFFFF;

    /// <summary>
    /// The entity a filter names. A number is an ENTITY INDEX and matches
    /// exactly — three barrels of one class is the normal state of a test, and
    /// a filter that can only say "barrel" can only ever reach the first.
    /// Anything else matches the first live entity whose classname contains
    /// it, case-insensitively. Null for no match — and null for an EMPTY
    /// filter, deliberately: "merge the first entity I have" is never what
    /// anyone meant, and a command that guesses is worse than one that asks.
    /// </summary>
    public static ClientStandaloneEntity? Pick(IReadOnlyList<ClientStandaloneEntity> live, string filter)
    {
        if (filter.Length == 0)
            return null;

        bool byIndex = int.TryParse(filter, out int index);

        foreach (var entity in live)
        {
            bool matches = byIndex
                ? (entity.Handle.Value & EntryMask) == (uint)index
                : entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase);

            if (matches)
                return entity;
        }

        return null;
    }
}

/// <summary>
/// The console name for <see cref="ClientBoneMerge"/>.
/// </summary>
[ConCommand("cs_bonemerge",
            Help = "Merge a standalone entity's bones onto a client entity. " +
                   "cs_bonemerge <classname filter> <source entindex, -1 to stop>")]
public sealed class BoneMergeCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public BoneMergeCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !int.TryParse(parts[1], out int sourceIndex))
        {
            _log.Msg("[SourceSharp/Client] usage: cs_bonemerge <classname filter> <source entindex, -1 to stop>\n");
            return;
        }

        var entity = ClientBoneMerge.Pick(_standalones.Live(), parts[0]);

        if (entity is null)
        {
            _log.Warning($"[SourceSharp/Client] cs_bonemerge: no live standalone entity matches '{parts[0]}'\n");
            return;
        }

        _render.SetBoneMergeSource(entity.Handle, sourceIndex);

        _log.Msg(sourceIndex >= 0
            ? $"[SourceSharp/Client] {entity.ClassName} now draws with entity {sourceIndex}'s bones\n"
            : $"[SourceSharp/Client] {entity.ClassName} back on its own bones\n");
    }
}
