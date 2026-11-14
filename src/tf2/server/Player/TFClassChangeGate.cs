using SourceSharp.Abi;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// Whether a player may choose a class. Asked by the class a player chooses
/// himself (<c>joinclass</c>, which the class menu sends:
/// <see cref="TFJoinClassCommand.Choose"/>) before anything changes; never by
/// the class the game picks for him (<see cref="TFJoinClassCommand.JoinClass"/>,
/// a mode placing a player, a dev bot). A further mod whose players' class is
/// decided elsewhere (a character's) refuses the others.
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>, registered by
/// <see cref="TFClassChangeRegistrar"/>): every gate is asked, and one refusal
/// refuses. TF2 registers none, so in TF2's own game every class the limit
/// allows may be chosen. A gate that throws is named on the log and refuses
/// nothing.
/// </para>
/// </summary>
public interface ITFClassChangeGate
{
    /// <summary>
    /// False refuses <paramref name="cls"/> to <paramref name="entIndex"/>,
    /// with <paramref name="reason"/>: the line the player is told (a
    /// localisation token, whose <c>%s1</c> is the refused class's name).
    /// </summary>
    bool MayChoose(int entIndex, TFClass cls, out string reason);
}

/// <summary>The gates' one answer, for the player's own class choice (<see cref="ITFClassChangeGate"/>).</summary>
[SideService(Side.Server)]
public sealed class TFClassChangeAccess(IHooks<ITFClassChangeGate> gates, ILog log)
{
    /// <summary>Whether every gate lets <paramref name="entIndex"/> choose <paramref name="cls"/>; the first refusing gate's reason when not.</summary>
    public bool MayChoose(int entIndex, TFClass cls, out string reason)
    {
        foreach (var gate in gates.All)
        {
            try
            {
                if (!gate.MayChoose(entIndex, cls, out reason))
                    return false;
            }
            catch (Exception e)
            {
                log.Warning($"[TF2/Player] class change gate {gate.GetType().FullName} threw: {e.Message}\n");
            }
        }

        reason = string.Empty;
        return true;
    }
}

/// <summary>The class change's chain (<see cref="ITFClassChangeGate"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TFClassChangeRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFClassChangeGate>();
}
