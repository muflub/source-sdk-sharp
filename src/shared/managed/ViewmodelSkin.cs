using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A rule for what skin the viewmodel wears: a mod's answer of the team
/// livery (or an item's own) before the deploy dresses the hands.
///
/// <para>
/// TF2 is why: stock's <c>CTFViewModel::GetSkin</c> (tf_viewmodel.cpp:362-398)
/// answers the item's per-team skin (<c>CEconItemView::GetSkin</c>) and, when
/// that is none and the weapon script has <c>HasTeamSkins_Viewmodel</c>, RED 0
/// / BLU 1 — the reason a BLU medic's hands are blue-grey and a RED soldier's
/// are not. The core's default — nothing — is stock's answer for a weapon
/// with neither. The chain is the seam; the rule that knows the item schema
/// and the weapon's script lives in the mod, which reads the player's team
/// itself.
/// </para>
///
/// <para>
/// A <b>CHAIN</b>, folded: every rule in <see cref="IOrderedHook.HookOrder"/>
/// order (ties refused) is handed the answer so far — null for the core's
/// default — and may replace it. A rule answers null for no opinion, and a
/// fold of null leaves the viewmodel's skin exactly as it is: an attack's own
/// write (the crossbow's glowing bolt, <c>CWeaponCrossbow::SetSkin</c>) made
/// it and keeps standing. Registered on the SERVER, beside the model chain:
/// the skin is the entity's networked state, and the attachment's is stock's
/// <c>m_hViewmodelAttachment-&gt;m_nSkin = vm-&gt;GetSkin()</c>.
/// </para>
/// </summary>
public interface IViewmodelSkinRule
{
    /// <summary>
    /// The skin the viewmodel wears, or <paramref name="chosen"/> unchanged
    /// (null included) for no opinion.
    /// </summary>
    /// <param name="playerIndex">The player being dressed.</param>
    /// <param name="type">The weapon row being deployed.</param>
    /// <param name="chosen">The answer so far: null until a rule speaks.</param>
    int? Skin(int playerIndex, WeaponType type, int? chosen);
}

/// <summary>
/// The viewmodel's skin choice: folded through the registered
/// <see cref="IViewmodelSkinRule"/>s at the one place the server dresses the
/// viewmodel — the Arsenal's deploy.
/// </summary>
public sealed class ViewmodelSkin
{
    private readonly IHooks<IViewmodelSkinRule>? _rules;
    private readonly ILog? _log;

    /// <param name="rules">The registered rules, collected on the first ask (so after the mods register). Null is no rules: nobody dresses the skin.</param>
    public ViewmodelSkin(IHooks<IViewmodelSkinRule>? rules = null, ILog? log = null)
    {
        _rules = rules;
        _log = log;
    }

    /// <summary>No rules: nobody dresses the skin, and what an attack wrote stands.</summary>
    public static ViewmodelSkin Stock() => new();

    /// <summary>
    /// The skin to dress the viewmodel and its attachment in, folded through
    /// the rules; null is no opinion (the deploy then touches nothing). A rule
    /// that throws is named and skipped, and the answer before it stands.
    /// </summary>
    public int? Skin(int playerIndex, WeaponType type)
    {
        int? chosen = null;

        if (_rules is null)
            return chosen;

        foreach (var rule in _rules.All)
        {
            try
            {
                chosen = rule.Skin(playerIndex, type, chosen);
            }
            catch (Exception e)
            {
                _log?.Warning($"[SourceSharp] viewmodel skin rule {rule.GetType().FullName} threw: {e.Message}; "
                            + $"{type.Name} keeps {(chosen?.ToString() ?? "what it wears")}\n");
            }
        }

        return chosen;
    }
}
