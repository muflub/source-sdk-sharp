using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What <c>env_fire_trail</c> tells the client, which is almost nothing.
///
/// <para>
/// <c>DT_FireTrail</c> is two props and the client class has no state of its own
/// beyond them — every size, speed and colour a flame trail uses is a #define in
/// <c>C_FireTrail::Update</c>. That makes this the cheapest of the six classes
/// in <c>smoke_trail.cpp</c> and the one that shows the family's shape with
/// nothing else in the way.
/// </para>
///
/// <para>
/// Neither field is reachable from a map. Both are <c>DEFINE_FIELD</c> on
/// stock's datadesc rather than <c>DEFINE_KEYFIELD</c>, so the only thing that
/// ever set them was <c>CFireTrail::FollowEntity</c>, called from the C++
/// creators in <c>hl2/</c> — none of which is compiled in this build. They are
/// networked anyway because they are what the class IS: a lifetime of zero
/// means the trail never stops, and an attachment of zero means the entity's
/// own origin, which is exactly what a map-placed one wants.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvFireTrail)]
public partial class FireTrailNetState
{
    /// <summary>An attachment index on a parent model. Zero is the entity's own origin.</summary>
    public partial int Attachment { get; set; }

    /// <summary>An absolute time the trail stops emitting at. ZERO means never, which is stock's own reading.</summary>
    public partial float Lifetime { get; set; }
}

/// <summary>
/// What a smoke trail tells the client: how many puffs a second, what colour
/// they fade between, how long they live, how fast and how wide they leave, how
/// big they get, and whether it is still emitting.
///
/// <para>
/// The two colours are in 0-1 SPACE, not 0-255. Stock's server takes the
/// mapper's <c>startcolor</c> key as a <c>color32</c> and divides by 255 before
/// it ever reaches the send table, and the client multiplies what arrives
/// straight into a particle colour without rescaling. A trail that networked
/// 0-255 would be white and stay white, which looks like a lighting problem
/// rather than a units one.
/// </para>
///
/// <para>
/// <see cref="StopEmitTime"/> is an ABSOLUTE time rather than a duration, which
/// is why it is a networked field at all: the client compares it to its own
/// clock every frame. Stock's <c>emittime</c> key is a duration and the server
/// adds the current time to it once, at parse; zero means never stop.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvSmoketrail)]
public partial class SmokeTrailNetState
{
    /// <summary>Puffs per second.</summary>
    public partial float SpawnRate { get; set; }

    /// <summary>The colour a puff starts at, in 0-1 space. See the class remarks.</summary>
    public partial Vector StartColor { get; set; }

    /// <summary>The colour a puff ends at, in 0-1 space.</summary>
    public partial Vector EndColor { get; set; }

    /// <summary>How solid a puff is at its most opaque.</summary>
    public partial float Opacity { get; set; }

    /// <summary>How long a puff lasts, in seconds.</summary>
    public partial float ParticleLifetime { get; set; }

    /// <summary>An ABSOLUTE time the trail stops emitting at. Zero means never.</summary>
    public partial float StopEmitTime { get; set; }

    /// <summary>The slower end of the random speed a puff drifts at.</summary>
    public partial float MinSpeed { get; set; }

    public partial float MaxSpeed { get; set; }

    /// <summary>The slower end of the speed a puff is given along the entity's forward.</summary>
    public partial float MinDirectedSpeed { get; set; }

    public partial float MaxDirectedSpeed { get; set; }

    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }

    /// <summary>How far off the entity's own origin a puff may appear.</summary>
    public partial float SpawnRadius { get; set; }

    /// <summary>Emitting. Stock's server has no key and no input for this — only C++ callers switched it.</summary>
    [NetField(Bits = 1)] public partial bool Emit { get; set; }
}

/// <summary>
/// What a dust trail tells the client.
///
/// <para>
/// <see cref="SmokeTrailNetState"/>'s twin — same fields, same defaults, same
/// keys — with ONE colour instead of two, because dust does not fade between a
/// start and an end. The colour is in 0-1 space for the same reason, and
/// <see cref="StopEmitTime"/> is an absolute time for the same reason.
/// </para>
///
/// <para>
/// The other difference is what is NOT here: <c>DT_DustTrail</c> has no
/// attachment index, alone among the four trails that have one. Stock's
/// <c>DustTrail</c> declares the member and its datadesc saves it, and no
/// <c>FollowEntity</c> was ever written to fill it.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvDusttrail)]
public partial class DustTrailNetState
{
    /// <summary>Puffs per second.</summary>
    public partial float SpawnRate { get; set; }

    /// <summary>The colour of a puff, in 0-1 space. Dust has one, not two.</summary>
    public partial Vector Color { get; set; }

    public partial float Opacity { get; set; }

    public partial float ParticleLifetime { get; set; }

    /// <summary>An ABSOLUTE time the trail stops emitting at. Zero means never.</summary>
    public partial float StopEmitTime { get; set; }

    public partial float MinSpeed { get; set; }
    public partial float MaxSpeed { get; set; }

    public partial float MinDirectedSpeed { get; set; }
    public partial float MaxDirectedSpeed { get; set; }

    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }

    public partial float SpawnRadius { get; set; }

    [NetField(Bits = 1)] public partial bool Emit { get; set; }
}

/// <summary>
/// What a rocket trail tells the client.
///
/// <para>
/// <see cref="SmokeTrailNetState"/> without the directed speeds, plus a flare
/// scale and a "damaged" flag that puts fire in the plume. Both colours are in
/// 0-1 space and <see cref="StopEmitTime"/> is an absolute time, for the
/// reasons that state gives.
/// </para>
///
/// <para>
/// The whole of this table is unreachable from a map. <c>RocketTrail</c>'s
/// datadesc is fifteen <c>DEFINE_FIELD</c>s and not one <c>DEFINE_KEYFIELD</c>,
/// and the class has no <c>KeyValue</c> override — so every value here is
/// whatever <c>RocketTrail::RocketTrail</c> set, always. Which makes the
/// constructor the entire entity, and makes it the one thing that had to be
/// carried across exactly.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvRockettrail)]
public partial class RocketTrailNetState
{
    public partial float SpawnRate { get; set; }

    /// <summary>The colour a puff starts at, in 0-1 space.</summary>
    public partial Vector StartColor { get; set; }

    /// <summary>The colour a puff ends at, in 0-1 space.</summary>
    public partial Vector EndColor { get; set; }

    public partial float Opacity { get; set; }
    public partial float ParticleLifetime { get; set; }

    /// <summary>An ABSOLUTE time the trail stops emitting at. Zero means never.</summary>
    public partial float StopEmitTime { get; set; }

    public partial float MinSpeed { get; set; }
    public partial float MaxSpeed { get; set; }
    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }
    public partial float SpawnRadius { get; set; }

    [NetField(Bits = 1)] public partial bool Emit { get; set; }

    /// <summary>
    /// An attachment index on a parent model, or −1 for the entity's own
    /// origin.
    ///
    /// MINUS ONE and not zero, alone among the four trails that carry this
    /// field: <c>C_RocketTrail</c>'s constructor sets −1 where the others set
    /// 0, and <c>GetAimEntOrigin</c> only consults an attachment when the index
    /// is greater than zero. So both mean "no attachment", and the value is
    /// stock's rather than tidied.
    /// </summary>
    public partial int Attachment { get; set; }

    /// <summary>Shot down: the plume gains fire. Only a C++ caller ever set it.</summary>
    [NetField(Bits = 1)] public partial bool Damaged { get; set; }

    /// <summary>How big the flare at the nozzle is. Stock's constructor default is 1.5.</summary>
    public partial float FlareScale { get; set; }
}

/// <summary>
/// What a spore trail tells the client.
///
/// <para>
/// Seven values, of which SIX are reported and not obeyed. The stock client
/// hardcodes its particle colour to white and never reads
/// <see cref="EndColor"/>; <c>C_SporeTrail::Start</c> inits its timed event at a
/// flat 64 a second and never reads <see cref="SpawnRate"/>; and the sizes, the
/// lifetime and the radius are constants inside <c>AddParticles</c> too. Only
/// <see cref="Emit"/> changes what is drawn.
/// </para>
///
/// <para>
/// They are all carried anyway, because they are all on the wire in stock and a
/// payload dump should say what stock says. Dropping them would make the
/// managed entity a different entity from the one a map was built against.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvSporetrail)]
public partial class SporeTrailNetState
{
    /// <summary>The colour a spore ends at, in 0-1 space — and never read. See the class remarks.</summary>
    public partial Vector EndColor { get; set; }

    /// <summary>Spores per second, as declared. The client uses a flat 64.</summary>
    public partial float SpawnRate { get; set; }

    public partial float ParticleLifetime { get; set; }
    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }
    public partial float SpawnRadius { get; set; }

    /// <summary>
    /// Emitting, and the only field that changes what is drawn.
    ///
    /// FALSE on a map-placed one, and that is stock: <c>SporeTrail</c>'s
    /// constructor is the only thing that ever set the other six and it does
    /// not touch this one, entity memory arrives zeroed, and the class has no
    /// key, no input and no <c>Spawn</c>. Only <c>SporeTrail::CreateSporeTrail</c>
    /// followed by a C++ caller writing the member could raise it.
    /// </summary>
    [NetField(Bits = 1)] public partial bool Emit { get; set; }
}

/// <summary>
/// What a spore explosion tells the client.
///
/// <para>
/// The same five numbers as <see cref="SporeTrailNetState"/> — and unlike that
/// class, the RATE here is obeyed: <c>C_SporeExplosion</c> arms its timed event
/// from it when the entity is built and re-arms it every time the entity is
/// switched on. The other four are still read by nothing;
/// <c>C_SporeExplosion::AddParticles</c> uses constants.
/// </para>
///
/// <para>
/// And unlike that class, this one can be switched. It is the only member of
/// the six with map inputs of its own, and both of them raise
/// <see cref="DontRemove"/> as well as changing <see cref="Emit"/>.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvSporeexplosion)]
public partial class SporeExplosionNetState
{
    /// <summary>Spores per second, and the one value of the five the client obeys.</summary>
    public partial float SpawnRate { get; set; }

    public partial float ParticleLifetime { get; set; }
    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }
    public partial float SpawnRadius { get; set; }

    /// <summary>
    /// Emitting. FALSE on spawn, which is deliberate in stock rather than
    /// forgotten: the constructor sets it true and <c>SporeExplosion::Spawn</c>
    /// then clears it, so an explosion waits for its Enable.
    /// </summary>
    [NetField(Bits = 1)] public partial bool Emit { get; set; }

    /// <summary>
    /// "A map is driving this one", which stops the client effect tidying
    /// itself away when it stops emitting.
    ///
    /// Raised by BOTH inputs and by nothing else — including Disable, because
    /// an explosion a map can switch back on must not have been collected in
    /// between.
    /// </summary>
    [NetField(Bits = 1)] public partial bool DontRemove { get; set; }
}
