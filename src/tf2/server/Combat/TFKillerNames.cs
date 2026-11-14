using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared.Combat;

namespace TF2.Server.Combat;

/// <summary>
/// The name of what killed a player when no player did (a mod's robot or
/// monster; stock's MONOCULUS, Merasmus and the skeletons are the precedent):
/// a localisation token the client resolves, and the team whose colour the
/// name takes. A chain a mod joins; asked once a death, for a
/// <c>player_death</c> no player scored that the victim did not deal himself.
/// The first member that answers a token names the killer. Order among
/// several is <see cref="IOrderedHook"/>'s.
/// </summary>
public interface ITFKillerNames
{
    /// <summary>The token naming the killer of <paramref name="death"/> and its name's team; false when this member does not know.</summary>
    bool TryToken(in DeathNoticeFacts death, out string token, out int team);
}

/// <summary>
/// Sends the killer's name of a death no player scored (<see cref="ITFKillerNames"/>)
/// to every client, as a reliable <see cref="TFKillerName"/>, while the
/// <c>player_death</c> is filled: the event fires after every
/// <see cref="IDeathNoticeHook"/>, so the name is ahead of it in the same
/// reliable stream, and the client's kill feeds find it when the event comes.
/// With no member answering, nothing is sent and the death reads as before.
/// </summary>
[SideService(Side.Server)]
public sealed class TFKillerNameNotice(IHooks<ITFKillerNames> names, IEnginePlayers players, IUserMessageSender messages, ILog log)
    : IDeathNoticeHook, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand), after TF's own fields (+703).</summary>
    public const int Order = 1_000_000 - 1_000 + 711;

    public int HookOrder => Order;

    /// <summary>Names sent, since the module started.</summary>
    public int Sent { get; private set; }

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        if (death.Scorer != EntityHandle.None || death.Attacker == death.Victim)
            return;

        foreach (var member in names.All)
        {
            string token;
            int team;
            try
            {
                if (!member.TryToken(in death, out token, out team) || string.IsNullOrEmpty(token))
                    continue;
            }
            catch (Exception e)
            {
                log.Warning($"[TF2] killer name {member.GetType().FullName} threw: {e.Message}\n");
                continue;
            }

            messages.Send(new TFKillerName
            {
                Victim = players.GetUserId(death.Victim),
                Team = (byte)Math.Clamp(team, 0, byte.MaxValue),
                Token = token.Length > TFKillerName.MaxToken ? token[..TFKillerName.MaxToken] : token,
            }, 0, reliable: true);
            Sent++;
            return;
        }
    }
}

/// <summary>The killer-name chain's registration.</summary>
[SideService(Side.Server)]
public sealed class TFKillerNamesRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFKillerNames>();
}
