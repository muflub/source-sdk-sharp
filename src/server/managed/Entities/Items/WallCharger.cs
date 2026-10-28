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
/// The wall chargers' machine, which CNewWallHealth (item_healthkit.cpp:529)
/// and CNewRecharge (func_recharge.cpp:583) each spell out: a static model
/// a player holds +USE on, giving one point every tenth of a second while
/// its juice lasts, with a start sound, a charging loop, a deny when it is
/// empty or the player is full, and a refill some seconds after it runs dry.
/// The juice is shown on the model as its "idle" cycle.
///
/// <para>
/// The display shows the two ends stock shows: full ("idle", cycle 0) and
/// empty ("emptyclick" played to its last frame, as stock's cycle 1). The
/// partial level between them is not shown: this side cannot hold a cycle
/// still at a chosen value (docs/todo.md).
/// </para>
/// </summary>
public abstract class WallCharger : AnimatingEntity
{
    /// <summary>CHARGE_RATE: how long the machine waits on a held key before it turns off.</summary>
    public const float ChargeRate = 0.25f;

    /// <summary>CHARGES_PER_SECOND.</summary>
    public const float ChargesPerSecond = 1f / ChargeRate;

    /// <summary>CALLS_PER_SECOND: the display's drain per use.</summary>
    public const float CallsPerSecond = 7f * ChargesPerSecond;

    /// <summary>The fastest one point follows another.</summary>
    public const float ChargeInterval = 0.1f;

    /// <summary>How long a deny sound holds off the next.</summary>
    public const float DenyInterval = 0.62f;

    /// <summary>How long the start sound plays before the loop takes over.</summary>
    public const float StartToLoop = 0.56f;

    private const string OffContext = "ChargerOff";
    private const string RechargeContext = "ChargerRecharge";

    protected readonly IEngineSound Sound;
    protected readonly IGameClock Clock;
    private readonly IEngineEntityRender _render;

    protected WallCharger(IEngineSound sound, IGameClock clock, IEngineEntityRender render)
    {
        Sound = sound;
        Clock = clock;
        _render = render;
    }

    public readonly Output OnPlayerUse = new();

    /// <summary>The model.</summary>
    protected abstract string Model { get; }

    /// <summary>The juice a full charger holds.</summary>
    protected abstract float MaxJuice { get; }

    /// <summary>The game rules' wait before an empty one refills (FlHealthChargerRechargeTime, FlHEVChargerRechargeTime).</summary>
    protected abstract float RechargeDelay { get; }

    protected abstract string DenySound { get; }
    protected abstract string StartSound { get; }
    protected abstract string LoopSound { get; }
    protected abstract string RechargeSound { get; }

    /// <summary>The display's drain per second of use; the citadel charger's is ten times.</summary>
    protected virtual float DisplayChargesPerSecond => ChargesPerSecond;

    /// <summary>
    /// StudioFrameAdvance's divisor: the health charger's whole charge, the
    /// suit charger's plus a tenth (CNewRecharge::StudioFrameAdvance).
    /// </summary>
    protected virtual float DisplayMaxJuice => MaxJuice;

    /// <summary>The sequence and cycle the client draws. See <see cref="IChargerNetState"/>.</summary>
    protected abstract IChargerNetState Display { get; }

    /// <summary>Whether this player may use it at all (the suit charger asks for the suit).</summary>
    protected virtual bool CanUse(EntityHandle player) => true;

    /// <summary>Whether this player has all this charger can give.</summary>
    protected abstract bool IsFull(EntityHandle player);

    /// <summary>One charge given to the player, taken from the juice.</summary>
    protected abstract void GiveCharge(EntityHandle player);

    /// <summary>The remaining-juice output.</summary>
    protected abstract void ReportRemaining(EntityHandle player);

    /// <summary>m_iJuice: whole points left.</summary>
    [SaveField] protected int Juice;

    /// <summary>m_flJuice: the display's drained juice.</summary>
    [SaveField] private float _displayJuice;

    /// <summary>m_iOn: 0 off, 1 starting, 2 looping.</summary>
    [SaveField] private int _on;

    [SaveField] private float _soundTime;
    [SaveField] private float _nextCharge;

    /// <summary>m_nState: 1 once it has run dry and shows empty.</summary>
    [SaveField] private int _state;

    /// <summary>m_iCaps: continuous, or impulse once a full player must press again.</summary>
    [SaveField] private UseCaps _caps = UseCaps.ContinuousUse;

    /// <summary>m_iReactivate: the refill wait, while one is pending.</summary>
    [SaveField] private float _reactivate;

    public override UseCaps ObjectCaps => base.ObjectCaps | _caps;

    public override void OnSpawn()
    {
        SetModel(Model);

        // MOVETYPE_NONE, SOLID_VPHYSICS, VPhysicsInitStatic: the static object
        // sets the solidity (a brush's SetSolidity would make it SOLID_BSP,
        // which a studio model is not traced as).
        MakePhysicsStatic();
        SetVisible(true);

        Juice = (int)MaxJuice;
        _displayJuice = Juice;
        _state = 0;
        _caps = UseCaps.ContinuousUse;
        _reactivate = 0f;

        ShowFull();
    }

    /// <summary>
    /// The machine's Use: every command the key is held (continuous use),
    /// with the player as activator.
    /// </summary>
    [ScriptedEvent("Use")]
    private void OnUseInput(InputData input)
    {
        EntityHandle player = input.Activator;
        if (!Entities.IsValid(player) || !Entities.IsPlayer(player))
            return;

        float now = Clock.Now;

        // Reset to a state of continuous use.
        _caps = UseCaps.ContinuousUse;

        if (_on != 0)
        {
            _displayJuice -= DisplayChargesPerSecond / CallsPerSecond;
            StudioFrameAdvance();
        }

        if (!CanUse(player))
        {
            Deny(now);
            return;
        }

        // If there is no juice left, turn it off. Stock restarts "emptyclick"
        // on every use and Off pins its cycle at 1, so what shows is its last
        // frame; played once from the moment it ran dry, it ends there too.
        if (Juice <= 0)
        {
            if (_state != 1)
                ShowEmpty();

            _state = 1;
            Off();
            Deny(now);
            return;
        }

        if (IsFull(player))
        {
            // Make the user re-use it to get started again.
            _caps = UseCaps.ImpulseUse;
            Sound.EntityEmitSound(Handle, DenySound);
            return;
        }

        // Bumped out while it is in use.
        StopThinking(RechargeContext);
        SetContextThink(OffContext, ChargeRate);

        // Time to charge yet?
        if (_nextCharge >= now)
            return;

        // The start sound, then the loop.
        if (_on == 0)
        {
            _on++;
            Sound.EntityEmitSound(Handle, StartSound);
            _soundTime = StartToLoop + now;
            OnPlayerUse.Fire(player);
        }

        if (_on == 1 && _soundTime <= now)
        {
            _on++;
            Sound.EntityEmitSound(Handle, LoopSound);
        }

        GiveCharge(player);
        ReportRemaining(player);

        // Govern the rate of charge.
        _nextCharge = now + ChargeInterval;
    }

    /// <summary>The deny, no more often than <see cref="DenyInterval"/>.</summary>
    private void Deny(float now)
    {
        if (_soundTime > now)
            return;

        _soundTime = now + DenyInterval;
        Sound.EntityEmitSound(Handle, DenySound);
    }

    public override void OnThink(string context)
    {
        if (context == OffContext)
            Off();
        else if (context == RechargeContext)
            Recharge();
    }

    /// <summary>Off: the loop stops, and an empty charger books its refill.</summary>
    private void Off()
    {
        if (_on > 1)
            Sound.EntityStopSound(Handle, LoopSound);

        _on = 0;
        _displayJuice = Juice;
        StopThinking(OffContext);

        if (_reactivate == 0f && Juice == 0 && RechargeDelay > 0f)
        {
            _reactivate = RechargeDelay;
            SetContextThink(RechargeContext, _reactivate);
        }
    }

    /// <summary>Recharge: full again.</summary>
    protected virtual void Recharge()
    {
        Sound.EntityEmitSound(Handle, RechargeSound);
        RefillTo((int)MaxJuice);
        _state = 0;
        _displayJuice = Juice;
        _reactivate = 0f;
        ShowFull();
        StudioFrameAdvance();
    }

    /// <summary>The juice set to <paramref name="juice"/>; the suit charger reports the change.</summary>
    protected virtual void RefillTo(int juice) => Juice = juice;

    /// <summary>
    /// ResetSequence( "idle" ) and SetCycle( 1 - juice/max ): what Spawn and
    /// InputSetCharge show, the drain the juice says.
    /// </summary>
    protected void ShowFull() => Show("idle", 1f - _displayJuice / MaxJuice);

    /// <summary>
    /// InputSetCharge's display: m_flJuice is the new juice, "idle" again, and
    /// StudioFrameAdvance.
    /// </summary>
    protected void ShowCharged()
    {
        _displayJuice = Juice;
        Show("idle", 0f);
        StudioFrameAdvance();
    }

    /// <summary>"emptyclick" held at its last frame: ResetSequence, then Off's SetCycle( 1.0f ).</summary>
    private void ShowEmpty() => Show("emptyclick", 1f);

    /// <summary>StudioFrameAdvance: the cycle at 1 - m_flJuice / max, at a playback rate of 0.</summary>
    private void StudioFrameAdvance() => SetDisplayCycle(1f - _displayJuice / DisplayMaxJuice);

    private void Show(string sequenceName, float cycle)
    {
        int sequence = _render.LookupSequence(Handle, sequenceName);
        if (sequence < 0)
            return;

        _render.PlaySequence(Handle, sequence, 1f, restart: true);
        Display.Sequence = sequence;
        SetDisplayCycle(cycle);
    }

    private void SetDisplayCycle(float cycle)
    {
        cycle = Math.Clamp(cycle, 0f, 1f);
        _render.SetCycle(Handle, cycle);
        Display.Cycle = cycle;
    }

    /// <summary>For the facts: the display's drained juice.</summary>
    internal float DisplayJuice => _displayJuice;
}
