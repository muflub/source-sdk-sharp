using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A rule for how much of one ammo type one player may carry: a mod's
/// per-player carry limit where the ammo row has one for everybody.
///
/// <para>
/// TF2 is why: a class's script names its own maxima (the class file's
/// <c>AmmoMax</c> block, <c>CTFPlayerClassData::ParseData</c>), so a soldier and
/// a heavy holding the same shotgun carry different reserves. The base
/// <c>CBaseCombatCharacter::GiveAmmo</c> clamps to the ammo definition's one
/// <c>MaxCarry(iAmmoIndex)</c>, which is <see cref="AmmoType.MaxCarry"/> and
/// the answer when no rule speaks; TF's player asks
/// <c>CTFPlayer::GetMaxAmmo</c> instead (<c>tf_player_shared.cpp</c>: the
/// class's maximum, then the attribute hooks), a per-player question.
/// </para>
///
/// <para>
/// A <b>CHAIN</b>, folded: every rule in <see cref="IOrderedHook.HookOrder"/>
/// order (ties refused) is handed the answer so far — the row's own first —
/// and returns the next, so a class limit and an item that raises it compose
/// without either knowing of the other. Register with the plain
/// <c>[SideService(Side.X)]</c> mark on BOTH sides: the client's HUD shows the
/// same limit the server clamps to.
/// </para>
/// </summary>
public interface IAmmoCarryRule
{
    /// <summary>
    /// The most of <paramref name="ammo"/> the player may carry, given
    /// <paramref name="maxCarry"/>, the answer so far. Return it unchanged for
    /// no opinion.
    /// </summary>
    /// <param name="playerIndex">
    /// The player's entity index, which is the same number on both sides.
    /// </param>
    /// <param name="ammo">The ammo row the limit is for.</param>
    /// <param name="maxCarry">The limit before this rule: the row's, or an earlier rule's.</param>
    int MaxCarry(int playerIndex, in AmmoType ammo, int maxCarry);
}

/// <summary>
/// One side's carry limits: <see cref="AmmoType.MaxCarry"/> folded through the
/// registered <see cref="IAmmoCarryRule"/>s. What every place the core clamps or
/// fills a reserve asks (the server's give and pickup, the fill-to-max commands,
/// the client's HUD limit).
/// </summary>
public sealed class AmmoCarry
{
    private readonly IHooks<IAmmoCarryRule>? _rules;
    private readonly ILog? _log;

    /// <summary>
    /// The limits over <paramref name="rules"/>, collected on the first ask (so
    /// after the mods register). Null is no rules: every limit is the row's.
    /// </summary>
    public AmmoCarry(IHooks<IAmmoCarryRule>? rules = null, ILog? log = null)
    {
        _rules = rules;
        _log = log;
    }

    /// <summary>No rules: every player carries the row's own limit.</summary>
    public static AmmoCarry Stock() => new();

    /// <summary>
    /// The most of <paramref name="ammo"/> the player at
    /// <paramref name="playerIndex"/> may carry. A rule that throws is named
    /// and skipped, and the limit before it stands; a negative answer is read
    /// as zero (nothing may be carried), never as "no limit".
    /// </summary>
    public int MaxFor(int playerIndex, in AmmoType ammo)
    {
        int max = ammo.MaxCarry;

        if (_rules is null)
            return max;

        foreach (var rule in _rules.All)
        {
            try
            {
                max = Math.Max(0, rule.MaxCarry(playerIndex, in ammo, max));
            }
            catch (Exception e)
            {
                _log?.Warning($"[SourceSharp] ammo carry rule {rule.GetType().FullName} threw: {e.Message}; "
                            + $"{ammo.Name} keeps {max}\n");
            }
        }

        return max;
    }
}
