using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A rule for what model the viewmodel wears: a mod's answer of the hands
/// (or whatever else the carrier should see) before the core dresses the
/// weapon's own.
///
/// <para>
/// TF2 is why: stock's <c>CTFWeaponBase::GetViewModel</c>
/// (tf_weaponbase.cpp:651-676) answers the PLAYER CLASS's hand model for an
/// item that attaches to hands, and the weapon's own display model rides the
/// hands as a client-side viewmodel attachment. The core's default — the
/// weapon row's own <see cref="WeaponType.ViewModel"/> — is stock's
/// <c>GetTFWpnData().szViewModel</c> arm, the answer for a weapon whose item
/// does not attach. The chain is the seam; the rule that knows which items
/// attach, and whose hands they are, lives in the mod, which reads the item
/// schema and the player's class itself.
/// </para>
///
/// <para>
/// A <b>CHAIN</b>, folded: every rule in <see cref="IOrderedHook.HookOrder"/>
/// order (ties refused) is handed the answer so far — null for the core's
/// default — and may replace it, so a role-wide hands model and a per-item
/// override compose without either knowing of the other. A rule answers null
/// for no opinion. Registered on the SERVER: the model is the entity's
/// networked state, not predicted input, and the client's copy wears what the
/// server wore.
/// </para>
/// </summary>
public interface IViewmodelModelRule
{
    /// <summary>
    /// The model the viewmodel wears, or <paramref name="chosen"/> unchanged
    /// (null included) for no opinion. The core dresses
    /// <see cref="WeaponType.ViewModel"/> when the fold answers null.
    /// </summary>
    /// <param name="playerIndex">The player being dressed.</param>
    /// <param name="type">The weapon row being deployed.</param>
    /// <param name="chosen">The answer so far: null until a rule speaks.</param>
    string? Model(int playerIndex, WeaponType type, string? chosen);
}

/// <summary>
/// The viewmodel's model choice: the weapon row's own folded through the
/// registered <see cref="IViewmodelModelRule"/>s, at the one place the server
/// dresses the viewmodel — the Arsenal's deploy.
/// </summary>
public sealed class ViewmodelModel
{
    private readonly IHooks<IViewmodelModelRule>? _rules;
    private readonly ILog? _log;

    /// <param name="rules">The registered rules, collected on the first ask (so after the mods register). Null is no rules: the row's own model stands.</param>
    public ViewmodelModel(IHooks<IViewmodelModelRule>? rules = null, ILog? log = null)
    {
        _rules = rules;
        _log = log;
    }

    /// <summary>No rules: the row's own model, unchanged.</summary>
    public static ViewmodelModel Stock() => new();

    /// <summary>
    /// The model to dress the viewmodel in, folded through the rules; null is
    /// the core's default (<see cref="WeaponType.ViewModel"/>). A rule that
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
                _log?.Warning($"[SourceSharp] viewmodel model rule {rule.GetType().FullName} threw: {e.Message}; "
                            + $"{type.Name} keeps {chosen ?? "the row's own"}\n");
            }
        }

        return chosen;
    }
}
