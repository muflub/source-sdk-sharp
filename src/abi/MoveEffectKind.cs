namespace SourceSharp.Abi;

/// <summary>
/// The effects the movement loop triggers — <c>SourceSharpMoveEffect</c>.
///
/// <para>
/// One slot with a kind rather than nine slots, because they are all "tell the
/// world something happened" and only one of them has an answer.
/// </para>
/// </summary>
public enum MoveEffectKind
{
    ResetTouchList = 0,
    AddToTouched,
    StartSoundSwim,
    SetAnimation,

    /// <summary>The one with an answer: false when the landing killed them.</summary>
    FallingDamage,

    PlayStepSound,
    UpdateStepSound,
    Splash,
    Rumble,
    ResetDuckLatched,
    SetPlayerSurface,
}
