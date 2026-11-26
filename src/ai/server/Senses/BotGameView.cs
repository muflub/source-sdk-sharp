using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Senses;

/// <summary>
/// A thing that is not a player, as the game draws it this tick (plan_bots_teams_tf2 C.3): its
/// entity, the facts every client is drawn of it, and how far its body reaches round its origin
/// (the box a client is sent it by, and a sight line ends in).
/// </summary>
public readonly record struct ThingRead(EntityHandle Entity, ThingFacts Facts, float Reach);

/// <summary>
/// What a game's client draws past what every Source game draws (plan_bots_teams_tf2 C.2, C.3),
/// the game's half of a bot's view: its own HUD's game rows, each sighted player as the bot's
/// client renders them (a disguise, a cloak), the things that are not players, and the map's
/// objectives as the HUD shows them. The binding of one game publishes it through
/// <see cref="IBotGame.View"/>; the generic senses never name a game.
/// </summary>
public interface IBotGameView
{
    /// <summary>The bot's own HUD, <paramref name="read"/> with the game's rows filled: its class, conditions and metal.</summary>
    SelfState Hud(EntityHandle bot, in SelfState read);

    /// <summary>
    /// What a client at <paramref name="viewer"/>'s eyes draws of <paramref name="them"/>, written
    /// over <paramref name="facts"/> (team, class, conditions, the weapon seen, how much of them shows);
    /// false when it draws nothing of them at all.
    /// </summary>
    bool Render(EntityHandle viewer, EntityHandle them, ref ActorFacts facts);

    /// <summary>
    /// The actor <paramref name="viewer"/>'s client takes <paramref name="them"/> (actor <paramref name="real"/>) for: an alias
    /// (<see cref="ActorId.Alias"/>) while the game draws them as someone they are not (TF2's disguised Spy, to his
    /// enemies), one per disguise and the same for every viewer it fools; <paramref name="real"/> otherwise
    /// (plan_bots_teams_tf2 C.3, the Spy's honesty fix).
    /// </summary>
    ActorId ShownAs(EntityHandle viewer, EntityHandle them, ActorId real);

    /// <summary>The things that are not players the game draws this tick, for every client alike, into <paramref name="into"/> (cleared first).</summary>
    void Things(List<ThingRead> into);

    /// <summary>What a client at <paramref name="viewer"/>'s eyes is shown of a thing, written over <paramref name="facts"/>; false when it shows nothing.</summary>
    bool RenderThing(EntityHandle viewer, ref ThingFacts facts);

    /// <summary>The map's objectives as every HUD shows them now; null for a game with none.</summary>
    ObjectiveKnowledge? Objectives { get; }

    /// <summary>
    /// Whether the game tells the bots its kill feed itself (<see cref="IBotKillFeed"/>: TF2's, from the notices its clients'
    /// feed shows, a feign and a silent kill among them); false, and every engine death is every bot's feed line.
    /// </summary>
    bool OwnKillFeed { get; }

    /// <summary>What <paramref name="bot"/>'s own HUD shows past its <see cref="SelfState"/> (its loadout, its meters): null for a game with none.</summary>
    GameHud? HudOf(EntityHandle bot);

    /// <summary>
    /// Whether the game holds <paramref name="bot"/>'s body still, as its player sees on the HUD and feels on
    /// the keys (TF2's pre-round freeze): its follower then counts no stall and hops at nothing.
    /// </summary>
    bool Frozen(EntityHandle bot);
}

/// <summary>
/// The view of a game that draws nothing past the engine's (HL2MP's): the HUD and every actor as
/// read, no things, no objectives.
/// </summary>
public sealed class PlainBotGameView : IBotGameView
{
    public SelfState Hud(EntityHandle bot, in SelfState read) => read;

    public bool Render(EntityHandle viewer, EntityHandle them, ref ActorFacts facts) => true;

    public ActorId ShownAs(EntityHandle viewer, EntityHandle them, ActorId real) => real;

    public void Things(List<ThingRead> into) => into.Clear();

    public bool RenderThing(EntityHandle viewer, ref ThingFacts facts) => true;

    public ObjectiveKnowledge? Objectives => null;

    public bool OwnKillFeed => false;

    public GameHud? HudOf(EntityHandle bot) => null;

    public bool Frozen(EntityHandle bot) => false;
}
