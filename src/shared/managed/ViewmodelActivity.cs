using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A rule for what activity a viewmodel animation is looked up as: a mod's
/// translation of the machine's base activity name before the model is asked.
///
/// <para>
/// TF2 is why: the machine sends stock's base viewmodel activities
/// (ACT_VM_IDLE, ACT_VM_PRIMARYATTACK, ...), and TF's arms models tag theirs
/// per weapon ROLE instead — the hand model carries every role's set, and
/// stock's <c>CTFWeaponBase::TranslateViewmodelHandActivity</c> (called from
/// <c>CBaseCombatWeapon::SendWeaponAnim</c>, basecombatweapon_shared.cpp:1216)
/// rewrites the name through <c>s_viewmodelacttable</c>
/// (tf_weaponbase.cpp:4292) before the model is asked. Without it the lookup
/// misses the model's own names and the hands never animate. The chain is the
/// translation's seam; the rule that knows the roles lives in the mod, which
/// also reads the weapon — the role's source — itself.
/// </para>
///
/// <para>
/// A <b>CHAIN</b>, folded: every rule in <see cref="IOrderedHook.HookOrder"/>
/// order (ties refused) is handed the name so far — the machine's base first
/// — and returns the next, so a role table and a per-item override compose
/// without either knowing of the other. Register with the plain
/// <c>[SideService(Side.X)]</c> mark on BOTH sides: the sequence number the
/// machine writes is PREDICTED state, so both sides must translate the same
/// ask the same way.
/// </para>
/// </summary>
public interface IViewmodelActivityRule
{
    /// <summary>
    /// The activity name to ask the viewmodel's model for. Return
    /// <paramref name="activity"/> unchanged for no opinion.
    /// </summary>
    /// <param name="playerIndex">
    /// The player whose viewmodel is asking — the same number on both sides.
    /// The weapon in their hands is the rule's own read.
    /// </param>
    /// <param name="anim">The animation kind the machine is sending.</param>
    /// <param name="activity">The name so far: the machine's base, or an earlier rule's answer.</param>
    string Translate(int playerIndex, WeaponAnim anim, string activity);
}

/// <summary>
/// The viewmodel's activity translation: the machine's base names folded
/// through the registered <see cref="IViewmodelActivityRule"/>s, at the ONE
/// place every ask goes through — the server's arms and the client's replay
/// arms both resolve through <see cref="WeaponAnimNames.Resolve"/>, and the
/// answer is predicted state.
/// </summary>
public sealed class ViewmodelActivity
{
    private readonly IHooks<IViewmodelActivityRule>? _rules;
    private readonly ILog? _log;

    /// <param name="rules">The registered rules, collected on the first ask (so after the mods register). Null is no rules: every name is the machine's own.</param>
    public ViewmodelActivity(IHooks<IViewmodelActivityRule>? rules = null, ILog? log = null)
    {
        _rules = rules;
        _log = log;
    }

    /// <summary>No rules: the machine's own names, unchanged.</summary>
    public static ViewmodelActivity Stock() => new();

    /// <summary>
    /// The name to ask the model for, folded through the rules. A rule that
    /// throws is named and skipped, and the name before it stands.
    /// </summary>
    public string Translate(int playerIndex, WeaponAnim anim, string activity)
    {
        string asked = activity;

        if (_rules is null)
            return asked;

        foreach (var rule in _rules.All)
        {
            try
            {
                asked = rule.Translate(playerIndex, anim, asked);
            }
            catch (Exception e)
            {
                _log?.Warning($"[SourceSharp] viewmodel activity rule {rule.GetType().FullName} threw: {e.Message}; "
                            + $"{activity} keeps {asked}\n");
            }
        }

        return asked;
    }
}
