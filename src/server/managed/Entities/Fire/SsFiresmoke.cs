using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// _firesmoke: the flame a natural fire is made of.
///
/// <para>
/// It replaces <c>_firesmoke</c>, which was a native <c>CFireSmoke</c> paired
/// with a native <c>C_FireSmoke</c> — and between them 847 references into
/// <c>baseentity.o</c> and <c>c_baseentity.o</c>, of which 95% existed only
/// because those two classes DERIVED from the entity. Not one of those was a
/// call a retype could have moved. The only way to stop inheriting is to stop
/// being a C++ entity.
/// </para>
///
/// <para>
/// WHY THIS IS ALLOWED TO BE A DIFFERENT CLASSNAME, where most of this port
/// keeps stock's. <c>_firesmoke</c> is not a name a mapper writes and not a name
/// any map in this tree contains — the entity lumps of both shipped maps were
/// read to check, and both say zero. It is created by code, and until now that
/// code was <c>CFire::SpawnEffect</c> and a shim beside it. The one caller is
/// <see cref="EnvFire"/>, which is C# already, so there is no mapper-facing
/// contract to preserve. The <c>ss_</c> prefix follows <see cref="SsBeam"/>:
/// this mod's own, spawned by nothing but its owner.
/// </para>
///
/// <para>
/// NOTHING IS DECIDED HERE. What burns, how hot, for how long, what it sets
/// alight and what it spreads to is <see cref="EnvFire"/>. This is told how big
/// it is and whether it smokes, and it networks those two things; the client
/// picks a particle system from the pair and starts it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FireSmoke, ScriptedEntityKind.Effect)]
public sealed class SsFiresmoke : ScriptedEntity
{
    /// <summary>
    /// The same declaration the client half reads. Written here, decoded there;
    /// there is no second copy of the layout.
    /// </summary>
    [NetState] public FireSmokeNetState State = new();

    /// <summary>
    /// How big a flame this is, and whether it smokes.
    ///
    /// <para>
    /// Applied once by whoever built it, which is <see cref="EnvFire"/>. Both
    /// halves of the particle system's name are set together because neither
    /// means anything alone — see <see cref="FireParticleNames"/>.
    /// </para>
    ///
    /// <para>
    /// The size is in WORLD UNITS, which is the whole of the fix this port
    /// carries: <c>env_fire</c> used to hand the flame <c>heat / MaxHeatLevel</c>
    /// instead, a ratio that cannot exceed 1, so the divide that picks the
    /// bucket answered zero for every fire in every map and all of them drew as
    /// <c>env_fire_tiny</c> whatever their firesize said.
    /// </para>
    /// </summary>
    public void SetLook(float flameSize, bool smoke)
    {
        State.FlameSize = flameSize;
        State.Smoke     = smoke;
    }
}
