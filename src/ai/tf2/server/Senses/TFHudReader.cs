using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// A reader of meters a TF2 bot's HUD shows (plan_bots_teams_tf2 P7): a class's lane registers one in its own file
/// (<c>[SideService(Side.Server)]</c>, registered as itself) for the meters its class reads (rage, a sticky count, a
/// sentry's panel), each a <see cref="TFMeter"/> named <c>class.meter</c>. A chain: every reader is asked for every
/// bot, on the main thread, and adds only what that bot's HUD shows. Each reader holds its own order
/// (<c>IOrderedHook</c>, 100 + its class): two at one order make the chain throw on every sense tick.
/// </summary>
public interface ITFHudMeters
{
    /// <summary>Adds to <paramref name="into"/> the meters <paramref name="bot"/>'s HUD shows now.</summary>
    void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into);
}

/// <summary>The chains the TF2 AI's readers register in (<see cref="ITFHudMeters"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TFHudRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFHudMeters>();
}

/// <summary>
/// The medigun's meters, as a HUD shows them (plan_bots_teams_tf2 P7, the Medic's A6 and the review's healed-by ask): to
/// a Medic, his charge 0..1 (<c>medic.charge</c>), whether it is being released (<c>medic.releasing</c>, 1 or 0) and the
/// beam's patient's slot (<c>medic.patient</c>, 0 for none); to anyone a Medic's beam is on, the healer's slot
/// (<c>healed.by</c>, the Healer panel and the beam every player sees) and his charge (<c>healed.charge</c>, which his
/// target ID shows).
/// </summary>
[SideService(Side.Server)]
public sealed class TFMedigunMeters(ITFMedigunWeapons mediguns, IEngineEnvironment environment) : ITFHudMeters
{
    public const string Charge = "medic.charge", Releasing = "medic.releasing", Patient = "medic.patient", HealedBy = "healed.by", HealerCharge = "healed.charge";

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        int index = bot.EntryIndex;
        if (playerClass == TFClass.Medic)
        {
            into.Add(new TFMeter(Charge, mediguns.ChargeLevel(index)));
            into.Add(new TFMeter(Releasing, mediguns.IsReleasing(index) ? 1f : 0f));
            into.Add(new TFMeter(Patient, Math.Max(0, mediguns.HealingTargetIndex(index))));
        }

        int max = environment.Globals.IsAvailable ? environment.Globals.MaxClients : 0;
        for (int medic = 1; medic <= max; medic++)
        {
            if (medic != index && mediguns.HealingTargetIndex(medic) == index)
            {
                into.Add(new TFMeter(HealedBy, medic));
                into.Add(new TFMeter(HealerCharge, mediguns.ChargeLevel(medic)));
                return;
            }
        }
    }
}

/// <summary>
/// Builds a TF2 bot's <see cref="TFHud"/> each sense tick (plan_bots_teams_tf2 P7): its kit off its own loadout
/// (<see cref="ITFOwnKit"/>, kept while the kit's version holds), the meters its HUD shows (the
/// <see cref="ITFHudMeters"/> chain) and the teammates the HUD shows it (its beam's patient's health; to a Medic, the
/// callers: <see cref="TFHud.CallingShare"/> within <see cref="TFHud.CallingReach"/>, flagged). The only reader of another
/// player's health in the AI (TFPerceptionCensusTests holds it); the crosshair's target ID is not read yet. The last HUD is kept per bot and
/// handed again while nothing on it changed.
/// </summary>
[SideService(Side.Server)]
public sealed class TFHudReader(ITFOwnKit kits, IHooks<ITFHudMeters> meters, ITFMapPlayers people, IEnginePlayers players, IEngineEntities entities,
                                IEngineEnvironment environment, ITFActorFacts actors, TFClassTable classes)
{
    private readonly Dictionary<int, (int Version, ImmutableArray<TFKitItem> Kit)> _kits = new();
    private readonly Dictionary<int, TFHud> _last = new();
    private readonly List<TFMeter> _meters = new();
    private readonly List<TFMateHealth> _mates = new();

    /// <summary>HUDs made (a change on one), for the facts: a HUD handed again is not counted.</summary>
    public int Made { get; private set; }

    public TFHud Read(EntityHandle bot)
    {
        int index = bot.EntryIndex;
        ImmutableArray<TFKitItem> kit = KitOf(index);
        TFClass cls = people.ClassOf(bot);
        _meters.Clear();
        foreach (ITFHudMeters reader in meters.All)
            reader.Read(bot, cls, _meters);
        _mates.Clear();
        int patient = 0;
        foreach (TFMeter m in _meters)
        {
            if (m.Name == TFMedigunMeters.Patient)
                patient = (int)m.Value;
        }

        Mates(bot, cls, patient);
        if (_last.TryGetValue(index, out TFHud? last) && last.Shows(kit.AsSpan(), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_meters),
                                                                   System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_mates)))
            return last;
        var hud = new TFHud(kit, [.. _meters], [.. _mates]);
        _last[index] = hud;
        Made++;
        return hud;
    }

    /// <summary>The bot's kit as <see cref="TFKitItem"/>s, made again only when its version changed.</summary>
    private ImmutableArray<TFKitItem> KitOf(int index)
    {
        int version = kits.Version(index);
        if (_kits.TryGetValue(index, out var held) && held.Version == version)
            return held.Kit;
        ImmutableArray<TFKitItem> kit = [.. kits.KitOf(index).Select(i => new TFKitItem(i.Slot, i.Defindex, i.ItemClass, i.WeaponKind, [.. i.Attributes]))];
        _kits[index] = (version, kit);
        return kit;
    }

    /// <summary>
    /// The teammates the bot's HUD shows: its beam's patient, with the exact health; to a Medic, every caller
    /// (<see cref="TFMateSeen.Calling"/>: a flag and a place, no number), the patient not again. A Spy disguised as the
    /// bot's team is a teammate here, as on stock's HUD.
    /// </summary>
    private void Mates(EntityHandle bot, TFClass cls, int patient)
    {
        int team = people.Team(bot);
        Vector at = entities.GetOrigin(bot);
        int max = environment.Globals.IsAvailable ? environment.Globals.MaxClients : 0;
        for (int i = 1; i <= max; i++)
        {
            if (i == bot.EntryIndex)
                continue;
            EntityHandle mate = players.GetHandle(i);
            if (mate == EntityHandle.None || !players.IsAlive(mate) || !ShownAsMate(mate, i, team))
                continue;
            Vector there = entities.GetOrigin(mate);
            var place = new AiVector(there.X, there.Y, there.Z);
            (int health, int most) = HealthShown(mate, i, team);
            if (i == patient)
            {
                _mates.Add(new TFMateHealth(i, health, most, TFMateSeen.HealTarget, place));
                continue;
            }

            if (cls != TFClass.Medic)
                continue;
            float dx = there.X - at.X, dy = there.Y - at.Y, dz = there.Z - at.Z;
            if (most > 0 && health <= TFHud.CallingShare * most && dx * dx + dy * dy + dz * dz <= TFHud.CallingReach * TFHud.CallingReach)
                _mates.Add(new TFMateHealth(i, -1, -1, TFMateSeen.Calling, place));
        }
    }

    /// <summary>
    /// The health the HUD shows of player <paramref name="index"/> and its most: a teammate's own; for an enemy Spy shown
    /// as a teammate by his disguise, the disguise's (stock's <c>m_iDisguiseHealth</c> against the disguise class's
    /// <c>health_max</c>, review-p7f F5), never his real health.
    /// </summary>
    private (int Health, int Most) HealthShown(EntityHandle mate, int index, int team)
    {
        if (people.Team(mate) == team || !actors.TryRead(index, out TFActorFacts f))
            return (people.Health(mate), people.MaxHealth(mate));
        int most = classes.IsLoaded && f.DisguiseClass is > TFClass.Undefined and < TFClass.Civilian ? classes[f.DisguiseClass].MaxHealth : people.MaxHealth(mate);
        return (f.DisguiseHealth, most);
    }

    /// <summary>Whether the HUD shows player <paramref name="index"/> as on <paramref name="team"/>: by their team, or a Spy's disguise as it.</summary>
    private bool ShownAsMate(EntityHandle mate, int index, int team)
        => people.Team(mate) == team
           || (actors.TryRead(index, out TFActorFacts f) && f.Class == TFClass.Spy && f.Disguised && (int)f.DisguiseTeam == team);
}
