namespace SourceSharp.Abi;

/// <summary>
/// The four sound-envelope calls a managed sound that ramps a patch makes --
/// the shared half of <c>CSoundEnvelopeController</c>'s surface, so a caller
/// that only plays, ramps and destroys (FrictionSounds) runs on either side.
/// The server's <c>IServerSoundController</c> extends it; the shared
/// <c>SoundEnvelopeController</c> answers it on both sides.
/// </summary>
public interface ISoundPatchRamps
{
    /// <summary>
    /// <c>SoundDestroy</c>'s managed half: shut it down, drop its queued ramps,
    /// unlist it and forget it. The shim frees the native object afterwards.
    /// </summary>
    void Destroy(int patch);

    /// <summary>
    /// <c>Play</c>: set both ramps instantly and either clear the queue — if it
    /// was already playing — or list it and start it.
    /// </summary>
    void Play(int patch, float volume, float pitch, float startTime);

    /// <summary>Ramp the pitch to a target over <paramref name="deltaTime"/> seconds.</summary>
    void ChangePitch(int patch, float target, float deltaTime);

    /// <summary>Ramp the volume. The TARGET is clamped to 1, not the current value.</summary>
    void ChangeVolume(int patch, float target, float deltaTime);
}
