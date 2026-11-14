using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.OwnerViews;

/// <summary>
/// THE SHEET'S NUMBERS, TO ITS OWNER (plan_descent_logic UI3): what the
/// character sheet shows beside the hero state's scalars, as the server
/// derives it (<see cref="SheetNumbers"/>): each attribute's base, now and
/// cap, what one more point adds to maximum life and mana (so the sheet's
/// preview of staged points is the server's arithmetic, not the client's),
/// the resistances and their cap, armour, crit chance, and the weapon in
/// hand's damage range (<see cref="IHeroWeaponDamage"/>).
///
/// <para>
/// Sent when the hero's stats change (<see cref="IHeroStatsWatcher"/>), when
/// the weapon in hand does (looked at every <see cref="WeaponCheckFrames"/>
/// frames), and again at each spawn (the client forgets it at a level's
/// end); never twice the same bytes in a row, never to anyone but the owner.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class OwnerSheetSender(
    IUserMessageSender sender,
    Heroes heroes,
    IHooks<IHeroContributionSource> contributions,
    IHeroWeaponDamage weapons,
    TF2DescentRules rules)
    : IHeroStatsWatcher, IPlayerSpawnHook, IFrameLifecycle, IClientLifecycle, IOrderedHook
{
    /// <summary>In TF2 Descent's owner-views block (UI3).</summary>
    public const int Order = ModStages.First + 1110;

    /// <summary>How often the weapon in hand is looked at for a changed damage range.</summary>
    public const int WeaponCheckFrames = 8;

    public int HookOrder => Order;

    public int ClientLifecycleOrder => Order;

    public int FrameOrder => Order;

    private readonly Dictionary<int, byte[]> _sent = new();
    private readonly Dictionary<int, (int, int)?> _ranges = new();
    private int _frame;
    private uint? _fingerprint;

    private uint Fingerprint => _fingerprint ??= ContentKeys.Fingerprint(rules.Content);

    public void StatsChanged(HeroComponent hero) => Send(hero, always: false);

    public void HeroLeft(int playerIndex) => Forget(playerIndex);

    public void OnClientDisconnected(int entIndex) => Forget(entIndex);

    public void OnSpawning(EntityHandle player, float now) { }

    public void OnSpawned(EntityHandle player, float now)
    {
        if (heroes.All.FirstOrDefault(h => h.Player == player) is { } hero)
            Send(hero, always: true);
    }

    public void OnPostThink(float frameTime)
    {
        if (++_frame % WeaponCheckFrames != 0)
            return;
        foreach (var hero in heroes.All)
        {
            // Only the weapon in hand is looked at here; the rest changes with the stats.
            var range = weapons.RangeOf(hero);
            if (_ranges.TryGetValue(hero.PlayerIndex, out var last) && last == range)
                continue;
            _ranges[hero.PlayerIndex] = range;
            Send(hero, always: false);
        }
    }

    private void Forget(int playerIndex)
    {
        _sent.Remove(playerIndex);
        _ranges.Remove(playerIndex);
    }

    /// <summary>The numbers of <paramref name="hero"/>'s sheet now; null with no sheet or stats.</summary>
    public SheetNumbers? Of(HeroComponent hero)
    {
        if (hero.Sheet is not { } sheet || hero.Stats is not { } stats || rules.Heroes.FindClass(sheet.ClassId) is not { } cls)
            return null;

        var content = rules.Content;
        var attributes = content.Attributes.Rows.Take(SheetNumbers.MaxRows).ToList();
        var given = hero.PlayerIndex > 0
            ? contributions.All.SelectMany(c => c.ContributionsOf(hero.PlayerIndex, sheet)).ToList()
            : [];
        var range = weapons.RangeOf(hero);

        return new SheetNumbers
        {
            Fingerprint = Fingerprint,
            Base = [.. attributes.Select(a => SheetNumbers.Short(sheet.Base(a.Id)))],
            NowTenths = [.. attributes.Select(a => SheetNumbers.Short(stats.Now(a.Id) * 10))],
            Cap = [.. attributes.Select(a => SheetNumbers.Short(cls.Attributes.FirstOrDefault(b => b.Attribute == a.Id)?.Cap ?? 0))],
            LifePerPointTenths = [.. attributes.Select(a => SheetNumbers.Short(OnePointMore(sheet, a.Id, given, stats, s => s.MaxLife) * 10))],
            ManaPerPointTenths = [.. attributes.Select(a => SheetNumbers.Short(OnePointMore(sheet, a.Id, given, stats, s => s.MaxMana) * 10))],
            ResistPercent = [.. content.DamageTypes.Rows.Take(SheetNumbers.MaxRows).Select(t => SheetNumbers.Short(stats.Resist(t.Id) * 100))],
            ResistCap = (byte)Math.Clamp(Math.Round(content.Hero.ResistCap), 0, byte.MaxValue),
            Armour = SheetNumbers.Short(stats.Armour),
            CritTenths = SheetNumbers.Short(stats.CritChance * 1000),
            DamageMin = SheetNumbers.Short(range?.Min ?? 0),
            DamageMax = SheetNumbers.Short(range?.Max ?? 0),
        };
    }

    /// <summary>What one more point in <paramref name="attribute"/> changes <paramref name="pick"/> by, by the rules' own stats.</summary>
    private double OnePointMore(HeroSheet sheet, string attribute, IReadOnlyList<Contribution> given, HeroStats now,
                                Func<HeroStats, double> pick)
    {
        var plus = sheet with { Attributes = sheet.Attributes.SetItem(attribute, sheet.Base(attribute) + 1) };
        return pick(rules.Heroes.Stats(plus, given)) - pick(now);
    }

    private void Send(HeroComponent hero, bool always)
    {
        if (hero.PlayerIndex <= 0 || Of(hero) is not { } numbers)
            return;

        Span<byte> buffer = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int n = numbers.Write(buffer);
        if (n < 0)
            return;
        var bytes = buffer[..n];
        if (!always && _sent.TryGetValue(hero.PlayerIndex, out var last) && bytes.SequenceEqual(last))
            return;
        _sent[hero.PlayerIndex] = bytes.ToArray();
        sender.Send(numbers, hero.PlayerIndex);
    }
}
