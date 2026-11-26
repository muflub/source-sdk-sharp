using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Heavy's own HUD meter (P7): whether his own minigun is spinning (<c>heavy.aiming</c>, 1 or 0: his own
/// TF_COND_AIMING, which the wind-up sets and the wind-down clears, TFMinigunBehaviour). A player hears and feels his own gun
/// turn, so it is his to know; it reads nobody else. The play counts with it how often the gun is idle when the hands
/// asked for it spun (review finding 3: the applier withholds the trigger while the aim reacts).
/// <para>Registered as itself, not <c>As</c> the chain's interface: the container puts a type in every hook chain it
/// implements, and a second reader claiming <see cref="ITFHudMeters"/> stopped the server's load ("registered twice", beside
/// TFMedigunMeters). Its place in the chain is every class reader's rule (coordinator notice 1): 100 + its class number.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFHeavyMeters(ITFMapPlayers people) : ITFHudMeters, IOrderedHook
{
    public const string Aiming = "heavy.aiming";

    public int HookOrder => 100 + (int)TFClass.HeavyWeapons;

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass == TFClass.HeavyWeapons)
            into.Add(new TFMeter(Aiming, people.InCond(bot, TFCond.Aiming) ? 1f : 0f));
    }
}
