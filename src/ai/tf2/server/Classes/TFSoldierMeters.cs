using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using TF2.Shared.Player;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Soldier's own HUD meter (P7): its banner's rage, 0..100, as the charge meter over its health shows it (stock's
/// <c>m_flRageMeter</c>, networked to its owner: <see cref="TFPlayerNetState.RageMeter"/>). Read for the bot itself only,
/// never another player. Registered as itself: a registered type joins every hook chain it implements, so it reads into the
/// <see cref="ITFHudMeters"/> chain beside the medigun's (registered "as" the chain too, two would collide). Its place in the
/// chain is its own (<see cref="HookOrder"/>): two readers at one order throw at the chain's first call, every sense tick
/// (live: AiSenses.OnPostThink threw and no bot was ever given a class).
/// </summary>
[SideService(Side.Server)]
public sealed class TFSoldierMeters(ITFPlayerData players) : ITFHudMeters, IOrderedHook
{
    /// <summary>The Soldier's place among the HUD's meter readers: 100 plus its class number, every class lane's rule (coordinator notice 1, 2026-11-21).</summary>
    public int HookOrder => 100 + (int)TFClass.Soldier;

    /// <summary>The rage meter's name on the bot's HUD.</summary>
    public const string Rage = "soldier.rage";

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass == TFClass.Soldier && players.Networked(bot.EntryIndex) is { } state)
            into.Add(new TFMeter(Rage, state.RageMeter));
    }
}
