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
/// A brush a player presses. It does not move in this mod — a button whose
/// travel is a centimetre is doing nothing a map can see — but it locks out
/// while "pressed" so a single press fires once.
/// </summary>
[ScriptedEntity(EntityNames.FuncButton, ScriptedEntityKind.Brush)]
public sealed class FuncButton : ScriptedEntity
{
    public readonly Output OnPressed = new();

    /// <summary>
    /// Named without the "On" because the class also OVERRIDES
    /// <see cref="ScriptedEntity.OnDamaged"/> — the virtual that decides how
    /// much damage is taken. A field and a method cannot share a name, and of
    /// the two it is the output that can be renamed without changing what a map
    /// writes: the mapper-visible name is still "OnDamaged", given explicitly
    /// here, so every connection stock's FGD describes still resolves.
    ///
    /// <para>
    /// It was declared as a bare <c>OnDamaged</c> field once, and that hid the
    /// method, which meant nothing could override the damage handler and the
    /// output could never fire. See docs/divergences.md.
    /// </para>
    /// </summary>
    public readonly Output Damaged = new("OnDamaged");

    public readonly Output OnUseLocked = new();
    public readonly Output OnIn = new();
    public readonly Output OnOut = new();

    /// <summary>
    /// Stock's <c>func_button</c> spawnflags. Only the one this entity acts on
    /// is named — the rest of stock's set is about movement and sound, which a
    /// button that does not move has nothing to do with.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0,

        /// <summary>SF_BUTTON_DAMAGE_ACTIVATES: being shot is how it is pressed.</summary>
        DamageActivates = 512,
    }

    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncButtonVars"/>.</summary>
    [ScriptVars] public FuncButtonVars Vars = new();

    private bool _locked;
    private bool _spent;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);
        _locked = Vars.StartDisabled;
    }

    [ScriptedEvent] private void OnLock() => _locked = true;
    [ScriptedEvent] private void OnUnlock() => _locked = false;

    [ScriptedEvent("Press")] private void OnPress() => Press();
    [ScriptedEvent("Use")] private void OnUsed() => Press();

    /// <summary>
    /// Something damaged the button. Stock's <c>CBaseButton::OnTakeDamage</c>,
    /// in stock's order.
    ///
    /// <para>
    /// The output fires FIRST and UNCONDITIONALLY — before the spawnflag, before
    /// the lock, before the spent check. That is stock's first line, and it
    /// matters: a map wiring <c>OnDamaged</c> is told the button was hit even
    /// when the hit does nothing to the button itself.
    /// </para>
    ///
    /// <para>
    /// Stock's next line is <c>if (!DAMAGE_ACTIVATES &amp;&amp; m_iHealth == 0) return 0</c>,
    /// guarding what its own comment calls an obsolete health keyvalue. This
    /// button has no health key at all, so it is always the zero half of that
    /// test and the spawnflag alone decides whether being shot presses it.
    /// </para>
    ///
    /// <para>
    /// The locked and already-pressed cases are silent returns here, not
    /// <see cref="OnUseLocked"/> firings: stock's damage path returns without a
    /// lock sound or an output, unlike its use path, which reports it. So the
    /// gates are asked here rather than left to <see cref="Press()"/>.
    /// </para>
    ///
    /// <para>
    /// Returns zero on every path, as stock does — a button is not worn down by
    /// being shot, whatever pressing it costs.
    /// </para>
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        Damaged.Fire(attacker);

        if (!Flags.HasFlag(SpawnFlag.DamageActivates))
            return 0;

        if (_spent || _locked)
            return 0;

        Press(attacker);
        return 0;
    }

    private void Press() => Press(EntityHandle.None);

    private void Press(EntityHandle activator)
    {
        // The other half of the [SourceSharp/Use] line in the shell: that one
        // says the press arrived, this one says what was done with it. A button
        // that is spent or locked looks exactly like one that is broken.
        if (_spent)
        {
            Log.Msg($"[SourceSharp/Button] '{Name}' is spent; ignoring\n");
            return;
        }

        if (_locked)
        {
            Log.Msg($"[SourceSharp/Button] '{Name}' is locked\n");
            OnUseLocked.Fire();
            return;
        }

        Log.Msg($"[SourceSharp/Button] '{Name}' pressed\n");

        OnPressed.Fire(activator);
        OnIn.Fire(activator);

        if (Vars.Wait < 0f)
        {
            // A negative wait is a one-shot button, which a map uses for
            // anything that should not be repeatable.
            _spent = true;
            return;
        }

        _locked = true;
        SetNextThink(Vars.Wait);
    }

    public override void OnThink()
    {
        _locked = false;
        OnOut.Fire();
    }
}

/// <summary>
/// The map keys <see cref="FuncButton"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncButtonVars
{
    /// <summary>Seconds before it can be pressed again. Negative means once only.</summary>
    public float Wait { get; set; } = 1f;

    public bool StartDisabled { get; set; }
}
