using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a natural flame tells the client about itself, which is two things.
///
/// <para>
/// Stock's <c>DT_FireSmoke</c> carried six props and the client read two of
/// them. That is not a simplification made here — it is what the file had
/// already become. <c>C_FireSmoke::UpdateScale</c>, <c>UpdateFlames</c>,
/// <c>UpdateAnimation</c>, <c>SpawnSmoke</c>, <c>AddFlames</c>,
/// <c>FindClipPlane</c> and <c>Simulate</c> are all empty bodies, so
/// <c>m_flScale</c> and <c>m_flScaleTime</c> arrive through two recv proxies
/// that write members nothing ever reads, and the two model indices are read by
/// nobody at all. The whole of the client's behaviour is
/// <c>Start()</c>: pick one of eight particle systems and create it.
/// </para>
///
/// <para>
/// So this networks exactly what that decision needs. Sending the four dead
/// props to keep the shape would be sending them nowhere.
/// </para>
///
/// <para>
/// WHAT IS NOT HERE AND WHY. There is no "active" flag, though stock has a
/// <c>bitsFIRE_ACTIVE</c> bit and <c>env_fire</c> sets it. Nothing on the client
/// has ever read it — <c>ShouldDraw</c> returns a bare <c>true</c> and
/// <c>Simulate</c> does nothing — so a flame's existence has always been its
/// activity, and making the bit mean something here would make a managed fire
/// go out visibly earlier than a stock one. The entity's lifetime is the
/// flame's lifetime, which is what stock actually does.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FireSmoke)]
public partial class FireSmokeNetState
{
    /// <summary>
    /// How tall the flame is, in world units — stock's <c>m_flFireSize</c>.
    ///
    /// <para>
    /// A SIZE, not a scale, and the distinction is the bug this port fixed. It
    /// is divided by 36 to pick one of four particle systems, so a normalised
    /// 0..1 value picks the smallest one every time. See
    /// <see cref="FireParticleNames"/>.
    /// </para>
    ///
    /// <para>
    /// A networked property cannot carry an initialiser, so the floor for a
    /// size that never arrived lives in <see cref="FireParticleNames"/> where
    /// the divide is — which is the better place for it anyway: it guards every
    /// route in, not just the one that forgot to set a field.
    /// </para>
    /// </summary>
    public partial float FlameSize { get; set; }

    /// <summary>
    /// Whether this fire smokes — stock's <c>bitsFIRESMOKE_SMOKE</c>, which the
    /// mapper sets by NOT ticking <c>SF_FIRE_SMOKELESS</c>.
    ///
    /// It picks between the <c>_smoke</c> and plain variants of whichever size
    /// bucket the flame landed in, so it is half of the particle system's name
    /// and belongs beside the other half.
    /// </summary>
    public partial bool Smoke { get; set; }
}
