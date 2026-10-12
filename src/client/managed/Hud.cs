using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The HUD's state: the damage the local player last took, what a map has
/// asked to say (<see cref="HudMessages"/>) and to instruct
/// (<see cref="HudHints"/>), and the build stamp. It draws nothing: the HUD is
/// the toolkit's screen (SourceSharp.Client.Ui.Screens.Hud), which reads this
/// state through the core's sources (plan_client_ui U2).
/// </summary>
public sealed class Hud : IHud
{

    public Hud(ILog log, IGameClock clock, IEngineHud screen,
               IUserMessageEvent<DamageArgs> damage,
               IUserMessageEvent<HudMessageArgs> hudMessages,
               IUserMessageEvent<string> hudTitles,
               IUserMessageEvent<IReadOnlyList<string>> keyHints,
               BuildInfo build,
               ViewReadout? view = null)
    {
        Clock = clock;
        _damage = damage;
        _hudMessages = hudMessages;
        _hudTitles = hudTitles;
        _keyHints = keyHints;

        // The two the HUD owns, handed the same engine it was. Constructed here
        // rather than in a field initialiser, which runs before the parameters
        // exist.
        _messages = new HudMessages(log, clock, screen);
        _hints = new HudHints(log, screen);
        _build = new HudBuildStamp(screen, build);

        // When and where, under the build: what makes a screenshot evidence.
        if (view is not null)
            _build.DebugLine = () => view.Known
                ? ViewReadout.Line(clock.Now, view.Eye, view.Pitch, view.Yaw)
                : null;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    // This side's user message events, from the container. They were static
    // events on the handler classes.
    private readonly IUserMessageEvent<DamageArgs> _damage;
    private readonly IUserMessageEvent<HudMessageArgs> _hudMessages;
    private readonly IUserMessageEvent<string> _hudTitles;
    private readonly IUserMessageEvent<IReadOnlyList<string>> _keyHints;


    /// <summary>Seconds a damage indicator takes to fade out.</summary>
    private const float DamageFadeSeconds = 1.0f;

    private float _lastDamageAt = float.NegativeInfinity;

    /// <summary>What a map has asked to say, via game_text and env_message.</summary>
    private readonly HudMessages _messages;

    /// <summary>What a map has asked to instruct, via env_hudhint.</summary>
    private readonly HudHints _hints;

    /// <summary>Which build this is, bottom right. See <see cref="HudBuildStamp"/>.</summary>
    private readonly HudBuildStamp _build;

    /// <summary>The build stamp, exposed so the module can hand it its convar.</summary>
    public HudBuildStamp Build => _build;

    /// <summary>Subscribes to the damage message that drives the indicator.</summary>
    public void Subscribe()
    {
        // The damage indicator's data comes from the standard "Damage" user
        // message, which is already decoded in shared code — so the HUD needs no
        // engine access of its own to know it was hit.
        _damage.Fired += OnDamage;

        _messages.Subscribe(_hudMessages, _hudTitles);
        _hints.Subscribe(_keyHints);
    }

    /// <summary>The map's text, exposed so its behaviour can be driven in a test.</summary>
    public HudMessages Messages => _messages;

    /// <summary>The map's hints, exposed for the same reason.</summary>
    public HudHints Hints => _hints;

    // The same two, as the container hands them out. Explicit rather than a
    // widening of the properties above, because the concrete types are what
    // every existing caller and every test uses and narrowing those to the
    // interface would take away the half of each class that is not on it.
    IHudMessages IHud.Messages => _messages;
    IHudHints IHud.Hints => _hints;

    // Stock's CHudDamageIndicator::MsgFunc_Damage (hl2/hud_damageindicator.cpp):
    // damage with no origin is ignored unless it is drowning ("this should never
    // happen, unless it's drowning damage"), and a message that took neither
    // health nor armour starts no animation.
    private void OnDamage(DamageArgs damage)
    {
        bool noOrigin = damage.Origin.X == 0f && damage.Origin.Y == 0f && damage.Origin.Z == 0f;
        if (noOrigin && (damage.DamageBits & (int)DamageType.Drown) == 0)
            return;
        // The server's empty reports (after a spawn, the frame after a hit) change nothing.
        if (damage.Health <= 0 && damage.Armour <= 0)
            return;
        _lastDamageAt = Clock.Now;
        _lastDamageOrigin = damage.Origin;
    }

    private Vector _lastDamageOrigin;

    /// <summary>Where the last hit came from (the attacker's origin); zero when the game did not say.</summary>
    public Vector LastDamageOrigin => _lastDamageOrigin;

    /// <summary>When the local player was last hurt (the clock's time), for the UI's damage panel.</summary>
    public float LastDamageAt => _lastDamageAt;

    /// <summary>Test seam: how faded the damage indicator is, 1 = fresh.</summary>
    public float DamageFraction(float now)
    {
        float elapsed = now - _lastDamageAt;
        if (float.IsInfinity(elapsed) || elapsed >= DamageFadeSeconds || elapsed < 0f)
            return 0f;

        return 1f - (elapsed / DamageFadeSeconds);
    }
}
