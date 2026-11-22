using System.Text.Json;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Skills;

namespace TF2.Descent.Rules.Economy.Model;

/// <summary>
/// A weapon family as the model fires it: TF's stock numbers (damage a shot, refire interval,
/// reload time a round), the hero damage kinds a hit counts as (<see cref="Hero.HeroStats.DamageMultiplier"/>),
/// the share of shots that land with no aiming modelled, and whether a hit splashes.
/// </summary>
public sealed record ModelWeapon(string Family, double Damage, double Interval, double Reload, IReadOnlyList<string> Kinds, double HitPct, bool Splash);

/// <summary>
/// The potion policy: drink health below <see cref="HealthAtLifePct"/> and mana below
/// <see cref="ManaAtManaPct"/>, one potion each <see cref="CooldownSeconds"/>; in town fill the
/// belt with so many of each and carry spares in the backpack.
/// </summary>
public sealed record ModelPotions(double CooldownSeconds, double HealthAtLifePct, double ManaAtManaPct, int BeltHealth, int BeltMana, int SpareHealth, int SpareMana);

/// <summary>A death costs <see cref="Seconds"/> (the walk back, D6's corpse run as time only); a pack that kills a hero more than <see cref="MaxPerPack"/> times is not cleared.</summary>
public sealed record ModelDeath(double Seconds, int MaxPerPack);

/// <summary>
/// A depth as the model walks it: <see cref="PackRoomsPct"/> of its layout's rooms hold a pack
/// at <see cref="MarkersPerRoom"/> markers; each room takes <see cref="SecondsPerRoom"/> to walk,
/// a trip to town <see cref="TownSeconds"/>; a boss level has <see cref="BossRoomPacks"/> packs
/// before its boss.
/// </summary>
public sealed record ModelRun(double PackRoomsPct, int MarkersPerRoom, double SecondsPerRoom, double TownSeconds, int BossRoomPacks);

/// <summary>
/// Median gear (A.6's gate): what a median hero wears is, slot by slot, the best of
/// <see cref="DropsPerSlot"/> drops of the depth, and the median of <see cref="MedianSets"/>
/// such heroes.
/// </summary>
public sealed record ModelGear(int MedianSets, int DropsPerSlot);

/// <summary>
/// RU14's solo pacing band: a solo hero of A.6's first three builds reaches the bottom in at most
/// <see cref="SoloHoursMaxRatio"/> times the hours of a party of the four builds on the same
/// seed, dying at most <see cref="SoloDeathsPerDepthMax"/> times a depth.
/// </summary>
public sealed record ModelPacing(double SoloHoursMaxRatio, double SoloDeathsPerDepthMax);

/// <summary>A boss fight's length band at a depth: a level-appropriate fight lasts between <see cref="MinSeconds"/> and <see cref="MaxSeconds"/>.</summary>
public sealed record ModelBossBand(int Depth, double MinSeconds, double MaxSeconds);

/// <summary>
/// The model's boss fights (bosses.json, HO13) and RU14's boss gates. A volley's shot lands on a
/// hero <see cref="VolleyHitPct"/> of the time and a slam <see cref="SlamHitPct"/> (no positions:
/// expected values, as a robot's attack). The Tank's track is <see cref="TankTrackUnits"/> long
/// over <see cref="TankStations"/> stations (the mine plan's Bore: about 10,700 units, four outer
/// pillars and the roof pillar); the roof falls once it has rolled the track at its speed and
/// drilled every station. The gates: no single ordinary boss attack takes more than
/// <see cref="OrdinaryHitMaxLifePct"/> of a hero's life, and no single shot or slam of a special
/// <see cref="SpecialHitMaxLifePct"/> or more; a fight of each of <see cref="SoloBuilds"/> alone lasts
/// inside its depth's <see cref="Bands"/>; a party of four clears it (not impossible), taking at
/// least <see cref="PartyMinTakenPct"/> of its life in damage and lasting at least
/// <see cref="PartyMinSeconds"/> (not trivial).
/// </summary>
public sealed record ModelBoss(
    double VolleyHitPct,
    double SlamHitPct,
    double TankTrackUnits,
    int TankStations,
    double OrdinaryHitMaxLifePct,
    double SpecialHitMaxLifePct,
    IReadOnlyList<string> SoloBuilds,
    double PartyMinTakenPct,
    double PartyMinSeconds,
    IReadOnlyList<ModelBossBand> Bands)
{
    public ModelBossBand? Band(int depth) => Bands.FirstOrDefault(b => b.Depth == depth);
}

/// <summary>A skill the build buys up to a rank, in the build's order.</summary>
public sealed record BuildPoint(string Skill, int Rank);

/// <summary>
/// An Appendix A.6 build: where its points go (in order, each to its rank, as soon as the
/// tree allows), how its attribute points split, the actions it casts by priority, and how
/// often it blast jumps (0: never).
/// </summary>
public sealed record ModelBuild(string Id, IReadOnlyDictionary<string, double> Attributes, double JumpEverySeconds,
    IReadOnlyList<BuildPoint> Points, IReadOnlyList<string> Rotation) : IContentRow;

/// <summary>
/// economy_model.json: the assumptions of RU14's economy model, every one a number here so the
/// model states them. The model is an instrument, not the game: it has no positions and no
/// aiming, so hits land by these percents and fights exchange expected damage a tick.
/// </summary>
public sealed record ModelSettings(
    double TickSeconds,
    double MaxFightSeconds,
    IReadOnlyList<ModelWeapon> Weapons,
    double RobotHitPct,
    int MaxAttackers,
    double ApproachSeconds,
    double SplashShare,
    int SplashTargets,
    double CritMultiplier,
    double CastSeconds,
    double SelfBlastDamage,
    double AirSeconds,
    double JumpAboveLifePct,
    ModelPotions Potions,
    ModelDeath Death,
    ModelRun Run,
    ModelGear Gear,
    double ThreatPerXpBandPct,
    IReadOnlyList<ModelBuild> Builds,
    ModelBoss Boss,
    ModelPacing Pacing)
{
    public ModelWeapon? Weapon(string family) => Weapons.FirstOrDefault(w => w.Family == family);

    public ModelBuild Build(string id) => Builds.FirstOrDefault(b => b.Id == id) ?? throw new KeyNotFoundException($"no build '{id}'");
}

public sealed class ModelTable : SingleTable<ModelSettings>
{
    public const string TableName = "economy_model";

    public override string Name => TableName;

    protected override ModelSettings Read(JsonFields f)
    {
        var weapons = new List<ModelWeapon>();
        foreach (var e in f.Array("weapons"))
        {
            var w = new JsonFields(e, f.Table, "weapons", f.Errors);
            weapons.Add(new ModelWeapon(w.Str("family"), w.Num("damage"), w.Num("interval"), w.Num("reload"), w.Strings("kinds", required: true),
                w.Num("hitPct"), w.Bool("splash")));
            w.End();
        }
        var p = f.Obj("potion");
        var potions = new ModelPotions(p.Num("cooldownSeconds"), p.Num("healthAtLifePct"), p.Num("manaAtManaPct"), p.Int("beltHealth"), p.Int("beltMana"),
            p.Int("spareHealth"), p.Int("spareMana"));
        p.End();
        var d = f.Obj("death");
        var death = new ModelDeath(d.Num("seconds"), d.Int("maxPerPack"));
        d.End();
        var r = f.Obj("run");
        var run = new ModelRun(r.Num("packRoomsPct"), r.Int("markersPerRoom"), r.Num("secondsPerRoom"), r.Num("townSeconds"), r.Int("bossRoomPacks"));
        r.End();
        var g = f.Obj("gear");
        var gear = new ModelGear(g.Int("medianSets"), g.Int("dropsPerSlot"));
        g.End();
        var bo = f.Obj("boss");
        var bands = new List<ModelBossBand>();
        foreach (var e in bo.Array("bands"))
        {
            var band = new JsonFields(e, f.Table, "boss.bands", f.Errors);
            bands.Add(new ModelBossBand(band.Int("depth"), band.Num("minSeconds"), band.Num("maxSeconds")));
            band.End();
        }
        var boss = new ModelBoss(bo.Num("volleyHitPct"), bo.Num("slamHitPct"), bo.Num("tankTrackUnits"), bo.Int("tankStations"),
            bo.Num("ordinaryHitMaxLifePct"), bo.Num("specialHitMaxLifePct"), bo.Strings("soloBuilds", required: true), bo.Num("partyMinTakenPct"),
            bo.Num("partyMinSeconds"), bands);
        bo.End();
        var pa = f.Obj("pacing");
        var pacing = new ModelPacing(pa.Num("soloHoursMaxRatio"), pa.Num("soloDeathsPerDepthMax"));
        pa.End();
        var builds = new List<ModelBuild>();
        foreach (var e in f.Array("builds"))
        {
            var b = new JsonFields(e, f.Table, "builds", f.Errors);
            var attributes = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var (key, value) in b.Map("attributes"))
                attributes[key] = value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
            var points = new List<BuildPoint>();
            foreach (var pe in b.Array("points"))
            {
                var bp = new JsonFields(pe, f.Table, "builds", f.Errors);
                points.Add(new BuildPoint(bp.Str("skill"), bp.Int("rank")));
                bp.End();
            }
            builds.Add(new ModelBuild(b.Str("id"), attributes, b.Num("jumpEverySeconds"), points, b.Strings("rotation", required: true)));
            b.End();
        }
        return new ModelSettings(f.Num("tickSeconds"), f.Num("maxFightSeconds"), weapons, f.Num("robotHitPct"), f.Int("maxAttackers"),
            f.Num("approachSeconds"), f.Num("splashShare"), f.Int("splashTargets"), f.Num("critMultiplier"), f.Num("castSeconds"),
            f.Num("selfBlastDamage"), f.Num("airSeconds"), f.Num("jumpAboveLifePct"), potions, death, run, gear, f.Num("threatPerXpBandPct"), builds, boss, pacing);
    }

    protected override void Validate(DescentContent content, ModelSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "tickSeconds", s.TickSeconds, 0.01, 5);
        ContentChecks.Range(errors, t, null, "maxFightSeconds", s.MaxFightSeconds, 1, 1e6);
        ContentChecks.Range(errors, t, null, "robotHitPct", s.RobotHitPct, 0, 100);
        ContentChecks.Range(errors, t, null, "maxAttackers", s.MaxAttackers, 1, 64);
        ContentChecks.Range(errors, t, null, "splashShare", s.SplashShare, 0, 1);
        ContentChecks.Range(errors, t, null, "splashTargets", s.SplashTargets, 0, 64);
        ContentChecks.Range(errors, t, null, "critMultiplier", s.CritMultiplier, 1, 10);
        ContentChecks.Range(errors, t, null, "threatPerXpBandPct", s.ThreatPerXpBandPct, 0, 1000);
        ContentChecks.Range(errors, t, "pacing", "soloHoursMaxRatio", s.Pacing.SoloHoursMaxRatio, 1, 100);
        ContentChecks.Range(errors, t, "pacing", "soloDeathsPerDepthMax", s.Pacing.SoloDeathsPerDepthMax, 0, 1000);
        ContentChecks.Range(errors, t, "boss", "volleyHitPct", s.Boss.VolleyHitPct, 0, 100);
        ContentChecks.Range(errors, t, "boss", "slamHitPct", s.Boss.SlamHitPct, 0, 100);
        ContentChecks.Range(errors, t, "boss", "tankTrackUnits", s.Boss.TankTrackUnits, 1, 1e6);
        ContentChecks.Range(errors, t, "boss", "tankStations", s.Boss.TankStations, 1, 64);
        ContentChecks.Range(errors, t, "boss", "ordinaryHitMaxLifePct", s.Boss.OrdinaryHitMaxLifePct, 1, 100);
        ContentChecks.Range(errors, t, "boss", "specialHitMaxLifePct", s.Boss.SpecialHitMaxLifePct, 1, 100);
        foreach (var band in s.Boss.Bands)
        {
            ContentChecks.Range(errors, t, "boss", "bands.minSeconds", band.MinSeconds, 1, s.MaxFightSeconds);
            ContentChecks.Range(errors, t, "boss", "bands.maxSeconds", band.MaxSeconds, band.MinSeconds, s.MaxFightSeconds);
        }
        foreach (var id in s.Boss.SoloBuilds.Where(id => s.Builds.All(b => b.Id != id)))
            errors.Add(t, "boss", ContentRule.DanglingReference, $"boss.soloBuilds names '{id}', which is no build");
        foreach (var w in s.Weapons)
        {
            ContentChecks.Reference<FamilyRow>(content, errors, t, w.Family, "weapons.family", w.Family, ContentTables.Families);
            ContentChecks.Range(errors, t, w.Family, "interval", w.Interval, 0.01, 60);
            ContentChecks.Range(errors, t, w.Family, "hitPct", w.HitPct, 0, 100);
        }
        foreach (var b in s.Builds)
        {
            foreach (var p in b.Points)
                ContentChecks.Reference<SkillDef>(content, errors, t, b.Id, "points", p.Skill, ContentTables.Skills);
            foreach (var r in b.Rotation)
                ContentChecks.Reference<SkillDef>(content, errors, t, b.Id, "rotation", r, ContentTables.Skills);
            foreach (var a in b.Attributes.Keys)
                ContentChecks.Reference<AttributeRow>(content, errors, t, b.Id, "attributes", a, ContentTables.Attributes);
        }
    }
}
