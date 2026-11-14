using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Ai.Senses;
using TF2.Descent.Server.Horde.Session;

namespace TF2.Descent.Server.Horde.Senses;

/// <summary>
/// The horde's ear on the server's sound emitter (plan_descent_logic HO5,
/// plan_tf2_rpg B.3): every sound the server emits, after its recipients are
/// final, classified by name (<see cref="HordeSoundKinds"/>), carried as far
/// as the engine carries it (<c>2 · SOUND_NORMAL_CLIP_DIST / attenuation</c>, the
/// PAS's reach for its sound level), queued for the session's next tick. The
/// robots are not players, so the recipient set is not read: a robot hears by
/// the room graph's walk, not by the PAS.
/// </summary>
[SideService(Side.Server, As = [typeof(ISoundEmitHook)])]
public sealed class HordeHearingTap(HordeSession session) : ISoundEmitHook, IOrderedHook
{
    public const int Order = ModStages.First + 520;

    public int HookOrder => Order;

    /// <summary>A sound's reach in units at <paramref name="soundLevel"/> (dB); a sound with no attenuation reaches across any level.</summary>
    public static float Reach(int soundLevel)
    {
        float attn = SoundRecipients.SoundLevelToAttenuation(soundLevel);
        return attn <= 0f ? 1e6f : 2f * SoundRecipients.NormalClipDistance / attn;
    }

    public void OnEmitted(in EmittedSound sound)
    {
        SoundKind kind = HordeSoundKinds.Classify(sound.SoundName);
        if (HordeSoundKinds.Heard(kind))
            session.Heard(kind, sound.Origin, Reach(sound.SoundLevel), sound.Owner);
    }
}

/// <summary>
/// The horde's senses as a member of the robot hit chain (plan_descent_logic
/// 3.8): a hit is damage for its robot's brain and pain for its pack (HO5).
/// After the tally (<see cref="RobotHitTally"/>, order 0) and before the
/// blast's push (<c>Motion.RobotBlastPush</c>), so a brain is told of the hit
/// that then throws its body.
/// </summary>
[SideService(Side.Server)]
public sealed class HordeHitSense(HordeSession session) : IRobotHitSink, IOrderedHook
{
    public const int Order = ModStages.First + 515;

    public int HookOrder => Order;

    public void OnRobotHit(in RobotHit hit) => session.Hit(hit);
}

/// <summary>A hero's death, for the robots that saw it (B.3's HeroDied): its seat is its player index.</summary>
[SideService(Side.Server)]
public sealed class HordeHeroDeaths(HordeSession session) : IPlayerDeathHook, IOrderedHook
{
    public const int Order = ModStages.First + 521;

    public int HookOrder => Order;

    public void OnPlayerDied(PlayerDeath death) => session.HeroDied(death.EntIndex, death.Attacker.EntryIndex);
}
