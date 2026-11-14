using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Mode;
using TF2.Server.Player;
using TF2.Shared.Econ;

namespace TF2.Descent.Server.Skills;

/// <summary>The TF attributes a hero's skill stats hold on the player: self-blast damage, self-blast push, airborne melee crits.</summary>
public readonly record struct SkillTFAttributes(float SelfBlastDamage, float SelfBlastPush, bool CritWhileAirborne)
{
    public static SkillTFAttributes None => new(1f, 1f, false);
}

/// <summary>
/// The skill stats TF2 already has an arm for, as standing TF attributes on
/// the hero's player (<see cref="ITFPlayerAttributeSource"/>, CR5), read by
/// TF2's blast applier and damage rules as an item's would be:
/// <list type="bullet">
/// <item><c>self_blast_damage_pct</c> (Jump Training) as <c>rocket_jump_dmg_reduction</c>,
/// the attacker's own multiplier on a self blast that hurt no other player
/// (stock's Gunboats arm): 1 + pct / 100, never below 0.</item>
/// <item><c>jump_force_pct</c> (Jump Training) as <c>mult_dmgself_push_force</c>,
/// the self blast's push: 1 + pct / 100.</item>
/// <item>a triggered <c>melee_crit</c> on <c>on_melee_hit_airborne</c> scoped to a
/// family (Death From Above) as <c>crit_while_airborne</c> 1 while a weapon
/// of that family is in the hands: TF2's arm crits a hit by an attacker an
/// explosion put in the air (the Market Gardener's).</item>
/// </list>
/// The container is this source's, one a hero, provided at every manager
/// build; a changed value is what the next hook walk reads.
/// </summary>
[SideService(Side.Server)]
public sealed class SkillAttributes(IDescentModeState mode, IBotPlayers bots, ItemSchema schema)
    : ITFPlayerAttributeSource, IOrderedHook
{
    /// <summary>In TF2 Descent's skills block.</summary>
    public const int Order = ModStages.First + 620;

    public const string SelfBlastDamageClass = "rocket_jump_dmg_reduction";
    public const string SelfBlastPushClass = "mult_dmgself_push_force";
    public const string CritWhileAirborneClass = "crit_while_airborne";

    private readonly Dictionary<int, AttributeContainer> _containers = new();
    private readonly Dictionary<int, SkillTFAttributes> _set = new();

    public int HookOrder => Order;

    public AttributeContainer? AttributesOf(EntityHandle player)
    {
        if (!mode.Active || bots.IsBot(player))
            return null;
        return ContainerOf(player.EntryIndex);
    }

    /// <summary>What the hero in <paramref name="playerIndex"/> holds now.</summary>
    public SkillTFAttributes Of(int playerIndex) => _set.GetValueOrDefault(playerIndex, SkillTFAttributes.None);

    /// <summary>The hero's numbers onto its container; nothing written when they did not change.</summary>
    public void Set(int playerIndex, SkillTFAttributes want)
    {
        if (Of(playerIndex) == want)
            return;

        var container = ContainerOf(playerIndex);
        Write(container, SelfBlastDamageClass, want.SelfBlastDamage, 1f);
        Write(container, SelfBlastPushClass, want.SelfBlastPush, 1f);
        Write(container, CritWhileAirborneClass, want.CritWhileAirborne ? 1f : 0f, 0f);
        _set[playerIndex] = want;
    }

    /// <summary>The hero left: nothing stands.</summary>
    public void Forget(int playerIndex)
    {
        if (_containers.TryGetValue(playerIndex, out _))
            Set(playerIndex, SkillTFAttributes.None);
        _set.Remove(playerIndex);
    }

    private void Write(AttributeContainer container, string attributeClass, float value, float neutral)
    {
        if (schema.AttributesOfClass(attributeClass).FirstOrDefault() is not { } definition)
            return;
        if (value == neutral)
            container.Remove(definition.Defindex);
        else
            container.Set(AttributeValue.OfFloat(definition, value));
    }

    private AttributeContainer ContainerOf(int index)
    {
        if (!_containers.TryGetValue(index, out var container))
            _containers[index] = container = new AttributeContainer();
        return container;
    }
}
