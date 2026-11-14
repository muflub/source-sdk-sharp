using System.Globalization;

using SourceSharp.Abi;

using TF2.Descent.Client.Content;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// What the hero's windows share (plan_descent_logic UI3): the line a button
// sends the server, the pointer's last report, and the item views turned into
// the screens' shape. Nothing here decides an outcome: a button asks the
// server, and what the server decided arrives in the owner's caches.
//=============================================================================

/// <summary>A line to the server as the client's own command (forwarded: the client answers none of the hero's verbs).</summary>
public interface IDescentCommandLine
{
    void Send(string line);
}

/// <summary><see cref="IDescentCommandLine"/> through the engine's console.</summary>
[SideService(Side.Client, As = [typeof(IDescentCommandLine)])]
public sealed class EngineCommandLine(IFwdEngineClient engine) : IDescentCommandLine
{
    public void Send(string line) => engine.SubmitConsoleLine(line);
}

/// <summary>
/// The row a window's pointer last reported, for as long as the pointer stays:
/// a screen reports its hovered row every frame it is hovered, and nothing
/// when the pointer leaves, so a report older than <see cref="HoldSeconds"/>
/// has lapsed.
/// </summary>
public abstract class PointerReport(IGameClock clock)
{
    /// <summary>How long a report lasts without being made again.</summary>
    public const float HoldSeconds = 0.25f;

    private float _at = float.NegativeInfinity;

    /// <summary>The reported row (meaningful while <see cref="Active"/>).</summary>
    public int Row { get; private set; } = -1;

    /// <summary>A second number the report carries (the sheet's derived plate).</summary>
    public int Other { get; private set; } = -1;

    public bool Active => clock.Now - _at <= HoldSeconds && clock.Now >= _at;

    public void Report(int row, int other = -1)
    {
        Row = row;
        Other = other;
        _at = clock.Now;
    }
}

/// <summary>The character sheet's pointer: the attribute row (<see cref="PointerReport.Row"/>) or derived plate (<see cref="PointerReport.Other"/>).</summary>
[SideService(Side.Client)]
public sealed class SheetPointer(IGameClock clock) : PointerReport(clock);

/// <summary>The inventory's pointer: a backpack cell, or an equipment slot as <c>-1 - slot</c>.</summary>
[SideService(Side.Client)]
public sealed class ItemPointer(IGameClock clock) : PointerReport(clock);

/// <summary>The skill tree's pointer: the card's skill (its index in the table).</summary>
[SideService(Side.Client)]
public sealed class SkillPointer(IGameClock clock) : PointerReport(clock);

/// <summary>The owner's item views in the screens' shape, named from the content the client embeds.</summary>
[SideService(Side.Client)]
public sealed class ItemViewNames(DescentCatalog catalog)
{
    /// <summary>The name an item reads by: its unique's, else its base's; "" for a row the client does not have.</summary>
    public string NameOf(OwnerItemView item) =>
        DescentCatalog.At(catalog.Uniques, item.Unique)?.Name ?? DescentCatalog.At(catalog.Bases, item.Base)?.Name ?? "";

    /// <summary>The base of an item, or null for a row the client does not have.</summary>
    public CatalogBase? BaseOf(OwnerItemView item) => DescentCatalog.At(catalog.Bases, item.Base);

    /// <summary>The item as a slot or a tooltip draws it.</summary>
    public DescentItemView View(OwnerItemView item)
    {
        int rarity = Math.Clamp(item.Rarity, 0, (int)DescentRarity.Australium);
        bool negative = false;
        foreach (var (affix, _) in item.Affixes)
            negative |= DescentCatalog.At(catalog.Affixes, affix)?.Cursed == true;
        bool unusable = (item.Flags & ItemViewFlags.Unusable) != 0;
        return new DescentItemView(
            NameOf(item), (DescentRarity)rarity, item.ItemLevel, item.Identified,
            item.Durability, item.MaxDurability, item.Affixes.Count,
            HasUnusual: rarity >= (int)DescentRarity.Unusual || item.Unusual >= 0,
            Broken: (item.Flags & ItemViewFlags.Broken) != 0,
            Inactive: (item.Flags & ItemViewFlags.Inactive) != 0 || unusable,
            NegativeAffix: negative);
    }

    /// <summary>
    /// An affix line as the tooltip prints it: the affix's name and its value, a TF
    /// multiplier as the percent it moves the number (1.08 is +8), any other stat as rolled.
    /// </summary>
    public DescentAffixLine Line(int affix, float value)
    {
        var row = DescentCatalog.At(catalog.Affixes, affix);
        double shown = row is { TF: true } ? (value - 1) * 100 : value;
        return new DescentAffixLine(row?.Name ?? "", (int)Math.Round(shown, MidpointRounding.AwayFromZero));
    }

    /// <summary>The family's name of an item's base, "" for none.</summary>
    public string FamilyOf(OwnerItemView item) =>
        BaseOf(item) is { Family: { } family } ? catalog.Families.FirstOrDefault(f => f.Id == family)?.Name ?? "" : "";

    /// <summary>The unusual effect's name, "" for none or hidden.</summary>
    public string EffectOf(OwnerItemView item) => DescentCatalog.At(catalog.UnusualEffects, item.Unusual)?.Name ?? "";

    /// <summary>
    /// The tooltip's requirement line: the first requirement the server says is unmet
    /// (the level first, then the base's attributes in order), else the first there is;
    /// "" for a base with none. Met is the server's word (the view's unmet bits).
    /// </summary>
    public (string Token, bool Met) Requirement(OwnerItemView item)
    {
        if (BaseOf(item) is not { } b)
            return ("", true);
        bool levelUnmet = (item.Unmet & 1) != 0;
        if (levelUnmet && b.Level > 1)
            return (DescentCatalog.LevelRequirementToken(b.Level), false);
        for (int k = 0; k < b.Requires.Count; k++)
            if ((item.Unmet & (1 << (k + 1))) != 0)
                return (DescentCatalog.RequirementToken(b.Requires[k].Attribute, b.Requires[k].Min), false);
        if (b.Requires.Count > 0)
            return (DescentCatalog.RequirementToken(b.Requires[0].Attribute, b.Requires[0].Min), true);
        if (b.Level > 1)
            return (DescentCatalog.LevelRequirementToken(b.Level), true);
        return ("", true);
    }

    /// <summary>A whole number with its sign (<c>+3</c>, <c>-0.5</c>), for a hover line's value.</summary>
    public static string Signed(double value) =>
        value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
}
