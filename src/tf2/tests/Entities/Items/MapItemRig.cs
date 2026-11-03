using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;
using TF2.Server.Entities;
using TF2.Server.Entities.Ctf;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Tests.Ctf;

using ModeSlot = TF2.Tests.Rules.ModeSlot;

namespace TF2.Tests.Entities.Items;

/// <summary>One fake TF player: what the map's entities read and write.</summary>
internal sealed class FakeMapPlayer
{
    public int Team { get; set; } = (int)TFTeam.Red;
    public bool Alive { get; set; } = true;
    public TFClass Class { get; set; } = TFClass.Soldier;
    public int Health { get; set; }
    public int MaxHealth { get; set; } = 200;
    public HashSet<TFCond> Conditions { get; } = new();
    public Dictionary<TFCond, float> Durations { get; } = new();
    public Dictionary<TFAmmo, int> Ammo { get; } = new();
    public Dictionary<TFAmmo, int> MaxAmmo { get; } = new();
    public int Regenerated { get; set; }
    public List<float> Burns { get; } = new();
    public float CloakMeter { get; set; }
}

/// <summary>The players by handle, as <see cref="ITFMapPlayers"/> answers them; health and ammo capped at the maximum as stock's TakeHealth and GiveAmmo cap.</summary>
internal sealed class FakeMapPlayers : ITFMapPlayers
{
    private uint _next = 1;

    public Dictionary<EntityHandle, FakeMapPlayer> All { get; } = new();

    public (EntityHandle Handle, FakeMapPlayer Player) Add(TFTeam team = TFTeam.Red, TFClass playerClass = TFClass.Soldier)
    {
        var handle = new EntityHandle(_next++ | (1u << 16));
        var player = new FakeMapPlayer { Team = (int)team, Class = playerClass };
        All[handle] = player;
        return (handle, player);
    }

    private FakeMapPlayer P(EntityHandle h) => All[h];

    public bool IsAlive(EntityHandle player) => All.TryGetValue(player, out var p) && p.Alive;

    /// <summary>An unknown handle has no team to name — the suicide-team read asks about strangers.</summary>
    public int Team(EntityHandle player) => All.TryGetValue(player, out var p) ? p.Team : 0;
    public TFClass ClassOf(EntityHandle player) => P(player).Class;
    public int Health(EntityHandle player) => P(player).Health;
    public int MaxHealth(EntityHandle player) => P(player).MaxHealth;

    public int TakeHealth(EntityHandle player, float health)
    {
        var p = P(player);
        int given = (int)Math.Max(0f, Math.Min(health, p.MaxHealth - p.Health));
        p.Health += given;
        return given;
    }

    public bool InCond(EntityHandle player, TFCond cond) => P(player).Conditions.Contains(cond);

    public void AddCond(EntityHandle player, TFCond cond, float duration)
    {
        P(player).Conditions.Add(cond);
        P(player).Durations[cond] = duration;
    }

    public void RemoveCond(EntityHandle player, TFCond cond) => P(player).Conditions.Remove(cond);
    public void RemoveAllCond(EntityHandle player) => P(player).Conditions.Clear();
    public bool IsInvulnerable(EntityHandle player) => P(player).Conditions.Contains(TFCond.Invulnerable);
    public int MaxAmmo(EntityHandle player, TFAmmo ammo) => P(player).MaxAmmo.GetValueOrDefault(ammo);

    public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo)
    {
        var p = P(player);
        int have = p.Ammo.GetValueOrDefault(ammo);
        int given = Math.Max(0, Math.Min(count, p.MaxAmmo.GetValueOrDefault(ammo) - have));
        p.Ammo[ammo] = have + given;
        return given;
    }

    public void Regenerate(EntityHandle player) => P(player).Regenerated++;
    public void SelfBurn(EntityHandle player, float duration)
    {
        P(player).Burns.Add(duration);
        P(player).Conditions.Add(TFCond.Burning);
    }

    /// <summary>
    /// <c>AddToSpyCloakMeter</c> as the seam answers it: the spy's own watch
    /// gate, the meter clamped 0..100, and the answer is whether a positive
    /// amount moved it. The watch's attribute hooks have their facts at the
    /// seam itself (TFPlayerMapPlayersTests).
    /// </summary>
    public bool AddToSpyCloakMeter(EntityHandle player, float amount)
    {
        var p = P(player);
        if (p.Class != TFClass.Spy)
            return false;
        bool moved = amount > 0f && p.CloakMeter < 100f;
        p.CloakMeter = Math.Clamp(p.CloakMeter + amount, 0f, 100f);
        return moved;
    }
}

/// <summary>What the pickups announced, in order.</summary>
internal sealed class FakeNotices : ITFPickupNotices
{
    public List<string> Said { get; } = new();

    public void ItemPickup(EntityHandle player, string className) => Said.Add($"ItemPickup {className}");
    public void ItemPickupEvent(EntityHandle player, string item) => Said.Add($"item_pickup {item}");
    public void PlayerHealed(EntityHandle patient, EntityHandle healer, int amount) => Said.Add($"player_healed {amount} by {healer}");
    public void PlayerHealOnHit(EntityHandle player, int amount) => Said.Add($"player_healonhit {amount}");
    public void PlayerStealsSandvich(EntityHandle owner, EntityHandle picker) => Said.Add($"player_stealsandvich {owner}->{picker}");
    public void Sound(EntityHandle player, string sound) => Said.Add($"sound {sound}");
}

/// <summary>The temp entities the map's entities sent, in order, with the engine-call count at each send so a fact can order a dispatch against an engine call.</summary>
internal sealed class RecordingTempEnts(RecordingEngine engine) : TempEntsDouble
{
    public readonly List<(string Name, TeEffectData Data, TeFilter Filter, int EngineCalls)> Dispatched = [];

    public override void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data)
        => Dispatched.Add((effectName, data, filter, engine.Calls.Count));
}

/// <summary>
/// The map's TF items, rooms and triggers on a registry over a recording
/// engine, with the players, the notices and the clock as fakes.
/// </summary>
internal sealed class MapItemRig
{
    public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
    public readonly RecordingEngine Engine;
    public readonly FakeMapPlayers Players = new();
    public readonly FakeNotices Notices = new();
    public readonly FakeClock Clock = new();
    public readonly FakeRoundState Round = new();
    public readonly ServiceContainer Services;

    private RecordingTempEnts? _temps;

    /// <summary>The temp entities, resolved after the engine it orders against.</summary>
    public RecordingTempEnts Temps => _temps ??= new(Engine);

    private uint _next = 0x0040_0000u;

    public MapItemRig()
    {
        Registry.RegisterAll(typeof(TFPowerup).Assembly);

        Services = TestServices.Ignoring("Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType",
                                         "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.IsValid",
                                         "EntityRender.LookupSequence", "Precache.PrecacheModel");
        Engine = (RecordingEngine)Services.Resolve<IEngineEntities>()!;
        Engine.Returns("Entities.GetSpawnFlags", 0)
              // A trigger's Enable/Disable reads its solid flags (CBaseTrigger's FSOLID_TRIGGER).
              .Returns("Entities.GetSolidFlags", SolidFlags.Trigger)
              .Returns("Entities.GetSolidType", SolidType.Bsp)
              .Returns("Entities.GetHealth", 1)
              .Returns("Files.GameFileSize", -1)
              // A respawning pickup asks for physics first (PickupItem.BecomePhysical)
              // and falls back to its box: the rig has no physics to give.
              .Returns("EntityPhysics.InitPhysics", false)
              // CItem::ItemTouch: a player, in sight of the item (UTIL_ItemCanBeTouchedByPlayer).
              .Returns("Entities.IsPlayer", true)
              .Returns("Entities.GetWorldSpaceCenter", Vector.Zero)
              .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f))
              .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(Vector.Zero, false, false, Fraction: 1f));

        Services.AddInstance<IEntityDirectory>(Registry);
        Services.AddInstance<ITFMapPlayers>(Players);
        Services.AddInstance<ITFPickupNotices>(Notices);
        Services.AddInstance<IObjectiveEntities>(new ObjectiveEntities(Registry));
        Services.AddInstance<IGameClock>(Clock);
        Services.AddInstance<ITeamRoundState>(Round);
        Services.AddInstance(new TFResupplyCooldowns());
        Services.AddInstance<ITempEnts>(Temps);

        // The respawn room lets a carried flag go through the CTF seams (2.5);
        // the CTF fakes stand under them where a map-item fact only needs the room.
        var ctfPlayers = new FakeCtfPlayers();
        Services.AddInstance<ICtfPlayers>(ctfPlayers);
        Services.AddInstance<ICtfRules>(new CtfRules(Round, new FakeRoundControl(),
            new TFTeamScores(new TeamResource()), new FakeCtfSettings(), ctfPlayers, Registry,
            new ModeSlot()));

        Registry.Services = Services;
    }

    public EntityHandle NewHandle() => new(_next++);

    public T Spawn<T>(string className, params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var handle = NewHandle();
        foreach (var (key, value) in keys)
            Registry.KeyValue(handle, className, key, value);
        Registry.Spawn(handle, className);
        return (T)Registry.Get(handle)!;
    }

    public void Touch(ScriptedEntity entity, EntityHandle player) => Registry.Touch(entity.Handle, player);

    public void Input(ScriptedEntity entity, string input, string value = "")
        => Registry.Input(entity.Handle, input, value, 0f, EntityHandle.None);
}
