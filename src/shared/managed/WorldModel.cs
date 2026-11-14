using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A rule for what model the carried weapon's entity wears — the model every
/// other player sees — before the core dresses the row's own.
///
/// <para>
/// TF2 is why: stock's <c>CTFWeaponBase::GetWorldModel</c>
/// (tf_weaponbase.cpp:681-701) answers the HOLDER's item's display model —
/// its <c>model_world</c>, else its per-class player model — so an unlock
/// sharing a weapon class wears its own shape while the row stays the base
/// item's. The core's default — <see cref="WeaponType.WorldModel"/> — is
/// stock's fall-through arm, the answer for a holder whose item names
/// nothing. The chain is the seam; the rule that reads the item schema and
/// the holder lives in the mod.
/// </para>
///
/// <para>
/// A <b>CHAIN</b>, folded exactly as <see cref="IViewmodelModelRule"/> is:
/// every rule in <see cref="IOrderedHook.HookOrder"/> order (ties refused) is
/// handed the answer so far — null for the core's default — and may replace
/// it; a rule answers null for no opinion. Registered on the SERVER: the
/// model is the entity's networked state, not predicted input.
/// </para>
/// </summary>
public interface IWorldModelRule
{
    /// <summary>
    /// The model the carried weapon wears, or <paramref name="chosen"/>
    /// unchanged (null included) for no opinion. The core dresses
    /// <see cref="WeaponType.WorldModel"/> when the fold answers null.
    /// </summary>
    /// <param name="playerIndex">The holder.</param>
    /// <param name="type">The weapon row carried.</param>
    /// <param name="chosen">The answer so far: null until a rule speaks.</param>
    string? Model(int playerIndex, WeaponType type, string? chosen);
}

/// <summary>
/// The carried weapon's model choice: the weapon row's own folded through the
/// registered <see cref="IWorldModelRule"/>s, at the one place the server
/// dresses the weapon — the Arsenal's deploy.
/// </summary>
public sealed class WorldModel
{
    private readonly IHooks<IWorldModelRule>? _rules;
    private readonly ILog? _log;

    /// <param name="rules">The registered rules, collected on the first ask (so after the mods register). Null is no rules: the row's own model stands.</param>
    public WorldModel(IHooks<IWorldModelRule>? rules = null, ILog? log = null)
    {
        _rules = rules;
        _log = log;
    }

    /// <summary>No rules: the row's own model, unchanged.</summary>
    public static WorldModel Stock() => new();

    /// <summary>
    /// The model to dress the carried weapon in, folded through the rules; null
    /// is the core's default (<see cref="WeaponType.WorldModel"/>). A rule that
    /// throws is named and skipped, and the answer before it stands.
    /// </summary>
    public string? Model(int playerIndex, WeaponType type)
    {
        string? chosen = null;

        if (_rules is null)
            return chosen;

        foreach (var rule in _rules.All)
        {
            try
            {
                chosen = rule.Model(playerIndex, type, chosen);
            }
            catch (Exception e)
            {
                _log?.Warning($"[SourceSharp] world model rule {rule.GetType().FullName} threw: {e.Message}; "
                            + $"{type.Name} keeps {chosen ?? "the row's own"}\n");
            }
        }

        return chosen;
    }
}
