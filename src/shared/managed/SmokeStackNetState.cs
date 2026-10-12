using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a smokestack tells the client: the shape of the column, the wind that
/// bends it, the material it is drawn with, and the two lights that make it read
/// as lit from below.
///
/// <para>
/// The widest table in the particle family — nineteen values in stock, of which
/// six are the two embedded light blocks. Those six are the interesting ones:
/// they are not a mapper's keys at all. They are copied out of the
/// <c>env_particlelight</c> entities that name this stack, once, when the level
/// finishes loading. See <see cref="Server.EnvSmokeStack"/>.
/// </para>
///
/// <para>
/// The stack's own base colour is NOT here, and that is stock: <c>C_SmokeStack</c>
/// reads it off <c>m_clrRender</c>, which arrives with every other entity's
/// render colour rather than on <c>DT_SmokeStack</c>. Which is why the base
/// colour is a parameter on the client call and not a field on this class.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvSmokestack)]
public partial class SmokeStackNetState
{
    /// <summary>How fast a puff drifts sideways as it rises. The width of the column.</summary>
    public partial float SpreadSpeed { get; set; }

    /// <summary>How fast a puff rises. With the length, this is what sets a puff's lifetime.</summary>
    public partial float Speed { get; set; }

    public partial float StartSize { get; set; }
    public partial float EndSize { get; set; }

    /// <summary>
    /// Puffs per second — read ONCE, when the client builds the emitter.
    ///
    /// Stock declares this a <c>DEFINE_INPUT</c>, and the input does reach the
    /// wire, but <c>C_SmokeStack</c> arms its timed event in <c>Start</c> and
    /// never re-arms it. So a map driving Rate changes what the entity reports
    /// and not what it emits, in stock as much as here.
    /// </summary>
    public partial float Rate { get; set; }

    /// <summary>How far the column reaches. The client divides the speed by this to get a fade rate.</summary>
    public partial float JetLength { get; set; }

    /// <summary>How fast a puff turns as it rises.</summary>
    public partial float RollSpeed { get; set; }

    /// <summary>How wide the mouth of the stack is — the radius a puff may first appear within.</summary>
    public partial float BaseSpread { get; set; }

    /// <summary>Degrees a second the whole column rotates about its own axis.</summary>
    public partial float Twist { get; set; }

    /// <summary>Emitting. Driven by TurnOn, TurnOff and Toggle.</summary>
    [NetField(Bits = 1)] public partial bool Emit { get; set; }

    /// <summary>
    /// A MODEL index, not a material handle.
    ///
    /// Stock precaches a <c>.vmt</c> as a MODEL and networks its index, and the
    /// client asks <c>modelinfo</c> for the name back and strips the extension to
    /// get a material. Minus one means the server never resolved one, which is
    /// why the send prop is <c>SendPropIntWithMinusOneFlag</c> rather than a
    /// plain int.
    /// </summary>
    public partial int MaterialModel { get; set; }

    /// <summary>Which way and how hard the wind blows a puff, as an acceleration.</summary>
    public partial Vector Wind { get; set; }

    /// <summary>Where the ambient light is, in world space.</summary>
    public partial Vector AmbientPos { get; set; }

    /// <summary>The ambient light's colour, in 0-1 space. The server divides the light's 0-255 key.</summary>
    public partial Vector AmbientColor { get; set; }

    /// <summary>How bright the ambient light is. Divided by the SQUARE of the distance to each puff.</summary>
    public partial float AmbientIntensity { get; set; }

    /// <summary>Where the directional light is, in world space.</summary>
    public partial Vector DirPos { get; set; }

    /// <summary>The directional light's colour, in 0-1 space.</summary>
    public partial Vector DirColor { get; set; }

    /// <summary>How bright the directional light is.</summary>
    public partial float DirIntensity { get; set; }

    /// <summary>
    /// The mapper's render colour, which stock reads off the entity rather than
    /// off DT_SmokeStack.
    ///
    /// Carried here because a managed entity is where the key landed and the
    /// client object this feeds has no keyvalues at all — the same move
    /// <see cref="ISteamJetNetState.Red"/> makes. Stock's default is BLACK at
    /// full alpha, which is what makes an unlit stack read as smoke rather than
    /// as a white cloud.
    /// </summary>
    [NetField(Bits = 8)] public partial byte Red { get; set; }

    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }

    /// <summary>How solid the column is overall. Stock's default is 255.</summary>
    [NetField(Bits = 8)] public partial byte Alpha { get; set; }
}
