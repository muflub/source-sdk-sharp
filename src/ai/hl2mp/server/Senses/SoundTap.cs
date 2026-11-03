using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// What a heard sound was (plan_hl2mp_ai §4.2), from the name it was emitted
/// under: a weapon's own sounds by the weapon table's rows (its fire, its
/// secondary, its reload, its dry click; no second copy of the names), the
/// rest by the script families stock names them in.
/// </summary>
[SideService(Side.Server)]
public sealed class SoundClassifier(WeaponCatalog weapons)
{
    private Dictionary<string, (SoundKind Kind, int Weapon)>? _byName;

    /// <summary>The kind of <paramref name="soundName"/> and the weapon kind (catalog index) it belongs to, or -1.</summary>
    public (SoundKind Kind, int Weapon) Classify(string soundName)
    {
        _byName ??= Table();
        if (_byName.TryGetValue(soundName, out (SoundKind, int) w))
            return w;

        string n = soundName;
        if (Has(n, ".Step") || Has(n, "footstep"))
            return (SoundKind.Footstep, -1);
        if (Has(n, "Explo") || Has(n, "Detonate"))
            return (SoundKind.Explosion, -1);
        if (Has(n, "Pain") || Has(n, "Hurt"))
            return (SoundKind.Pain, -1);
        if (Has(n, "Death") || Has(n, "Die"))
            return (SoundKind.Death, -1);
        if (Has(n, "Pickup") || Has(n, ".Touch") || Has(n, "AmmoPickup"))
            return (SoundKind.Pickup, -1);
        if (Has(n, "Door"))
            return (SoundKind.Door, -1);
        if (Has(n, "Impact") || Has(n, "Physics") || Has(n, "Break") || Has(n, "Scrape"))
            return (SoundKind.Physics, -1);
        return (SoundKind.Unknown, -1);
    }

    private static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);

    private Dictionary<string, (SoundKind, int)> Table()
    {
        var t = new Dictionary<string, (SoundKind, int)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponType w = weapons.ByIndex(i)!.Value;
            Add(t, w.FireSound, SoundKind.Gunfire, i);
            Add(t, w.Secondary?.Sound, SoundKind.Gunfire, i);
            Add(t, w.ReloadSound, SoundKind.Reload, i);
            Add(t, w.EmptySound, SoundKind.DryFire, i);
        }

        return t;
    }

    private static void Add(Dictionary<string, (SoundKind, int)> t, string? name, SoundKind kind, int weapon)
    {
        if (!string.IsNullOrEmpty(name))
            t.TryAdd(name, (kind, weapon));
    }
}

/// <summary>
/// The hearing tap (plan_hl2mp_ai §4.2): a bot hears a sound exactly when its
/// player index is in the sound's final recipient set, what a client would be
/// sent; never its own sound (a predicting client is not sent it either).
/// The heard place carries an error that grows with distance and shrinks with
/// Alertness; the loudness falls with distance across the sound's reach
/// (<c>2 · SOUND_NORMAL_CLIP_DIST / attenuation</c>), and Alertness can only
/// shrink that reach.
/// </summary>
/// <para>
/// A footstep reaches a bot by two ways, as it reaches a client: the server
/// sends it to the PAS less the PVS (<see cref="ISoundEmitHook"/>), and each
/// client in the PVS makes it itself from the player's networked movement
/// (<see cref="IClientStepHook"/>, the step's recipients being the PVS's
/// players). Both are heard by the same rule.
/// </para>
[SideService(Side.Server, As = [typeof(ISoundEmitHook), typeof(IClientStepHook)])]
public sealed class SoundTap(Hl2mpSenses senses, Hl2mpBots bots, SoundClassifier classifier, IBotHud hud, IEngineFrame frame, HeardLog heard, ILog log)
    : ISoundEmitHook, IClientStepHook
{
    private readonly Dictionary<int, AiRandom> _random = new();

    /// <summary>Sounds a bot heard, in all.</summary>
    public long Heard { get; private set; }

    /// <summary>The share of the engine's reach a bot hears at an Alertness level: 0.5 at 0, the whole reach at 10.</summary>
    public static float ReachShare(int alertness) => Math.Clamp(0.5f + 0.05f * alertness, 0f, 1f);

    /// <summary>The heard place's error as a share of the distance: 0.3 at Alertness 0 down to 0.05 at 10.</summary>
    public static float ErrorShare(int alertness) => Math.Clamp(0.3f - 0.025f * alertness, 0.05f, 0.3f);

    /// <summary>How far a sound at <paramref name="soundLevel"/> carries (CPASAttenuationFilter's reach); infinite at no attenuation.</summary>
    public static float Reach(int soundLevel)
    {
        float attn = SoundRecipients.SoundLevelToAttenuation(soundLevel);
        return attn <= 0f ? float.PositiveInfinity : 2f * SoundRecipients.NormalClipDistance / attn;
    }

    public void OnEmitted(in EmittedSound sound) => Deliver(in sound, "sound");

    /// <summary>A step the clients in the walker's PVS make themselves: heard as a client in the PVS hears it.</summary>
    public void OnClientStep(in EmittedSound step) => Deliver(in step, "client step");

    private void Deliver(in EmittedSound sound, string way)
    {
        if (bots.Bots.Count == 0 || sound.Recipients.IsEmpty)
            return;

        (SoundKind kind, int weapon) = classifier.Classify(sound.SoundName);
        ActorId source = sound.Owner is >= 1 and < VisionPass.Slots ? senses.ActorAt(sound.Owner) : ActorId.None;
        var tick = new Tick(frame.TickCount());
        foreach (Hl2mpBot b in bots.Bots)
        {
            EntityHandle player = bots.Applier.PlayerOf(b.Id);
            if (player != EntityHandle.None && senses.LogSight)
            {
                // The truth a scenario's facts compare the heard events with (§6.6, ss_ai_hearing).
                AiVector me = hud.Read(player).Origin;
                var at = new AiVector(sound.Origin.X, sound.Origin.Y, sound.Origin.Z);
                log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] {way} {tick.Value} to {b.Id.Slot}: {sound.SoundName} owner {sound.Owner} in {(sound.Recipients.Contains(b.Id.Slot) ? 1 : 0)} "
                    + $"dist {me.DistanceTo(at):F0} reach {Reach(sound.SoundLevel):F0} share {ReachShare(Level(b.Skills, "alertness")):F2} kind {kind} weapon {weapon}\n"));
            }

            if (player == EntityHandle.None || !sound.Recipients.Contains(b.Id.Slot))
                continue;
            if (TryHear(b.Id, hud.Read(player).Origin, Level(b.Skills, "alertness"), in sound, kind, weapon, source, tick, Rng(b.Id), out AiEvent e))
            {
                senses.Push(e);
                heard.Add(in e);
                Heard++;
            }
        }
    }

    /// <summary>
    /// Whether the bot <paramref name="bot"/>, standing at <paramref name="at"/>,
    /// hears <paramref name="sound"/>: only when its index is in the recipient
    /// set, never its own, and within the share of the sound's reach its
    /// Alertness allows; the event carries the heard place with its error.
    /// </summary>
    public static bool TryHear(ActorId bot, AiVector at, int alertness, in EmittedSound sound, SoundKind kind, int weapon, ActorId source,
                               Tick tick, AiRandom rng, out AiEvent heard)
    {
        heard = default;
        if (bot.Slot == sound.Owner || !sound.Recipients.Contains(bot.Slot))
            return false;

        var origin = new AiVector(sound.Origin.X, sound.Origin.Y, sound.Origin.Z);
        float reach = Reach(sound.SoundLevel);
        float d = at.DistanceTo(origin);
        if (d > reach * ReachShare(alertness))
            return false;

        float loudness = float.IsInfinity(reach) ? 1f : Math.Clamp(1f - d / reach, 0f, 1f);
        float angle = (float)(rng.NextUInt64() % 3600) * (MathF.PI / 1800f);
        float err = d * ErrorShare(alertness) * ((rng.NextUInt64() % 1000) / 1000f);
        var place = new AiVector(origin.X + MathF.Cos(angle) * err, origin.Y + MathF.Sin(angle) * err, origin.Z);
        heard = AiEvent.SoundHeard(bot, tick, kind, place, loudness, source, weapon);
        return true;
    }

    private AiRandom Rng(ActorId bot)
    {
        if (!_random.TryGetValue(bot.Slot, out AiRandom? r))
            _random[bot.Slot] = r = new AiRandom(AiRandom.SeedFor(Hl2mpBots.MatchSeed ^ 0x4541_5253UL, bot.Serial));
        return r;
    }

    private static int Level(SkillProfile p, string key)
    {
        int i = p.IndexOf(key);
        return i < 0 ? 0 : Math.Clamp(p.Levels[i], 0, StandardSkills.Cap);
    }
}
