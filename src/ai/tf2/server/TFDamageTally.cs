using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// The damage each player has dealt, for the per-class harness's measure (<c>/ai/classes</c>, P7 step 5): the mod's
/// scoreboard stats do not count damage (no lane increments <c>TFStat.Damage</c>), so the hits are tallied here as the
/// damage hooks tell them, a player's and a building's alike, a player's own blast on himself not counted. Read by the
/// route only; no bot reads it.
/// </summary>
[SideService(Side.Server)]
public sealed class TFDamageTally : IPlayerHurtHook, IEntityHurtHook, IOrderedHook
{
    /// <summary>Among the hooks: after the mods' own, a measurement.</summary>
    public int HookOrder => ModStages.First + 937;

    private readonly long[] _dealt = new long[SourceSharp.Ai.Server.Senses.VisionPass.Slots];

    public void OnPlayerHurt(in PlayerHurt hurt) => Tally(hurt.Attacker, hurt.Player, hurt.Taken);

    public void OnEntityHurt(in EntityHurt hurt) => Tally(hurt.Attacker, hurt.Victim, hurt.Taken);

    private void Tally(EntityHandle attacker, EntityHandle victim, int taken)
    {
        int slot = attacker.EntryIndex;
        if (taken <= 0 || attacker == EntityHandle.None || attacker == victim || (uint)slot >= (uint)_dealt.Length)
            return;
        _dealt[slot] += taken;
    }

    /// <summary>The damage the player in <paramref name="slot"/> has dealt to others since this service was made.</summary>
    public long Dealt(int slot) => (uint)slot < (uint)_dealt.Length ? _dealt[slot] : 0;
}
