using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>What one walk did: participants handled, bytes carried, and what went wrong.</summary>
/// <param name="Orphans">Carried sections no participant claimed (a renamed or removed service).</param>
/// <param name="Failed">Participants whose section would not read back, or that threw.</param>
/// <param name="Missing">Participants with no carried section (nothing was saved for them).</param>
public sealed record ReloadParticipantTally(
    int Participants, int Bytes, IReadOnlyList<string> Sections,
    IReadOnlyList<string> Orphans, IReadOnlyList<string> Failed, IReadOnlyList<string> Missing);

/// <summary>
/// The client module's walk over its <see cref="IReloadParticipant"/> services
/// (plan_client_novalve §3.8): every one saved into the side's reload store at
/// the reload save, and restored at the restore, each under
/// <see cref="Prefix"/> plus its contract's full name.
/// </summary>
public sealed class ClientReloadParticipants(ServiceContainer services, ReloadStore store, ILog log)
{
    /// <summary>The section prefix: what a carried participant section is called before its contract.</summary>
    public const string Prefix = "client.service:";

    /// <summary>The last save this build made, or null.</summary>
    public ReloadParticipantTally? LastSaved { get; private set; }

    /// <summary>The last restore this build made, or null.</summary>
    public ReloadParticipantTally? LastRestored { get; private set; }

    public static string SectionOf(Type contract) => Prefix + contract.FullName;

    public ReloadParticipantTally Save()
    {
        var sections = new List<string>();
        var failed = new List<string>();
        int bytes = 0;
        var participants = services.ResolveAllByContract<IReloadParticipant>();

        foreach (var (contract, participant) in participants)
        {
            string section = SectionOf(contract);
            try
            {
                var writer = new ReloadWriter();
                participant.Save(writer);
                byte[] carried = writer.ToArray();

                // A participant with nothing to say still carries a section: a
                // restore that finds none would report it missing.
                store.Put(section, carried.Length == 0 ? [0] : [1, .. carried], 1);
                bytes += carried.Length + 1;
                sections.Add(section);
            }
            catch (Exception e)
            {
                failed.Add(section);
                log.Warning($"[SourceSharp/Client] reload: {contract.Name} could not save ({e.Message}); " +
                            "it starts from its constructor after the reload\n");
            }
        }

        LastSaved = new ReloadParticipantTally(participants.Count, bytes, sections, [], failed, []);
        return LastSaved;
    }

    public ReloadParticipantTally Restore()
    {
        var sections = new List<string>();
        var failed = new List<string>();
        var missing = new List<string>();
        int bytes = 0;
        var participants = services.ResolveAllByContract<IReloadParticipant>();

        foreach (var (contract, participant) in participants)
        {
            string section = SectionOf(contract);
            if (store.Take(section) is not { } carried)
            {
                missing.Add(section);
                continue;
            }

            bytes += carried.Length;
            sections.Add(section);
            if (carried[0] == 0)
                continue;

            try
            {
                var reader = new ReloadReader(carried[1..]);
                participant.Restore(reader);
                if (!reader.AtEnd)
                    throw new ReloadFormatException("bytes were left over");
            }
            catch (Exception e)
            {
                failed.Add(section);
                log.Warning($"[SourceSharp/Client] reload: {contract.Name} could not restore ({e.Message}); " +
                            "it keeps what its constructor gave it\n");
            }
        }

        // What the old build carried that nothing here claims: a contract was
        // renamed or removed between the builds. Taken, so the next reload
        // does not report it again.
        var orphans = store.Sections().Select(s => s.Section)
                           .Where(s => s.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
        foreach (string orphan in orphans)
        {
            store.Take(orphan);
            log.Warning($"[SourceSharp/Client] reload: the old build carried {orphan[Prefix.Length..]} and " +
                        "no service of that contract restores it\n");
        }

        LastRestored = new ReloadParticipantTally(participants.Count, bytes, sections, orphans, failed, missing);
        return LastRestored;
    }
}
