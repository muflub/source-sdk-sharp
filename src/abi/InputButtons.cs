namespace SourceSharp.Abi;

/// <summary>
/// Standard Source input button bits (see public/in_buttons.h).
///
/// The whole table, to the last bit, and that is not tidiness. This list used to
/// stop at Zoom and skip IN_SCORE, which the client owns and nothing here reads
/// — so the three after it were each named one bit too low, and asking about the
/// speed key answered about the scoreboard. Nothing had used them yet, which is
/// the only reason it went unnoticed. A partial copy of an engine table invites
/// exactly that, so this one is complete.
/// </summary>
[Flags]
public enum InputButtons
{
    Attack    = 1 << 0,
    Jump      = 1 << 1,
    Duck      = 1 << 2,
    Forward   = 1 << 3,
    Back      = 1 << 4,
    Use       = 1 << 5,
    Cancel    = 1 << 6,
    Left      = 1 << 7,
    Right     = 1 << 8,
    MoveLeft  = 1 << 9,
    MoveRight = 1 << 10,
    Attack2   = 1 << 11,
    Run       = 1 << 12,
    Reload    = 1 << 13,
    Alt1      = 1 << 14,
    Alt2      = 1 << 15,

    /// <summary>Scoreboard held down. The client's, and it never reaches the server.</summary>
    Score     = 1 << 16,

    Speed     = 1 << 17,
    Walk      = 1 << 18,
    Zoom      = 1 << 19,
    Weapon1   = 1 << 20,
    Weapon2   = 1 << 21,
    Bullrush  = 1 << 22,
    Grenade1  = 1 << 23,
    Grenade2  = 1 << 24,
    Attack3   = 1 << 25,
}
