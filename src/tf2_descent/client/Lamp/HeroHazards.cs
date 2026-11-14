using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Descent.Client.Content;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Lamp;

/// <summary>The hazard kinds' ids in hazards.json's order, from the rules' own file the client embeds: what a hero's wire number names.</summary>
[SideService(Side.Client)]
public sealed class HazardKinds(DescentCatalog catalog)
{
    /// <summary>The table's name: the rules' hazards.json.</summary>
    public const string Table = "hazards";

    private IReadOnlyList<string>? _ids;

    public IReadOnlyList<string> Ids => _ids ??= Parse(catalog.Bytes(Table) ?? throw new InvalidOperationException("the client embeds no hazards.json"));

    /// <summary>The kind a hero's wire number names (its position plus one), or null for 0 or a number past the table.</summary>
    public string? Of(int wire) => wire > 0 && wire <= Ids.Count ? Ids[wire - 1] : null;

    public static IReadOnlyList<string> Parse(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.GetProperty("kinds").EnumerateArray().Select(k => k.GetProperty("id").GetString() ?? "")];
    }
}

/// <summary>Each hero's hazard number as his entity carries it (<see cref="HeroNetState.Hazard"/>), and who the local player is.</summary>
public interface IHeroHazardWire
{
    /// <summary>The hazard number of the hero in <paramref name="playerIndex"/>, or 0 when the client holds no such hero.</summary>
    int HazardOf(int playerIndex);

    /// <summary>The local player's index (0 before the client is in a level).</summary>
    int LocalPlayer { get; }
}

/// <summary>The heroes' <c>tf2_descent_hero</c> entities, read at each ask.</summary>
[SideService(Side.Client, As = [typeof(IHeroHazardWire)])]
public sealed class EngineHeroHazardWire(IClientStandalones standalones, IEngineHud hud) : IHeroHazardWire
{
    public int LocalPlayer => hud.LocalPlayerIndex;

    public int HazardOf(int playerIndex)
    {
        if (playerIndex <= 0)
            return 0;
        foreach (var entity in standalones.OfClass(HeroNetState.ClassName))
            if (entity is ClientHeroEntity { State.IsLive: true } hero && hero.State.OwnerIndex == playerIndex)
                return hero.State.Hazard;
        return 0;
    }
}

/// <summary>The hazard a hero stands in, as this client knows it: for the HUD's warning (the polish lane's) and party frames.</summary>
public interface IHeroHazards
{
    /// <summary>The hazard kind (hazards.json's id) the hero in <paramref name="playerIndex"/> stands in, or null.</summary>
    string? Of(int playerIndex);

    /// <summary>The hazard kind the local hero stands in, or null.</summary>
    string? Local { get; }
}

/// <summary><see cref="IHeroHazards"/> over the heroes' wire and the embedded kinds.</summary>
[SideService(Side.Client, As = [typeof(IHeroHazards)])]
public sealed class HeroHazards(IHeroHazardWire wire, HazardKinds kinds) : IHeroHazards
{
    public string? Of(int playerIndex) => kinds.Of(wire.HazardOf(playerIndex));

    public string? Local => Of(wire.LocalPlayer);
}

/// <summary><c>tf2_descent_hazard_cl</c>: the hazard each hero stands in, as this client holds it.</summary>
[ConCommand("tf2_descent_hazard_cl", Help = "tf2_descent_hazard_cl: the hazard the local hero (and each hero) stands in, as this client holds it.")]
public sealed class ClientHazardCommand(IHeroHazards hazards, IHeroHazardWire wire, ILog log)
{
    public void Execute()
    {
        log.Msg($"[TF2Descent/Hazard] client: local player {wire.LocalPlayer} stands in {hazards.Local ?? "nothing"}\n");
        for (int p = 1; p <= 64; p++)
            if (hazards.Of(p) is { } kind)
                log.Msg($"[TF2Descent/Hazard]   player {p} stands in {kind}\n");
    }
}
