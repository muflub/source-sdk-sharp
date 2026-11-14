using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>
/// What a map's objective asks of a team (plan_bots_teams_tf2 C.6): kinds a
/// bot reasons about, not any game's entities. A flag is <see cref="Carry"/>,
/// its capture zone <see cref="Deliver"/>, a control point <see cref="Capture"/>,
/// a cart <see cref="Push"/>, a king-of-the-hill point <see cref="Hold"/>.
/// </summary>
public enum ObjectiveKind : byte
{
    Capture = 0,
    Carry,
    Deliver,
    Push,
    Block,
    Hold,
    Defend,
}

/// <summary>Where an objective stands, as the HUD shows it (plan_bots_teams_tf2 C.6).</summary>
public enum ObjectiveState : byte
{
    Locked = 0,
    Open,
    Capturing,
    Carried,
    Dropped,
    Home,
    Moving,
    RollingBack,
    Won,
}

/// <summary>
/// One objective as every player's HUD shows it (plan_bots_teams_tf2 C.6):
/// map objectives are public knowledge. Teams are the game's team numbers.
/// </summary>
/// <param name="Id">The binding's id for it, the same for the level.</param>
/// <param name="Kind">What it asks.</param>
/// <param name="OwnerTeam">The team that owns it (0 for none).</param>
/// <param name="MayAct">The teams that may act on it now, one bit per team number (<c>1 &lt;&lt; team</c>).</param>
/// <param name="State">Where it stands.</param>
/// <param name="Position">Where it is: a point, a flag (at home or carried), a cart.</param>
/// <param name="Progress">A capture's or a push's progress, 0..1.</param>
/// <param name="CarrierSlot">The slot of the player carrying it, 0 for none.</param>
/// <param name="UnlockIn">Seconds until it unlocks, 0 when open.</param>
/// <param name="TimeLeft">Its own timer, seconds: a dropped flag's return, a hill's.</param>
/// <param name="Setup">The round's setup is running: the gates are shut.</param>
public readonly record struct Objective(int Id, ObjectiveKind Kind, int OwnerTeam, int MayAct, ObjectiveState State, AiVector Position,
                                        float Progress = 0f, int CarrierSlot = 0, float UnlockIn = 0f, float TimeLeft = 0f, bool Setup = false);

/// <summary>
/// A map's objectives as a game's binding publishes them with its
/// <see cref="MapKnowledge"/> (plan_bots_teams_tf2 C.6). HL2MP has none; a
/// bot reads it as data and the core knows kinds, not games.
/// </summary>
public sealed class ObjectiveKnowledge(ImmutableArray<Objective> all, int version = 0)
{
    public ImmutableArray<Objective> All { get; } = all.IsDefault ? [] : all;

    /// <summary>Counts each change the binding published.</summary>
    public int Version { get; } = version;
}
