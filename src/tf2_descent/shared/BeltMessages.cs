using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

//=============================================================================
// The hero's belt and his gear's wear, to his own client only (plan_descent_logic
// HE8, HE9; the HUD's belt strip and gear warning, UI2). Each is sent when it
// changes and again when the client arrives or the hero spawns, so a level
// change or a full update leaves the client right.
//=============================================================================

/// <summary>The hero's belt as his own client draws it (HE8).</summary>
public interface IBeltHook
{
    void OnBelt(BeltView belt);
}

/// <summary>
/// A received belt: each slot's potion base and name ("" for an empty slot) and how many it
/// holds, the server tick the shared cooldown ends (0: ready) and the cooldown's whole length
/// in ticks (what a sweep's fraction is measured against).
/// </summary>
public sealed record BeltView(IReadOnlyList<string> Bases, IReadOnlyList<string> Names, IReadOnlyList<int> Counts,
                              int ReadyTick, int CooldownTicks);

/// <summary>
/// The belt on the wire: four slots' base ids joined by ',', their names joined by '|',
/// their counts, and the cooldown's end and length in server ticks. Bounded well under the
/// engine's 255 bytes.
/// </summary>
public partial struct Belt
{
    public const int Slots = 4;

    /// <summary>The longest base id carried (the potions' longest is 13).</summary>
    public const int MaxId = 16;

    /// <summary>The longest name carried; a longer name is cut.</summary>
    public const int MaxName = 20;

    [UserMessageString(MaxLength = Slots * (MaxId + 1))]
    public partial string Bases { get; set; }

    [UserMessageString(MaxLength = Slots * (MaxName + 1))]
    public partial string Names { get; set; }

    [UserMessageArray(MaxCount = Slots)]
    public partial byte[] Counts { get; set; }

    public partial int ReadyTick { get; set; }

    public partial int CooldownTicks { get; set; }

    /// <summary>The belt as the client reads it: missing slots empty.</summary>
    public BeltView View()
    {
        string[] ids = (Bases ?? "").Split(',');
        string[] names = (Names ?? "").Split('|');
        var bases = new string[Slots];
        var named = new string[Slots];
        var counts = new int[Slots];
        for (int i = 0; i < Slots; i++)
        {
            bases[i] = i < ids.Length ? ids[i] : "";
            named[i] = i < names.Length ? names[i] : "";
            counts[i] = Counts is { } c && i < c.Length ? c[i] : 0;
        }

        return new BeltView(bases, named, counts, ReadyTick, CooldownTicks);
    }

    /// <summary>A name as the wire carries it: '|' and ',' taken out, cut to <see cref="MaxName"/>.</summary>
    public static string Cut(string name, int max)
    {
        string clean = name.Replace("|", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        return clean.Length <= max ? clean : clean[..max];
    }
}

[UserMessage(Name)]
public sealed class BeltMessage(IHooks<IBeltHook>? hooks) : IUserMessage<Belt>
{
    public BeltMessage() : this(null) { }

    public const string Name = "belt";

    public void OnReceived(Belt message)
    {
        var view = message.View();
        hooks?.Each(nameof(IBeltHook.OnBelt), h => h.OnBelt(view));
    }
}

/// <summary>How worn a piece of gear is, as the gear warning shows it (the rules' wear level, and an empty slot).</summary>
public enum GearWearLevel : byte
{
    Empty,
    Ok,
    Low,
    Broken,
    Unusable,
}

/// <summary>The hero's gear wear as his own client draws the warning (HE9).</summary>
public interface IGearWearHook
{
    void OnGearWear(GearWearView wear);
}

/// <summary>One equipment slot's wear: its slot id ("primary"), the item's name, the level, and durability out of maximum when it was sent.</summary>
public sealed record GearWearView(string Slot, string Name, GearWearLevel Level, int Durability, int MaxDurability);

/// <summary>
/// One equipment slot's wear on the wire: sent when its level crosses a line (ok, low,
/// broken, unusable, empty), never for each shot.
/// </summary>
public partial struct GearWear
{
    public const int MaxSlot = 16;

    public const int MaxName = 40;

    [UserMessageString(MaxLength = MaxSlot)]
    public partial string Slot { get; set; }

    [UserMessageString(MaxLength = MaxName)]
    public partial string Name { get; set; }

    public partial byte Level { get; set; }

    public partial short Durability { get; set; }

    public partial short MaxDurability { get; set; }

    public GearWearView View() =>
        new(Slot ?? "", Name ?? "", Enum.IsDefined((GearWearLevel)Level) ? (GearWearLevel)Level : GearWearLevel.Empty, Durability, MaxDurability);
}

[UserMessage(Name)]
public sealed class GearWearMessage(IHooks<IGearWearHook>? hooks) : IUserMessage<GearWear>
{
    public GearWearMessage() : this(null) { }

    public const string Name = "gear_wear";

    public void OnReceived(GearWear message)
    {
        var view = message.View();
        hooks?.Each(nameof(IGearWearHook.OnGearWear), h => h.OnGearWear(view));
    }
}
