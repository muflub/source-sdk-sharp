using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Horde.Senses;

/// <summary>Whether a hero landed from a blast jump since it was last asked (plan_tf2_rpg B.3: a landing is a sound the horde hears).</summary>
public interface IHeroLandings
{
    /// <summary>Asked once a tick per hero: true the first tick it is no longer blast jumping.</summary>
    bool Landed(EntityHandle hero);
}

/// <summary>
/// TF2's blast jump state as the horde hears it: <c>TF_COND_BLASTJUMPING</c>
/// is on while a player is in a rocket or sticky jump and goes when it lands
/// (TFBlastJumpState.Clear, tf_player.cpp:8710); its going is the landing.
/// </summary>
[SideService(Side.Server, As = [typeof(IHeroLandings)])]
public sealed class TFHeroLandings(ITFPlayerConditions conditions) : IHeroLandings
{
    private readonly bool[] _jumping = new bool[256];

    public bool Landed(EntityHandle hero)
    {
        int i = hero.EntryIndex;
        if ((uint)i >= (uint)_jumping.Length)
            return false;
        bool now = conditions.InCond(hero, TFCond.Blastjumping);
        bool landed = _jumping[i] && !now;
        _jumping[i] = now;
        return landed;
    }
}
