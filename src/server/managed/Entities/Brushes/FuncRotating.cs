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
/// A brush that spins. Fans, turntables, anything continuously in motion —
/// which is why it uses angular velocity rather than a destination: it never
/// arrives.
/// </summary>
[ScriptedEntity(EntityNames.FuncRotating, ScriptedEntityKind.Brush)]
public sealed class FuncRotating : ScriptedEntity
{
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncRotatingVars"/>.</summary>
    [ScriptVars] public FuncRotatingVars Vars = new();

    /// <summary>
    /// func_rotating's own spawnflags, and the cautionary case for the whole set.
    ///
    /// <para>
    /// Bits 4 and 8 are THIS entity's rotation axes. They are not
    /// <see cref="RotationAxisFlag"/>, which is 64 and 128 and belongs to the
    /// entities that go through <c>CBaseToggle::AxisDir</c>. func_rotbutton once
    /// read these two by mistake, so a mapper who ticked "rotate roll" got yaw and
    /// no error. Two enums is what makes the two sets impossible to confuse; one
    /// shared enum covering both would have been the bug written down.
    /// </para>
    ///
    /// <para>
    /// Z is the absence of both, in stock's own order: X wins over Y, and neither
    /// leaves the default.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        StartOn = 0b0000_0001,
        ReverseDirection = 0b0000_0010,
        AxisX = 0b0000_0100,
        AxisY = 0b0000_1000,
    }

    private bool _running;
    private bool _reversed;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        _reversed = Flags.HasFlag(SpawnFlag.ReverseDirection);
        _running = Flags.HasFlag(SpawnFlag.StartOn);

        Apply();
    }

    [ScriptedEvent] private void OnStart() { _running = true; Apply(); }
    [ScriptedEvent] private void OnStop() { _running = false; Apply(); }
    [ScriptedEvent] private void OnToggle() { _running = !_running; Apply(); }

    [ScriptedEvent] private void OnStartForward() { _reversed = false; _running = true; Apply(); }
    [ScriptedEvent] private void OnStartBackward() { _reversed = true; _running = true; Apply(); }
    [ScriptedEvent] private void OnReverse() { _reversed = !_reversed; Apply(); }

    [ScriptedEvent] private void OnSetSpeed(float speed)
    {
        Vars.MaxSpeed = speed;
        Apply();
    }

    private void Apply()
    {
        if (!_running)
        {
            Spin(Vector.Zero);
            return;
        }

        float rate = _reversed ? -Vars.MaxSpeed : Vars.MaxSpeed;

        // Which axis it turns about is a spawnflag, not a key. Yaw unless the
        // map says otherwise, which is what a fan on a ceiling does.
        Spin(Flags.HasFlag(SpawnFlag.AxisX) ? new Vector(rate, 0f, 0f)
           : Flags.HasFlag(SpawnFlag.AxisY) ? new Vector(0f, 0f, rate)
           : new Vector(0f, rate, 0f));
    }
}

/// <summary>
/// The map keys <see cref="FuncRotating"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncRotatingVars
{
    public float MaxSpeed { get; set; } = 100f;
}
