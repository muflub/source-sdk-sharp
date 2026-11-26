using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Death;

/// <summary>
/// Which of a hero's items fall to the corpse on death (D6: all of them). The Australium is not
/// one: it stays with the hero (the user, 2026-11-22, plan L26).
/// </summary>
public sealed record DeathDrops(bool Equipped, bool Backpack, bool Belt);

/// <summary>
/// The downed window and the revive (R26): the first down on a level lasts
/// <see cref="WindowSeconds"/>, each further one <see cref="ShortenPerDownSeconds"/> less, never
/// under <see cref="WindowFloorSeconds"/>; a revive is <see cref="ReviveHoldSeconds"/> of held
/// use within <see cref="ReviveRange"/> units, and leaves the hero at
/// <see cref="ReviveLifePct"/> of maximum life with <see cref="GraceSeconds"/> of grace. A hero
/// who respawns takes no damage for <see cref="RespawnGraceSeconds"/>, or until he acts, and no
/// robot recognises him in it. <see cref="RespawnBlindSeconds"/> (this lane's rule, plan_descent_logic
/// 4.7; 0 turns it off): when the grace lapses by time, a robot that had him in view during it stays
/// blind to him until it has lost him from view once, at most this long after the respawn; his
/// first act ends it at once.
/// </summary>
public sealed record DownedSettings(
    double WindowSeconds,
    double ShortenPerDownSeconds,
    double WindowFloorSeconds,
    double ReviveHoldSeconds,
    double ReviveRange,
    double ReviveLifePct,
    double GraceSeconds,
    double RespawnGraceSeconds,
    double RespawnBlindSeconds);

/// <summary>
/// death.json (plan_tf2_rpg 2g, D6, D7, R5, R26; plan_descent_logic RU10): what drops,
/// how long a corpse outlives an emptied instance before the Lost &amp; Found takes it, the
/// Lost &amp; Found's fee, and the downed state's numbers.
/// </summary>
public sealed record DeathSettings(DeathDrops Drops, double CorpseLingerSeconds, double LostAndFoundFeePct, DownedSettings Downed);

public sealed class DeathTable : SingleTable<DeathSettings>
{
    public override string Name => ContentTables.Death;

    protected override DeathSettings Read(JsonFields f)
    {
        var drops = f.Obj("drops");
        var corpse = f.Obj("corpse");
        var downed = f.Obj("downed");
        var settings = new DeathSettings(
            new DeathDrops(drops.Bool("equipped"), drops.Bool("backpack"), drops.Bool("belt")),
            corpse.Num("lingerSeconds"),
            corpse.Num("lostAndFoundFeePct"),
            new DownedSettings(
                downed.Num("windowSeconds"),
                downed.Num("shortenPerDownSeconds"),
                downed.Num("windowFloorSeconds"),
                downed.Num("reviveHoldSeconds"),
                downed.Num("reviveRange"),
                downed.Num("reviveLifePct"),
                downed.Num("graceSeconds"),
                downed.Num("respawnGraceSeconds"),
                downed.Num("respawnBlindSeconds")));
        drops.End();
        corpse.End();
        downed.End();
        return settings;
    }

    protected override void Validate(DescentContent content, DeathSettings d, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "corpse.lingerSeconds", d.CorpseLingerSeconds, 0, 86400);
        ContentChecks.Range(errors, t, null, "corpse.lostAndFoundFeePct", d.LostAndFoundFeePct, 0, 100);
        var w = d.Downed;
        if (ContentChecks.Range(errors, t, null, "downed.windowFloorSeconds", w.WindowFloorSeconds, 0.1, 600))
            ContentChecks.Range(errors, t, null, "downed.windowSeconds", w.WindowSeconds, w.WindowFloorSeconds, 600);
        ContentChecks.Range(errors, t, null, "downed.shortenPerDownSeconds", w.ShortenPerDownSeconds, 0, 600);
        ContentChecks.Range(errors, t, null, "downed.reviveHoldSeconds", w.ReviveHoldSeconds, 0.1, 60);
        ContentChecks.Range(errors, t, null, "downed.reviveRange", w.ReviveRange, 1, 4096);
        ContentChecks.Range(errors, t, null, "downed.reviveLifePct", w.ReviveLifePct, 1, 100);
        ContentChecks.Range(errors, t, null, "downed.graceSeconds", w.GraceSeconds, 0, 60);
        ContentChecks.Range(errors, t, null, "downed.respawnGraceSeconds", w.RespawnGraceSeconds, 0, 60);
        ContentChecks.Range(errors, t, null, "downed.respawnBlindSeconds", w.RespawnBlindSeconds, 0, 120);
    }
}
