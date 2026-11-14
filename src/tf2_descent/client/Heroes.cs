using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Descent.Shared;

namespace TF2.Descent.Client;

/// <summary>
/// The client's half of <c>tf2_descent_hero</c> (plan_descent_logic HE2): one
/// per hero, every client holds every hero's. It draws nothing; it is read
/// through <see cref="IHeroStates"/>.
/// </summary>
[ClientStandaloneEntity(HeroNetState.ClassName)]
public sealed class ClientHeroEntity(IEngineEntityRender render) : ClientStandaloneEntity(render)
{
    public override string ClassName => HeroNetState.ClassName;

    /// <summary>The hero's networked state.</summary>
    [NetState] public HeroNetState State = new();
}

/// <summary>
/// One hero as a client knows it, from its <c>tf2_descent_hero</c> entity:
/// what party frames and the owner's HUD draw. Every number is the whole
/// number the server sent (<see cref="HeroNetState"/>). <see cref="ClassId"/>
/// is a TF2 class number (3 is the Soldier). <see cref="DownedEndTick"/> is 0
/// when not downed.
/// </summary>
public sealed record HeroState(
    int PlayerIndex, int ClassId, int Level, int Xp, int LevelXp, int NextLevelXp,
    int LifeMax, int Mana, int ManaMax, int AttributePoints, int SkillPoints,
    bool Downed, int DownedEndTick, bool Hardcore)
{
    /// <summary>How far through the level the hero is, 0..1: the XP bar's fill (L5). Full at the level cap.</summary>
    public double LevelProgress =>
        NextLevelXp <= LevelXp ? 1 : Math.Clamp((double)(Xp - LevelXp) / (NextLevelXp - LevelXp), 0, 1);

    public static HeroState Of(HeroNetState s) => new(
        s.OwnerIndex, s.ClassId, s.Level, s.Xp, s.LevelXp, s.NextLevelXp,
        s.LifeMax, s.Mana, s.ManaMax, s.AttributePoints, s.SkillPoints,
        s.Downed, s.DownedEndTick, s.Hardcore);
}

/// <summary>The heroes this client knows: what the HUD and party frames ask.</summary>
public interface IHeroStates
{
    /// <summary>The hero state of player <paramref name="playerIndex"/>, or null when the client holds none.</summary>
    HeroState? Of(int playerIndex);

    /// <summary>The local player's hero state, or null.</summary>
    HeroState? Local();

    /// <summary>Every hero this client holds, by player index.</summary>
    IReadOnlyList<HeroState> All();
}

/// <summary>
/// <see cref="IHeroStates"/> over the live <c>tf2_descent_hero</c> entities:
/// read at each ask, never cached, so a full update that re-creates the
/// entities is seen at once.
/// </summary>
[SideService(Side.Client, As = [typeof(IHeroStates)])]
public sealed class HeroStates(IClientStandalones standalones, IEngineHud hud) : IHeroStates
{
    public HeroState? Of(int playerIndex)
    {
        if (playerIndex <= 0)
            return null;

        foreach (var entity in standalones.OfClass(HeroNetState.ClassName))
        {
            if (entity is ClientHeroEntity { State.IsLive: true } hero && hero.State.OwnerIndex == playerIndex)
                return HeroState.Of(hero.State);
        }

        return null;
    }

    public HeroState? Local() => Of(hud.LocalPlayerIndex);

    public IReadOnlyList<HeroState> All() =>
        standalones.OfClass(HeroNetState.ClassName)
            .OfType<ClientHeroEntity>()
            .Where(h => h.State.IsLive && h.State.OwnerIndex > 0)
            .Select(h => HeroState.Of(h.State))
            .OrderBy(h => h.PlayerIndex)
            .ToList();
}
