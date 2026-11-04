namespace SourceSharp.Ai;

/// <summary>One bot's threat in a recording: what it did to the people, and to everyone, a minute (ruling 2026-11-03).</summary>
public sealed class ThreatRow(ActorId bot, int points)
{
    public ActorId Bot { get; } = bot;

    /// <summary>The levels' total: 20 Easy, 35 Medium, 50 Hard, 60 Expert for a preset's draw.</summary>
    public int Points { get; } = points;

    public double DamageToPeople { get; internal set; }

    public int KillsOfPeople { get; internal set; }

    public double DamageToAll { get; internal set; }

    public int Kills { get; internal set; }

    public int Shots { get; internal set; }

    /// <summary>Frags thrown, however they left: a frag's shot is its throw.</summary>
    public int Frags { get; internal set; }

    /// <summary>Second triggers that went, by weapon kind.</summary>
    public Dictionary<int, int> AltFired { get; } = new();
}

/// <summary>
/// <c>ai-replay threat</c> (§12.3, ruling 2026-11-03: Hard's threat must not rise): per bot, the
/// damage and kills a minute it did to the people in a recording and to everyone, from the
/// recording's own hits, kill feed and second triggers. A person's serial carries the human bit.
/// </summary>
public static class AiThreat
{
    /// <summary>The bit a person's actor serial has (the binding's <c>ActorDirectory.HumanSerialBit</c>).</summary>
    public const int HumanSerialBit = 0x4000_0000;

    /// <summary>Every bot's row, and the recording's minutes by its ticks.</summary>
    public static (List<ThreatRow> Rows, double Minutes, Dictionary<int, string> Weapons) Read(Stream input)
    {
        AiRecordFormat.ReadFileHeader(input);
        var rows = new Dictionary<int, ThreatRow>();
        var weapons = new Dictionary<int, string>();
        var seenKills = new HashSet<(ActorId Victim, int Tick)>();
        int first = int.MaxValue, last = int.MinValue, frag = -1;
        while (AiRecordFormat.TryReadRecord(input, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.BotAdded:
                    ActorId bot = AiRecordFormat.ReadActor(r);
                    r.ReadUInt64();
                    byte[] levels = r.ReadBytes(r.ReadUInt16());
                    rows[bot.Slot] = new ThreatRow(bot, levels.Sum(l => l));
                    break;
                case AiRecordType.World:
                    foreach (WeaponFacts w in AiRecordFormat.ReadWorld(r).Map.Weapons.All)
                    {
                        weapons[w.Kind] = w.Name;
                        if (w.Alt.Kind == AltFireKind.Underhand || w.Name == "weapon_frag")
                            frag = w.Kind;
                    }

                    break;
                case AiRecordType.Event:
                case AiRecordType.Observed:
                    AiEvent e = AiRecordFormat.ReadEvent(r);
                    first = Math.Min(first, e.Tick.Value);
                    last = Math.Max(last, e.Tick.Value);
                    Take(in e, rows, seenKills, frag);
                    break;
            }
        }

        double minutes = last > first ? (last - first) * AiMetrics.TickSeconds / 60.0 : 0.0;
        return ([.. rows.Values.OrderBy(t => t.Bot.Slot)], minutes, weapons);
    }

    private static void Take(in AiEvent e, Dictionary<int, ThreatRow> rows, HashSet<(ActorId, int)> seenKills, int frag)
    {
        switch (e.Kind)
        {
            case AiEventKind.HitDealt when rows.TryGetValue(e.Bot.Slot, out ThreatRow? by) && by.Bot == e.Bot:
                by.DamageToAll += e.Amount;
                if ((e.Other.Serial & HumanSerialBit) != 0)
                    by.DamageToPeople += e.Amount;
                break;
            case AiEventKind.ShotFired when rows.TryGetValue(e.Bot.Slot, out ThreatRow? shooter) && shooter.Bot == e.Bot:
                if (e.Code == frag)
                    shooter.Frags++;
                else
                    shooter.Shots++;
                break;
            case AiEventKind.AltFired when rows.TryGetValue(e.Bot.Slot, out ThreatRow? alt) && alt.Bot == e.Bot:
                alt.AltFired[e.Code] = alt.AltFired.GetValueOrDefault(e.Code) + 1;
                break;
            case AiEventKind.KillFeed when seenKills.Add((e.Other, e.Tick.Value)):
                // Each bot's HUD tells it: one kill.
                if (rows.TryGetValue(e.Killer.Slot, out ThreatRow? killer) && killer.Bot == e.Killer && e.Other != e.Killer)
                {
                    killer.Kills++;
                    if ((e.Other.Serial & HumanSerialBit) != 0)
                        killer.KillsOfPeople++;
                }

                break;
        }
    }

    /// <summary>The report's text: one line a bot, and the file's own minutes.</summary>
    public static string Table(List<ThreatRow> rows, double minutes, Dictionary<int, string> weapons)
    {
        var w = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        w.WriteLine($"threat: {minutes:F2} minutes");
        foreach (ThreatRow t in rows)
        {
            string alts = string.Join(" ", t.AltFired.OrderBy(kv => kv.Key).Select(kv => $"{weapons.GetValueOrDefault(kv.Key, kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture))}:{kv.Value}"));
            w.WriteLine($"threat: points {t.Points} slot {t.Bot.Slot} damage/min people {t.DamageToPeople / minutes:F1} all {t.DamageToAll / minutes:F1} kills/min people {t.KillsOfPeople / minutes:F2} all {t.Kills / minutes:F2} shots/min {t.Shots / minutes:F1} frags/min {t.Frags / minutes:F2} alt/min {t.AltFired.Values.Sum() / minutes:F2} [{alts}]");
        }

        return w.ToString();
    }
}
