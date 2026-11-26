using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Player;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Demoman's own HUD meters (plan_bots_teams_tf2 P7; the Demoman research's A6): what his HUD shows him and nobody
/// else. <c>demoman.stickies</c> is the count of his stickybombs out, the number stock's pipe panel draws
/// (<c>CHudDemomanPipes</c>, from the launcher's own list: here <see cref="TFStickyTray.Count"/>);
/// <c>demoman.charge</c> is his shield's charge meter 0..100 (<c>m_flChargeMeter</c>, the meter under the panel) and
/// <c>demoman.heads</c> his sword's heads (<c>m_iDecapitations</c>, the panel's head count), both off his own networked
/// state, as metal is read. Read for a Demoman only. Registered as itself: the hook chain takes every registered
/// reader (<c>As = [typeof(ITFHudMeters)]</c> claims the contract for one type, and the medigun's reader holds that claim).
/// </summary>
[SideService(Side.Server)]
public sealed class TFDemomanMeters(TFStickyTray stickies, ITFPlayerData players) : ITFHudMeters, IOrderedHook
{
    public const string Stickies = "demoman.stickies", Charge = "demoman.charge", Heads = "demoman.heads";

    /// <summary>
    /// Its own place in the readers' chain, which requires each reader's to differ (the medigun's is 0): 100 and the class's
    /// number, a rule every class's reader can follow without meeting another's.
    /// </summary>
    public int HookOrder => 100 + (int)TFClass.Demoman;

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass != TFClass.Demoman)
            return;
        int index = bot.EntryIndex;
        into.Add(new TFMeter(Stickies, stickies.Count(index)));
        if (players.Networked(index) is not { } own)
            return;
        into.Add(new TFMeter(Charge, own.ChargeMeter));
        into.Add(new TFMeter(Heads, own.Decapitations));
    }
}
