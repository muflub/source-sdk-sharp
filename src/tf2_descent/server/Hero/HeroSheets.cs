using System.Collections.Immutable;
using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Host.Contracts;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Sheets;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// A checkpoint taken from <see cref="HeroSheets"/>: what it hands the host,
/// and the evidence it drained, which <see cref="HeroSheets.Restore"/> puts
/// back when the host did not take it.
/// </summary>
public sealed record HeroCheckpoint(string SteamId, SheetCheckpoint Payload, CheckpointProof Drained);

/// <summary>
/// The live hero sheet of every hero in the game (plan_descent_logic HE2), and
/// the evidence each has earned since the host last took a checkpoint.
///
/// <list type="bullet">
/// <item>HOSTED, the sheet is the leased character's: <see cref="PlayerService"/>
/// hands it over when the join answers (<see cref="Leased"/>), keyed as it keys
/// its entries, by the host's SteamID; every checkpoint it sends carries the
/// sheet as it is now and drains the evidence (<see cref="TakeCheckpoint"/>).</item>
/// <item>UNHOSTED (E8), a hero's sheet is a new character of the default
/// class, made the first time it is asked for and held in memory for the
/// process: it outlives a respawn, a level change and a reconnect. It is keyed
/// by the engine's SteamID, or by the slot when the engine reports none.</item>
/// </list>
///
/// Every change goes through <see cref="Apply"/>, which runs the rules' audit
/// and refuses (and logs) a sheet that play could not have reached.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroSheets(TF2DescentRules rules, HostService host, IConnectedClients clients, ILog log)
{
    /// <summary>The class an unhosted hero starts as (PlayerService.NewCharacterClass's, hosted).</summary>
    public const string NewHeroClass = "soldier";

    private sealed class Held(HeroSheet sheet)
    {
        public HeroSheet Sheet { get; set; } = sheet;
        public long Xp { get; set; }
        public Dictionary<string, int> Kills { get; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, Held> _byKey = new(StringComparer.Ordinal);

    // Hosted only: who holds each slot, as the join answered.
    private readonly Dictionary<int, string> _hosted = new();

    private bool Hosted => host.Mode == HostingMode.Hosted;

    /// <summary>The rules every sheet here is read and checked by.</summary>
    public TF2DescentRules Rules => rules;

    /// <summary>The key the slot's hero is held under: the host's SteamID hosted, the engine's (or the slot) unhosted; null for a hosted slot with no join yet.</summary>
    public string? KeyOf(int entIndex)
    {
        if (Hosted)
            return _hosted.GetValueOrDefault(entIndex);

        ulong steam = clients.SteamId64(entIndex);
        return steam != 0
            ? steam.ToString(CultureInfo.InvariantCulture)
            : "slot " + entIndex.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The hero sheet of the player in <paramref name="entIndex"/>, or null
    /// while a hosted join has not answered. Unhosted, the first ask makes a
    /// new character of <see cref="NewHeroClass"/>.
    /// </summary>
    public HeroSheet? Of(int entIndex) => HeldOf(entIndex)?.Sheet;

    private Held? HeldOf(int entIndex)
    {
        if (KeyOf(entIndex) is not { } key)
            return null;
        if (_byKey.TryGetValue(key, out var held))
            return held;
        if (Hosted)
            return null;

        var sheet = rules.SheetCodec.Read(rules.NewCharacter(NewHeroClass, "hero-" + key, hardcore: false));
        held = new Held(sheet);
        _byKey[key] = held;
        log.Msg($"[TF2Descent/Hero] {key} is a new {sheet.ClassId} (unhosted)\n");
        return held;
    }

    /// <summary>
    /// Changes the slot's sheet. The change is checked by the rules' audit
    /// (<see cref="HeroRules.Audit"/>): a sheet that breaks it is refused,
    /// logged, and the sheet stays as it was. A slot with no sheet refuses.
    /// </summary>
    public AuditResult Apply(int entIndex, Func<HeroSheet, HeroSheet> change)
    {
        if (HeldOf(entIndex) is not { } held)
            return AuditResult.Fail("no_sheet", $"player {entIndex} has no hero sheet");

        var after = change(held.Sheet);
        var audit = rules.Heroes.Audit(after);
        if (!audit.Ok)
        {
            log.Warning($"[TF2Descent/Hero] a change to {KeyOf(entIndex)}'s sheet was refused: {audit.Rule} ({audit.Detail})\n");
            return audit;
        }

        held.Sheet = after;
        return audit;
    }

    /// <summary>
    /// Earned in play since the last checkpoint (what grants XP and counts
    /// kills calls this): <paramref name="xp"/> experience and
    /// <paramref name="kills"/> robots by template. The next checkpoint sends
    /// it as the rules' evidence and drains it, so the host can explain the
    /// sheet's growth (RU13: a checkpoint with no evidence earns nothing).
    /// </summary>
    public void RecordEarned(int entIndex, long xp, IReadOnlyDictionary<string, int>? kills = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(xp);
        if (HeldOf(entIndex) is not { } held)
            return;

        held.Xp += xp;
        foreach (var (template, count) in kills ?? ImmutableDictionary<string, int>.Empty)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            held.Kills[template] = held.Kills.GetValueOrDefault(template) + count;
        }
    }

    /// <summary>The evidence the slot's next checkpoint would carry.</summary>
    public CheckpointProof Pending(int entIndex) =>
        HeldOf(entIndex) is { } held ? Proof(held) : CheckpointProof.None;

    private static CheckpointProof Proof(Held held) =>
        new(held.Xp, held.Kills.ToImmutableSortedDictionary(StringComparer.Ordinal));

    //-------------------------------------------------------------------------
    // Hosted: what PlayerService hands over and asks for
    //-------------------------------------------------------------------------

    /// <summary>
    /// A hosted join answered: the slot is <paramref name="steamId"/>'s, whose
    /// leased sheet is <paramref name="sheet"/>. A hero already held under that
    /// SteamID (a re-lease after a lost connection) keeps the sheet it has, which
    /// is ahead of the host's by what its evidence explains. A join that carried
    /// no sheet starts a new character of <paramref name="className"/>. Answers
    /// why the sheet cannot be played (it does not decode), or null.
    /// </summary>
    public string? Leased(int entIndex, string steamId, CharacterSheet? sheet, string? className, string? name)
    {
        if (!_byKey.ContainsKey(steamId))
        {
            HeroSheet hero;
            try
            {
                hero = sheet is { Data.Length: > 0 }
                    ? rules.SheetCodec.Read(sheet)
                    : rules.Heroes.New(className ?? NewHeroClass, name ?? "hero-" + steamId);
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException)
            {
                log.Warning($"[TF2Descent/Hero] {steamId}'s sheet cannot be played: {e.Message}\n");
                return "Your character's sheet could not be read: " + e.Message;
            }

            if (sheet is not { Data.Length: > 0 })
                log.Warning($"[TF2Descent/Hero] the join of {steamId} carried no sheet: a new {hero.ClassId}\n");
            _byKey[steamId] = new Held(hero);
        }

        _hosted[entIndex] = steamId;
        return null;
    }

    /// <summary>
    /// The checkpoint of <paramref name="steamId"/>'s hero: the sheet now and
    /// the evidence since the last one, which this drains. Null when no hero
    /// is held under that SteamID (the host's own sheet then goes back unchanged).
    /// </summary>
    public HeroCheckpoint? TakeCheckpoint(string steamId)
    {
        if (!_byKey.TryGetValue(steamId, out var held))
            return null;

        var proof = Proof(held);
        held.Xp = 0;
        held.Kills.Clear();
        var payload = new SheetCheckpoint(rules.SheetCodec.Write(held.Sheet), rules.Evidence.Write(proof));
        return new HeroCheckpoint(steamId, payload, proof);
    }

    /// <summary>The host did not take <paramref name="failed"/>: its evidence is owed again, beside anything earned since.</summary>
    public void Restore(HeroCheckpoint failed)
    {
        if (!_byKey.TryGetValue(failed.SteamId, out var held))
            return;

        held.Xp += failed.Drained.XpEarned;
        foreach (var (template, count) in failed.Drained.Kills)
            held.Kills[template] = held.Kills.GetValueOrDefault(template) + count;
    }

    /// <summary>A hosted hero left (its lease released): nothing of it is held here any more.</summary>
    public void Left(string steamId)
    {
        _byKey.Remove(steamId);
        foreach (var slot in _hosted.Where(e => e.Value == steamId).Select(e => e.Key).ToList())
            _hosted.Remove(slot);
    }

    /// <summary>Every hosted hero is forgotten: the level's Booting answered and the hop already released them.</summary>
    public void ForgetHosted()
    {
        foreach (var steamId in _hosted.Values.Distinct().ToList())
            _byKey.Remove(steamId);
        _hosted.Clear();
    }
}
