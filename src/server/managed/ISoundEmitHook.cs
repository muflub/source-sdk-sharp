using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One sound the server emitted, as the clients that will hear it are sent it
/// (plan_hl2mp_ai §4.2): where it is, how loud its script makes it, the name
/// it was emitted by (a script such as <c>Weapon_SMG1.Single</c>, or a raw
/// wave), the entity index it comes from, and the final recipient set: the
/// player indices after PAS, attenuation and the prediction rules.
/// </summary>
public readonly ref struct EmittedSound(Vector origin, int soundLevel, string soundName, int owner, ReadOnlySpan<int> recipients)
{
    public Vector Origin { get; } = origin;

    public int SoundLevel { get; } = soundLevel;

    public string SoundName { get; } = soundName;

    /// <summary>The entity index the sound comes from (0, the world, for a sound at a point).</summary>
    public int Owner { get; } = owner;

    /// <summary>The player indices the sound is sent to.</summary>
    public ReadOnlySpan<int> Recipients { get; } = recipients;
}

/// <summary>
/// Sees every sound the server emits, after its recipient filter is final
/// (plan_hl2mp_ai §4.2): a bot hears a sound exactly when its player index is
/// in <see cref="EmittedSound.Recipients"/>, what a client would be sent.
/// Registered implementations are called on the game thread from the sound
/// emitter, after the engine was handed the sound.
/// </summary>
public interface ISoundEmitHook
{
    void OnEmitted(in EmittedSound sound);
}

/// <summary>
/// Sees each footstep the clients make themselves (plan_hl2mp_ai §4.2): in
/// multiplayer the server sends a player's step only to the PAS less the PVS,
/// because each client in the PVS makes the step from the player's networked
/// movement by the same rules (C_BasePlayer::Simulate's UpdateStepSound). The
/// step is taken where the server's movement decides it, with the stepping
/// player as <see cref="EmittedSound.Owner"/> and the PVS's players, whose
/// clients make it, as <see cref="EmittedSound.Recipients"/>. Called on the
/// game thread.
/// </summary>
public interface IClientStepHook
{
    void OnClientStep(in EmittedSound step);
}
