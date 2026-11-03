using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// What a bot knows about itself, as a client does from its own HUD and view
/// (plan_hl2mp_ai §2.4): whether it is alive or watching, where its eye is,
/// which way it looks, and what a weapon slot holds.
/// </summary>
public readonly record struct BotSelfFacts(bool Alive, bool Observer, AiVector Origin, AiVector Eye, AiAngles View);

/// <summary>The applier's reads of its own bot (§2.4): the bot's own state, never another actor's.</summary>
public interface IBotSelf
{
    BotSelfFacts Read(EntityHandle player);

    /// <summary>
    /// The usercmd's <c>weaponselect</c> for the weapon in the bot's inventory
    /// slot <paramref name="slot"/>: the weapon catalog's type + 1, as the
    /// server reads the wire (ServerModule.OnStandalonePlayerCommand, phase 0);
    /// 0, no selection, for an empty slot.
    /// </summary>
    int WeaponSelectFor(EntityHandle player, int slot);
}

/// <summary>What a bot holds, as its own HUD lists it: the weapons' classnames, by inventory slot.</summary>
public interface IBotInventory
{
    IReadOnlyList<string> Held(EntityHandle player);
}

/// <summary>
/// <see cref="IBotInventory"/> over the core's <see cref="Arsenal"/>: a held
/// weapon's entity is the core's <c>ss_weapon</c> shell, whose classname
/// says nothing of its type, so the holdings are read from the arsenal.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotInventory)])]
public sealed class ArsenalBotInventory(Arsenal arsenal) : IBotInventory
{
    public IReadOnlyList<string> Held(EntityHandle player) => [.. arsenal.Holdings(player).Select(h => h.ClassName)];
}

/// <summary><see cref="IBotSelf"/> over the engine's player and entity reads.</summary>
public sealed class EngineBotSelf(IEnginePlayers players, IEngineEntities entities, WeaponCatalog weapons, IBotInventory inventory) : IBotSelf
{
    public BotSelfFacts Read(EntityHandle player)
    {
        Vector origin = entities.GetOrigin(player);
        Vector eye = entities.GetEyePosition(player);
        Vector view = entities.GetEyeAngles(player);
        return new BotSelfFacts(
            players.IsAlive(player),
            players.GetObserverMode(player) != ObserverMode.None,
            new AiVector(origin.X, origin.Y, origin.Z),
            new AiVector(eye.X, eye.Y, eye.Z),
            new AiAngles(view.X, view.Y, view.Z));
    }

    public int WeaponSelectFor(EntityHandle player, int slot)
    {
        IReadOnlyList<string> held = inventory.Held(player);
        if ((uint)slot >= (uint)held.Count)
            return 0;

        int type = weapons.IndexOf(held[slot]);
        return type < 0 ? 0 : type + 1;
    }
}
