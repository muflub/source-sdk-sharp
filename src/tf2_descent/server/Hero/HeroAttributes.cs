using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Mode;
using TF2.Server.Player;
using TF2.Shared.Econ;
using TF2.Shared.Movement;

namespace TF2.Descent.Server.Hero;

/// <summary>The TF attributes a hero's stats hold on the player, as set: the additive maximum health and the speed factor.</summary>
public readonly record struct HeroTFAttributes(float AddMaxHealth, float MoveSpeedFactor);

/// <summary>
/// A hero's outgoing stats as TF attributes on the player (HE3): one
/// <see cref="ITFPlayerAttributeSource"/> holding, for each hero, an
/// <c>add_maxhealth</c> that makes TF's maximum health the sheet's
/// <see cref="HeroStats.MaxLife"/> (the difference to the class's own
/// <c>health_max</c>) and a <c>mult_player_movespeed</c> that makes TF's
/// maximum speed the sheet's <see cref="HeroStats.MoveSpeed"/> (the ratio to
/// the class's <c>speed_max</c>).
///
/// <para>
/// The container is this source's, one a player slot, and stays provided to
/// the player's manager across kit rebuilds (TF2 asks at every build); the
/// values are set again when the hero's stats change
/// (<see cref="IHeroStatsWatcher"/>) and at every build (the class may have
/// changed). A set value is what TF's next walk reads, and it reaches every
/// client on the player's state: no respawn, and no heal — health stays where
/// it is (a caller that wants a refill asks
/// <see cref="HeroComponent.RefillLifeAndMana"/>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroAttributes(IDescentModeState mode, IBotPlayers bots, Heroes heroes, IHeroClassBase classes,
                                   ITFAttributeLookup lookup)
    : ITFPlayerAttributeSource, IHeroStatsWatcher, IOrderedHook
{
    /// <summary>After TF2's band, in TF2 Descent's stats block.</summary>
    public const int Order = ModStages.First + 300;

    /// <summary>The maximum health's hook class (<c>CTFPlayer::GetMaxHealthForBuffing</c>).</summary>
    public const string MaxHealthClass = "add_maxhealth";

    /// <summary>The schema's additive maximum health attribute, preferred among those of the class.</summary>
    public const string MaxHealthName = "max health additive bonus";

    /// <summary>The speed's hook class (<c>TeamFortress_CalculateMaxSpeed</c>).</summary>
    public const string MoveSpeedClass = TFMaxSpeed.MoveSpeedAttrib;

    /// <summary>The schema's speed bonus attribute, preferred among those of the class.</summary>
    public const string MoveSpeedName = "move speed bonus";

    private readonly Dictionary<int, AttributeContainer> _containers = new();
    private readonly Dictionary<int, HeroTFAttributes> _set = new();

    public int HookOrder => Order;

    public AttributeContainer? AttributesOf(EntityHandle player)
    {
        if (!mode.Active || bots.IsBot(player))
            return null;

        int index = player.EntryIndex;
        var container = ContainerOf(index);
        Fill(index, player, container);
        return container;
    }

    public void StatsChanged(HeroComponent hero)
    {
        if (hero.PlayerIndex >= 0)
            Fill(hero.PlayerIndex, hero.Player, ContainerOf(hero.PlayerIndex));
    }

    public void HeroLeft(int playerIndex)
    {
        if (_containers.TryGetValue(playerIndex, out var container))
            Clear(playerIndex, container);
    }

    /// <summary>What the hero in <paramref name="playerIndex"/> holds now; null when nothing is set.</summary>
    public HeroTFAttributes? Of(int playerIndex) => _set.TryGetValue(playerIndex, out var set) ? set : null;

    /// <summary>The TF attributes that make <paramref name="classBase"/>'s numbers the stats': the difference in health, the ratio in speed.</summary>
    public static HeroTFAttributes For(HeroStats stats, TFClassBase classBase) =>
        new((float)(Math.Round(stats.MaxLife, MidpointRounding.AwayFromZero) - classBase.MaxHealth),
            classBase.MaxSpeed > 0f ? (float)(stats.MoveSpeed / classBase.MaxSpeed) : 1f);

    private AttributeContainer ContainerOf(int index)
    {
        if (!_containers.TryGetValue(index, out var container))
            _containers[index] = container = new AttributeContainer();
        return container;
    }

    /// <summary>The hero's numbers onto the container; nothing set while there is no hero, no stats or no class.</summary>
    private void Fill(int index, EntityHandle player, AttributeContainer container)
    {
        if (heroes.Of(index) is not { Stats: { } stats } || classes.Of(player) is not { } classBase)
        {
            Clear(index, container);
            return;
        }

        var want = For(stats, classBase);
        if (_set.TryGetValue(index, out var held) && held == want)
            return;

        // A value set only when it changed: each Set bumps the container's
        // version, which is what sends the player's attributes to the clients.
        if (lookup.Find(MaxHealthClass, MaxHealthName) is { } health)
            container.Set(AttributeValue.OfFloat(health, want.AddMaxHealth));
        if (lookup.Find(MoveSpeedClass, MoveSpeedName) is { } speed)
            container.Set(AttributeValue.OfFloat(speed, want.MoveSpeedFactor));
        _set[index] = want;
    }

    private void Clear(int index, AttributeContainer container)
    {
        if (!_set.Remove(index))
            return;

        if (lookup.Find(MaxHealthClass, MaxHealthName) is { } health)
            container.Remove(health.Defindex);
        if (lookup.Find(MoveSpeedClass, MoveSpeedName) is { } speed)
            container.Remove(speed.Defindex);
    }
}
