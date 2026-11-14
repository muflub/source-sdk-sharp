using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Mode;
using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// Every hero in the game by player index: each <see cref="HeroComponent"/>
/// enters at its attach and leaves at its detach. What other server code asks
/// for a hero's stats, mana and sheet.
/// </summary>
[SideService(Side.Server)]
public sealed class Heroes
{
    private readonly Dictionary<int, HeroComponent> _byIndex = new();

    /// <summary>The hero in <paramref name="playerIndex"/>, or null (no such player, a bot, or no Descent mode).</summary>
    public HeroComponent? Of(int playerIndex) => _byIndex.GetValueOrDefault(playerIndex);

    /// <summary>Every hero, in no particular order.</summary>
    public IReadOnlyCollection<HeroComponent> All => _byIndex.Values;

    internal void Enter(int playerIndex, HeroComponent hero) => _byIndex[playerIndex] = hero;

    internal void Leave(int playerIndex, HeroComponent hero)
    {
        if (_byIndex.TryGetValue(playerIndex, out var held) && ReferenceEquals(held, hero))
            _byIndex.Remove(playerIndex);
    }
}

/// <summary>
/// TF2 Descent's per-player components: a <see cref="HeroComponent"/> for a
/// player who is a hero (the Descent mode is on and the player is no bot).
/// </summary>
[SideService(Side.Server)]
public sealed class HeroComponents(IDescentModeState mode, IBotPlayers bots, IEngineEntities entities,
                                   IEntityDirectory directory, HeroSheets sheets, Heroes heroes,
                                   IHooks<IHeroContributionSource> contributions, IHooks<IHeroStatsWatcher> watchers,
                                   ITFPlayerResupply resupply, ILog log)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>After TF2's band, in TF2 Descent's hero block.</summary>
    public const int Order = ModStages.First + 200;

    public int HookOrder => Order;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        if (!mode.Active || bots.IsBot(player))
            yield break;

        yield return new HeroComponent(entities, directory, sheets, heroes, contributions, watchers, resupply, log);
    }
}

/// <summary>A hero's class as the mode places it: the sheet's, or the default before there is a sheet.</summary>
[SideService(Side.Server, As = [typeof(IHeroClass)])]
public sealed class SheetHeroClass(HeroSheets sheets) : IHeroClass
{
    public TFClass ClassOf(int entIndex) =>
        sheets.Of(entIndex) is { } sheet && HeroClasses.ToTF(sheet.ClassId) is var cls && cls != TFClass.Undefined
            ? cls
            : DescentMode.DefaultClass;
}

/// <summary>The rules' hero class ids as TF2's classes.</summary>
public static class HeroClasses
{
    /// <summary>The TF2 class of a rules class id ("soldier", "heavy", ...); <see cref="TFClass.Undefined"/> for none.</summary>
    public static TFClass ToTF(string classId) => classId switch
    {
        "scout" => TFClass.Scout,
        "soldier" => TFClass.Soldier,
        "pyro" => TFClass.Pyro,
        "demoman" => TFClass.Demoman,
        "heavy" => TFClass.HeavyWeapons,
        "engineer" => TFClass.Engineer,
        "medic" => TFClass.Medic,
        "sniper" => TFClass.Sniper,
        "spy" => TFClass.Spy,
        _ => TFClass.Undefined,
    };
}

/// <summary>The registrations of the hero's chains.</summary>
[SideService(Side.Server)]
public sealed class HeroRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<IHeroContributionSource>();
        registry.AddHooks<IHeroStatsWatcher>();
    }
}
