using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Server;
using TF2.Server.Player;
using TF2.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// A TF2 bot picks its class as a player does at the class menu (plan_bots_teams_tf2 C.4, P6 step 7):
/// on a playing team with no class asked for yet, it sends the mod's own <c>joinclass</c> word as
/// that player's (<see cref="IBotClientCommand"/>), and the mod's rules decide (class limits, the
/// instant spawn of a first pick). For now every bot is a Soldier (<see cref="Class"/>); C.4's
/// composition and the change on respawn come with P7. A pick the mod refused (a class limit) is
/// asked again after <see cref="RetrySeconds"/>.
/// </summary>
[SideService(Side.Server)]
public sealed class TFBotClassPick(AiBots bots, IEnginePlayers players, TFPlayerCarriers carriers, IBotClientCommand commands, IGameClock clock)
    : IFrameLifecycle
{
    /// <summary>The class every TF2 bot picks until P7's composition.</summary>
    public const TFClass Class = TFClass.Soldier;

    /// <summary>A refused pick is asked again after this long.</summary>
    public const float RetrySeconds = 3f;

    /// <summary>After the TF2 AI's level service (First + 650).</summary>
    public int FrameOrder => ModStages.First + 655;

    private readonly Dictionary<int, float> _askedAt = new();

    /// <summary>Picks sent, for the facts and the log.</summary>
    public int Picks { get; private set; }

    public void OnPostThink(float frameTime)
    {
        float now = clock.Now;
        foreach (AiBot bot in bots.Bots)
        {
            EntityHandle player = bots.Applier.PlayerOf(bot.Id);
            if (player == EntityHandle.None)
                continue;
            TFClass desired = carriers.Part<TFPlayerClass>(player.EntryIndex)?.Desired ?? TFClass.Undefined;
            int slot = bot.Id.Slot;
            if (Word(players.GetTeam(player), desired) is not { } word
                || (_askedAt.TryGetValue(slot, out float at) && now - at < RetrySeconds && now >= at))
                continue;
            _askedAt[slot] = now;
            Picks++;
            commands.Run(player, word);
        }
    }

    /// <summary>The word a bot on <paramref name="team"/> that has asked for <paramref name="desired"/> sends: <c>joinclass soldier</c> on a playing team with nothing asked for, else none.</summary>
    public static string? Word(int team, TFClass desired)
        => team is (int)TFTeam.Red or (int)TFTeam.Blue && desired == TFClass.Undefined
            ? $"{TFJoinClassCommand.Verb} {TFClassNames.Raw(Class)}"
            : null;
}
