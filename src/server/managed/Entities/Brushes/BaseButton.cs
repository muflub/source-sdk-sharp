//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// CBaseButton (buttons.cpp): a brush that travels in when pressed, fires,
/// waits and travels back, or stays in until pressed again (toggle) or for
/// good (a wait of -1). Pressed by +USE, by touch or by damage as its
/// spawnflags say, and by the Press, PressIn and PressOut inputs. Its noise
/// is the <c>sounds</c> key's "Buttons.sndN"; a locked press plays the
/// <c>locked_sound</c> and an unlocked one the <c>unlocked_sound</c>.
///
/// <para>
/// Not here: the locked and unlocked SENTENCES (PlayLockSounds' sentence
/// groups), which need the engine's sentence system this build has no seam
/// for; and m_nState's "alternate textures", which nothing draws.
/// </para>
/// </summary>
public abstract class BaseButton : ScriptedEntity
{
    protected BaseButton(IGameClock clock, IEngineHost host, IEnginePrecache precache, IEngineSound sound,
                         IEngineEffects effects)
    {
        Clock = clock;
        Host = host;
        Precache = precache;
        Sound = sound;
        Effects = effects;
    }

    protected readonly IGameClock Clock;
    private readonly IEngineHost Host;
    private readonly IEnginePrecache Precache;
    private readonly IEngineSound Sound;
    private readonly IEngineEffects Effects;

    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public readonly Output OnPressed = new();

    /// <summary>
    /// "OnDamaged" to a map. Named without the "On" because the class also
    /// overrides <see cref="ScriptedEntity.OnDamaged"/>, the damage virtual.
    /// </summary>
    public readonly Output Damaged = new("OnDamaged");

    public readonly Output OnUseLocked = new();
    public readonly Output OnIn = new();
    public readonly Output OnOut = new();

    /// <summary>CBaseButton's spawnflags.</summary>
    [Flags]
    protected enum ButtonFlag
    {
        None = 0,

        /// <summary>SF_BUTTON_DONTMOVE (func_button) and SF_ROTBUTTON_NOTSOLID (func_rot_button).</summary>
        DontMove = 1,

        /// <summary>SF_BUTTON_TOGGLE: stays pushed until pressed again.</summary>
        Toggle = 32,

        /// <summary>SF_BUTTON_TOUCH_ACTIVATES.</summary>
        TouchActivates = 256,

        /// <summary>SF_BUTTON_DAMAGE_ACTIVATES.</summary>
        DamageActivates = 512,

        /// <summary>SF_BUTTON_USE_ACTIVATES.</summary>
        UseActivates = 1024,

        /// <summary>SF_BUTTON_LOCKED: starts locked.</summary>
        Locked = 2048,

        /// <summary>
        /// SF_BUTTON_SPARK_IF_OFF. Also SF_DOOR_SILENT, the bit PlayLockSounds
        /// reads, so a sparking button plays no lock sounds, as in stock.
        /// </summary>
        SparkIfOff = 4096,
    }

    protected ButtonFlag Flags => SpawnFlagsAs<ButtonFlag>();

    /// <summary>CBaseButton::ObjectCaps: usable, and found in the radius, when use activates it.</summary>
    public override UseCaps ObjectCaps
        => base.ObjectCaps | (Flags.HasFlag(ButtonFlag.UseActivates) ? UseCaps.ImpulseUse | UseCaps.UseInRadius : UseCaps.None);

    /// <summary>What a map may set on every button. See <see cref="BaseButtonVars"/>.</summary>
    [ScriptVars] public BaseButtonVars ButtonVars = new();

    /// <summary>CBaseToggle's TOGGLE_STATE.</summary>
    protected enum ToggleState { AtTop = 0, AtBottom = 1, GoingUp = 2, GoingDown = 3 }

    /// <summary>m_toggle_state.</summary>
    [SaveField] protected ToggleState State = ToggleState.AtBottom;

    [SaveField] private bool _locked;

    /// <summary>m_fStayPushed: a wait of -1 leaves it in.</summary>
    private bool StayPushed => ButtonVars.Wait == -1f;

    /// <summary>m_sNoise: "Buttons.sndN", or empty for sounds 0.</summary>
    private string _noise = string.Empty;

    private string _lockedSound = string.Empty, _unlockedSound = string.Empty;

    /// <summary>locksound_t::flwaitSound, shared by both lock sounds.</summary>
    private float _lockSoundWait;

    /// <summary>m_flUseLockedTime.</summary>
    private float _useLockedTime;

    private EntityHandle _activator = EntityHandle.None;

    /// <summary>Whether the button responds to a +USE at all (CBaseButton: the flag; CRotButton: always).</summary>
    protected virtual bool UseEnabled => Flags.HasFlag(ButtonFlag.UseActivates);

    /// <summary>m_takedamage: CBaseButton::Spawn sets DAMAGE_YES.</summary>
    public override bool TakesDamage => true;

    /// <summary>Starts the travel in (LinearMove or AngularMove to the pressed end).</summary>
    protected abstract void TravelIn();

    /// <summary>Starts the travel back out.</summary>
    protected abstract void TravelOut();

    /// <summary>MakeButtonSound: "Buttons.sndN".</summary>
    internal static string ButtonSound(int sound) => $"Buttons.snd{sound}";

    /// <summary>The noise, lock sounds, defaults and start state both buttons' Spawn share.</summary>
    protected void SpawnButton()
    {
        _noise = ButtonVars.Sounds != 0 ? ButtonSound(ButtonVars.Sounds) : string.Empty;
        if (_noise.Length > 0)
            Precache.PrecacheSound(_noise);

        // CBaseButton::Precache.
        if ((int)ButtonVars.LockedSound != 0)
            Precache.PrecacheSound(_lockedSound = ButtonSound((int)ButtonVars.LockedSound));
        if ((int)ButtonVars.UnlockedSound != 0)
            Precache.PrecacheSound(_unlockedSound = ButtonSound((int)ButtonVars.UnlockedSound));

        if (ButtonVars.Speed == 0f)
            ButtonVars.Speed = 40f;
        if (ButtonVars.Wait == 0f)
            ButtonVars.Wait = 1f;

        State = ToggleState.AtBottom;
        _locked = Flags.HasFlag(ButtonFlag.Locked);

        if (Flags.HasFlag(ButtonFlag.SparkIfOff))
            SetContextThink(SparkContext, 0.5f);
    }

    [ScriptedEvent] private void OnLock() => _locked = true;
    [ScriptedEvent] private void OnUnlock() => _locked = false;

    private enum ButtonCode { Nothing, Activate, Return, Press }

    [ScriptedEvent("Press")] private void OnPressInput(InputData input) => Press(input.Activator, ButtonCode.Press);
    [ScriptedEvent("PressIn")] private void OnPressIn(InputData input) => Press(input.Activator, ButtonCode.Activate);
    [ScriptedEvent("PressOut")] private void OnPressOut(InputData input) => Press(input.Activator, ButtonCode.Return);

    /// <summary>CBaseButton::Press: the three inputs.</summary>
    private void Press(EntityHandle activator, ButtonCode code)
    {
        bool moving = State is ToggleState.GoingUp or ToggleState.GoingDown;
        if (code == ButtonCode.Press && moving)
            return;
        if (code == ButtonCode.Activate && State is ToggleState.GoingUp or ToggleState.AtTop)
            return;
        if (code == ButtonCode.Return && State is ToggleState.GoingDown or ToggleState.AtBottom)
            return;

        if (_locked)
        {
            PlayLockSounds(locked: true);
            return;
        }

        if ((code == ButtonCode.Press && State == ToggleState.AtTop)
            || (code == ButtonCode.Return && State is ToggleState.AtTop or ToggleState.GoingUp))
        {
            PlayNoise();
            OnPressed.Fire(activator);
            ButtonReturn();
        }
        else if (code == ButtonCode.Press
                 || (code == ButtonCode.Activate && State is ToggleState.AtBottom or ToggleState.GoingDown))
        {
            OnPressed.Fire(activator);
            ButtonActivate();
        }
    }

    /// <summary>CBaseButton::ButtonUse, which a player's +USE reaches.</summary>
    [ScriptedEvent("Use")]
    private void OnUsed(InputData input)
    {
        EntityHandle activator = input.Activator;

        if (!UseEnabled)
            return;

        if (State is ToggleState.GoingUp or ToggleState.GoingDown)
            return;

        if (_locked)
        {
            OnUseLockedBy(activator);
            return;
        }

        _activator = activator;

        if (State == ToggleState.AtTop)
        {
            // A toggle returns now; any other either returns on its own or stays.
            if (Flags.HasFlag(ButtonFlag.Toggle))
            {
                PlayNoise();
                OnPressed.Fire(_activator);
                ButtonReturn();
            }
        }
        else
        {
            OnPressed.Fire(_activator);
            ButtonActivate();
        }
    }

    /// <summary>CBaseButton::OnUseLocked: the lock sounds, and the output at most twice a second.</summary>
    private void OnUseLockedBy(EntityHandle activator)
    {
        PlayLockSounds(locked: true);

        if (Clock.Now > _useLockedTime)
        {
            OnUseLocked.Fire(activator);
            _useLockedTime = Clock.Now + 0.5f;
        }
    }

    /// <summary>CBaseButton::ButtonResponseToTouch.</summary>
    private ButtonCode ResponseToTouch()
    {
        // Ignore touches while moving, or pushed in and waiting to come out on its own.
        if (State is ToggleState.GoingUp or ToggleState.GoingDown
            || (State == ToggleState.AtTop && !StayPushed && !Flags.HasFlag(ButtonFlag.Toggle)))
            return ButtonCode.Nothing;

        if (State == ToggleState.AtTop)
            return Flags.HasFlag(ButtonFlag.Toggle) && !StayPushed ? ButtonCode.Return : ButtonCode.Nothing;

        return ButtonCode.Activate;
    }

    /// <summary>CBaseButton::ButtonTouch: players only.</summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (!Flags.HasFlag(ButtonFlag.TouchActivates) || !Entities.IsPlayer(toucher))
            return;

        _activator = toucher;
        ButtonCode code = ResponseToTouch();
        if (code == ButtonCode.Nothing)
            return;

        if (_locked)
        {
            PlayLockSounds(locked: true);
            return;
        }

        ActivateOrReturn(code);
    }

    /// <summary>CBaseButton::OnTakeDamage, in stock's order: the output first, whatever follows.</summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (!TakesDamage)
            return 0;

        Damaged.Fire(_activator);

        // "Remove obsolete health keyvalue from func_button."
        if (!Flags.HasFlag(ButtonFlag.DamageActivates) && ButtonVars.Health == 0)
            return 0;

        ButtonCode code = ResponseToTouch();
        if (code == ButtonCode.Nothing)
            return 0;

        _activator = attacker;
        if (_activator == EntityHandle.None || _locked)
            return 0;

        ActivateOrReturn(code);
        return 0;
    }

    private void ActivateOrReturn(ButtonCode code)
    {
        if (code == ButtonCode.Return)
        {
            PlayNoise();
            OnPressed.Fire(_activator);
            ButtonReturn();
        }
        else
        {
            OnPressed.Fire(_activator);
            ButtonActivate();
        }
    }

    /// <summary>CBaseButton::ButtonActivate: the noise, the lock check, and the travel in.</summary>
    private void ButtonActivate()
    {
        PlayNoise();

        if (_locked)
        {
            PlayLockSounds(locked: true);
            return;
        }

        PlayLockSounds(locked: false);

        State = ToggleState.GoingUp;
        TravelIn();
    }

    /// <summary>CBaseButton::ButtonReturn.</summary>
    private void ButtonReturn()
    {
        State = ToggleState.GoingDown;
        TravelOut();
    }

    public override void OnMoveDone()
    {
        if (State == ToggleState.GoingUp)
            TriggerAndWait();
        else if (State == ToggleState.GoingDown)
            ButtonBackHome();
    }

    /// <summary>CBaseButton::TriggerAndWait: in, OnIn, and the wait before coming out unless it stays.</summary>
    private void TriggerAndWait()
    {
        if (_locked)
            return;

        State = ToggleState.AtTop;

        // SetThink( ButtonReturn ) takes the think a sparking button had.
        if (!StayPushed && !Flags.HasFlag(ButtonFlag.Toggle))
        {
            StopThinking(SparkContext);
            SetContextThink(ReturnContext, ButtonVars.Wait);
        }

        OnIn.Fire(_activator);
    }

    /// <summary>CBaseButton::ButtonBackHome: out, OnOut, and the sparks again.</summary>
    private void ButtonBackHome()
    {
        State = ToggleState.AtBottom;
        OnOut.Fire(_activator);

        if (Flags.HasFlag(ButtonFlag.SparkIfOff))
            SetContextThink(SparkContext, 0.5f);
    }

    private const string ReturnContext = "Return", SparkContext = "Spark";

    public override void OnThink(string context)
    {
        if (context == ReturnContext)
        {
            if (State == ToggleState.AtTop)
                ButtonReturn();
        }
        else if (context == SparkContext)
        {
            ButtonSpark();
        }
    }

    /// <summary>CBaseButton::ButtonSpark: a spark at the middle, again in 0.1 to 1.6 s.</summary>
    private void ButtonSpark()
    {
        SetContextThink(SparkContext, 0.1f + Host.RandomFloat(0f, 1.5f));
        Effects.SparkEffect(Entities.GetWorldSpaceCenter(Handle), 1, 1, makeSound: true);
    }

    /// <summary>m_sNoise on CHAN_VOICE (the script's own parameters, as a script name plays in stock).</summary>
    private void PlayNoise()
    {
        if (_noise.Length > 0)
            Sound.EntityEmitSound(Handle, _noise);
    }

    /// <summary>
    /// PlayLockSounds for a button: the locked or unlocked sound, no more
    /// often than BUTTON_SOUNDWAIT (half a second); nothing at all with
    /// SF_DOOR_SILENT.
    /// </summary>
    private void PlayLockSounds(bool locked)
    {
        if (Flags.HasFlag(ButtonFlag.SparkIfOff))
            return;

        string sound = locked ? _lockedSound : _unlockedSound;
        if (sound.Length == 0 || Clock.Now <= _lockSoundWait)
            return;

        Sound.EntityEmitSound(Handle, sound);
        _lockSoundWait = Clock.Now + 0.5f;
    }
}

/// <summary>
/// The map keys every button declares.
/// </summary>
[ScriptVars]
public partial class BaseButtonVars
{
    /// <summary>Seconds before it comes back out. -1 means it stays in.</summary>
    public float Wait { get; set; } = 1f;

    public float Speed { get; set; } = 40f;

    /// <summary>The "Buttons.sndN" it makes when pressed; 0 for none.</summary>
    public int Sounds { get; set; }

    [VarName("locked_sound")]
    public float LockedSound { get; set; }

    [VarName("unlocked_sound")]
    public float UnlockedSound { get; set; }

    /// <summary>The obsolete health key: non-zero lets damage press a button without the spawnflag.</summary>
    public int Health { get; set; }
}
