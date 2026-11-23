using System.Globalization;
using System.Text;
using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Descent.Client.Content;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Lamp;

//=============================================================================
// The miner's lamp on the client (plan_descent_logic HE10; mine plan M9, D7,
// E5: "the lamp's light on the client for every hero in sight, not only the
// local player"). Every client holds every hero's tf2_descent_hero entity, so
// every client lights every lamp it can see as a dynamic light of its own:
// no entity, no edict, one keyed light from the engine's pool a lit hero.
//=============================================================================

/// <summary>The lamp key: <c>tf2_descent_lamp_toggle</c>, which the server answers (TF2's second drop-item key; E still drops).</summary>
[KeyAction("tf2_descent_lamp_toggle", "Miner's lamp", Section = "Descent", Order = 1150, Keys = ["l"])]
public sealed class LampKey;

/// <summary>
/// The lamp's numbers as the client reads them from the rules' own lamp.json (embedded, byte for
/// byte, <see cref="DescentCatalog"/>): the cone, its colour and exponent, how far ahead of the
/// eye it starts, and the step a hero's look comes in.
/// </summary>
public sealed record LampNumbers(float Radius, float InnerAngle, float OuterAngle, int Red, int Green, int Blue, int Exponent,
                                 float Forward, float PitchStep)
{
    /// <summary>The lowest pitch the wire carries (straight up), as the rules' <c>LampRules.PitchFloor</c>.</summary>
    public const float PitchFloor = -90f;

    /// <summary>The pitch in degrees a wire step count stands for: the rules' <c>LampRules.PitchOf</c>.</summary>
    public float PitchOf(int steps) => Math.Clamp(PitchFloor + steps * PitchStep, PitchFloor, -PitchFloor);

    /// <summary>A hero's lamp radius from the wire's whole units: lamp.json's while the server has written none (0).</summary>
    public float RadiusOf(int wire) => wire <= 0 ? Radius : wire;

    /// <summary>A hero's lamp brightness from the wire's percent, as a multiplier: 1 while the server has written none (0).</summary>
    public float BrightnessOf(int wire) => wire <= 0 ? 1f : wire / 100f;

    /// <summary>
    /// The light's colour and exponent at <paramref name="brightness"/> times lamp.json's intensity,
    /// its hue kept: the engine lights by colour times two to the exponent, so the exponent takes
    /// the power of two and the colour the rest, every channel within 0..255. At 1 this is
    /// lamp.json's colour and exponent exactly.
    /// </summary>
    public (int Red, int Green, int Blue, int Exponent) Colour(float brightness)
    {
        int top = Math.Max(Red, Math.Max(Green, Blue));
        if (top <= 0 || !(brightness > 0f) || brightness == 1f)
            return (Red, Green, Blue, Exponent);
        int shift = (int)Math.Ceiling(Math.Log2(top * (double)brightness / 255.0));
        double scale = brightness / Math.Pow(2, shift);
        int Channel(int c) => (int)Math.Clamp(Math.Round(c * scale, MidpointRounding.AwayFromZero), 0, 255);
        return (Channel(Red), Channel(Green), Channel(Blue), Exponent + shift);
    }

    /// <summary>The numbers in lamp.json's bytes.</summary>
    public static LampNumbers Parse(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var light = root.GetProperty("light");
        var colour = light.GetProperty("colour");
        return new LampNumbers(
            light.GetProperty("radius").GetSingle(), light.GetProperty("innerAngle").GetSingle(), light.GetProperty("outerAngle").GetSingle(),
            colour.GetProperty("r").GetInt32(), colour.GetProperty("g").GetInt32(), colour.GetProperty("b").GetInt32(),
            light.GetProperty("exponent").GetInt32(), light.GetProperty("forward").GetSingle(), root.GetProperty("pitchStep").GetSingle());
    }
}

/// <summary>The lamp's numbers from the embedded lamp.json, read once.</summary>
[SideService(Side.Client)]
public sealed class LampContent(DescentCatalog catalog)
{
    /// <summary>The table's name: the rules' lamp.json.</summary>
    public const string Table = "lamp";

    private LampNumbers? _numbers;

    public LampNumbers Numbers => _numbers ??= LampNumbers.Parse(
        catalog.Bytes(Table) ?? throw new InvalidOperationException("the client embeds no lamp.json"));
}

/// <summary>
/// One hero as the lamp needs him: his player index, whether his lamp is lit on the wire, whether
/// this client can see him (alive, his player not dormant), his eye, and where he looks.
/// </summary>
public readonly record struct LampHero(int PlayerIndex, bool LampOn, bool Visible, Vector Eye, float Pitch, float Yaw)
{
    /// <summary>How far his lamp reaches, in units, from his wire (<see cref="HeroNetState.LampRadius"/>); 0 is lamp.json's.</summary>
    public float Radius { get; init; }

    /// <summary>How bright his lamp is, as a multiplier on lamp.json's light, from his wire (<see cref="HeroNetState.LampBrightness"/>); 0 is 1.</summary>
    public float Brightness { get; init; }
}

/// <summary>The heroes this client holds, as the lamp reads them each frame.</summary>
public interface ILampHeroes
{
    /// <summary>Every hero this client holds, this frame.</summary>
    IReadOnlyList<LampHero> Heroes();

    /// <summary>The local player's index (0 before the client is in a level).</summary>
    int LocalPlayer { get; }
}

/// <summary>
/// The heroes from their <c>tf2_descent_hero</c> entities and their players: a hero's lamp and
/// look are his entity's (<see cref="HeroNetState.LampOn"/>, <see cref="HeroNetState.LampPitch"/>);
/// his eye is his player's networked origin and view offset, his yaw his body's. The local hero's
/// eye and look are his own view's (<see cref="ViewReadout"/>), so his lamp follows his mouse and
/// not the server's echo of it.
/// </summary>
[SideService(Side.Client, As = [typeof(ILampHeroes)])]
public sealed class EngineLampHeroes(IClientStandalones standalones, IEngineHud hud, IEngineEntityRender render, ViewReadout view,
                                     LampContent content) : ILampHeroes
{
    private readonly List<LampHero> _heroes = [];

    public int LocalPlayer => hud.LocalPlayerIndex;

    public IReadOnlyList<LampHero> Heroes()
    {
        _heroes.Clear();
        int local = hud.LocalPlayerIndex;
        foreach (var entity in standalones.OfClass(HeroNetState.ClassName))
        {
            if (entity is not ClientHeroEntity { State.IsLive: true } hero || hero.State.OwnerIndex == 0)
                continue;
            var state = hero.State;
            int index = state.OwnerIndex;
            if (standalones.GetByIndex(index) is not ClientSsPlayer player)
            {
                _heroes.Add(new LampHero(index, state.LampOn, false, default, 0f, 0f));
                continue;
            }

            bool visible = player.State.Alive && !render.IsDormant(player.Handle);
            var numbers = content.Numbers;
            var lamp = index == local && view.Known
                ? new LampHero(index, state.LampOn, visible, view.Eye, view.Pitch, view.Yaw)
                : new LampHero(index, state.LampOn, visible, player.State.Origin + player.State.ViewOffset,
                               numbers.PitchOf(state.LampPitch), player.EyeAngles().Y);
            _heroes.Add(lamp with { Radius = numbers.RadiusOf(state.LampRadius), Brightness = numbers.BrightnessOf(state.LampBrightness) });
        }

        return _heroes;
    }
}

/// <summary>Where the lamp's lights go: the engine's dynamic light pool, keyed.</summary>
public interface ILampLightSink
{
    /// <summary>Lights, or moves, the light under its key.</summary>
    void Light(in DynamicLight light);

    /// <summary>Puts the light under <paramref name="key"/> out.</summary>
    void Out(int key);
}

/// <summary>The core's dynamic light registry (<see cref="IDynamicLights"/>), which knows which keys are lit.</summary>
[SideService(Side.Client, As = [typeof(ILampLightSink)])]
public sealed class EngineLampLightSink(IDynamicLights lights) : ILampLightSink
{
    public void Light(in DynamicLight light) => lights.Refresh(in light);

    public void Out(int key) => lights.Release(key);
}

/// <summary>The miner's lamps as this client knows them: for a HUD icon, party frames and the dev print.</summary>
public interface IHeroLamps
{
    /// <summary>Whether the hero in <paramref name="playerIndex"/> has his lamp lit on the wire.</summary>
    bool On(int playerIndex);

    /// <summary>Whether the local hero's lamp is lit on the wire.</summary>
    bool LocalOn { get; }

    /// <summary>The player indexes whose lamp this client lit last frame.</summary>
    IReadOnlyCollection<int> Lit { get; }
}

/// <summary>
/// Lights each hero's lamp once a client frame: one spot light, keyed by the hero's player index,
/// from his eye along his look, for every hero whose lamp is lit on the wire and whom this client
/// can see; none for a dark lamp, a dead hero or one out of the client's view (dormant), and a
/// light that was lit and is no longer is put out once. The engine's pool goes with the level, so
/// a level's end forgets what was lit.
///
/// <para><b>Range and brightness</b> are each hero's own, from his wire. Range is the light's
/// radius, for the world and models alike: a longer lamp shows a robot further off. Brightness is
/// the light's intensity (its colour times two to its exponent, the hue kept:
/// <see cref="LampNumbers.Colour"/>) on the WORLD only. The engine lights a wall with a dlight as a
/// point light whose cone it ignores (gl_lightmap.cpp's AddSingleDynamicLight tests only that the
/// surface faces the light), and a model as a spot whose intensity falls with the square of the
/// distance (lightcache.cpp's WorldLightFromDynamicLight); colour and exponent scale both alike, so
/// one brighter light brightens the view model, a few units from the lamp, as much as the wall,
/// and the inner cone brightens no wall at all. So a lamp brighter (or dimmer) than lamp.json's is
/// two lights: the world's at his brightness, flagged to light no model, and the models' (robots,
/// view models) at lamp.json's intensity under <see cref="ModelKeyOf"/>, flagged to light no
/// world surface. That half is a dlight, not an elight: static props (a gravestone, furniture)
/// take only dlights (lightcache.cpp's AddDLightsForStaticProps), and the first live try with an
/// elight left them black. A lamp at lamp.json's brightness stays one light.</para>
/// </summary>
[SideService(Side.Client, As = [typeof(IHeroLamps)])]
public sealed class HeroLampLights(ILampHeroes heroes, ILampLightSink sink, LampContent content)
    : IHeroLamps, IClientFrameThink, IClientLevelHook, IOrderedHook
{
    /// <summary>The first key of the lamps' lights: clear of entity indexes, which light_dynamic keys by.</summary>
    public const int KeyBase = 0x0800_0000;

    /// <summary>After the robots' poses (500), beside the mod's other frame members.</summary>
    public int HookOrder => 1300;

    /// <summary>The first key of the models' half of a brighter lamp: clear of the world lights' keys.</summary>
    public const int ModelKeyBase = KeyBase + 0x100;

    private readonly HashSet<int> _lit = [];
    private readonly HashSet<int> _now = [];
    private readonly HashSet<int> _litModels = [];
    private readonly HashSet<int> _nowModels = [];
    private readonly HashSet<int> _on = [];

    /// <summary>The key of the hero in <paramref name="playerIndex"/>'s lamp.</summary>
    public static int KeyOf(int playerIndex) => KeyBase + playerIndex;

    /// <summary>The key of the models' light of the hero in <paramref name="playerIndex"/>'s lamp when it is not lamp.json's brightness.</summary>
    public static int ModelKeyOf(int playerIndex) => ModelKeyBase + playerIndex;

    /// <summary>Whether the hero's lamp is two lights: its brightness is not lamp.json's.</summary>
    public static bool Split(LampHero hero) => hero.Brightness > 0f && hero.Brightness != 1f;

    /// <summary>Lights made since the client started, and put out.</summary>
    public long Lights { get; private set; }

    public long Outs { get; private set; }

    public IReadOnlyCollection<int> Lit => _lit;

    public bool On(int playerIndex) => _on.Contains(playerIndex);

    public bool LocalOn => On(heroes.LocalPlayer);

    public void ClientThink(float frameTime)
    {
        var numbers = content.Numbers;
        _now.Clear();
        _nowModels.Clear();
        _on.Clear();
        foreach (var hero in heroes.Heroes())
        {
            if (hero.LampOn)
                _on.Add(hero.PlayerIndex);
            if (!hero.LampOn || !hero.Visible)
                continue;
            var light = LightOf(hero, numbers);
            sink.Light(in light);
            Lights++;
            _now.Add(hero.PlayerIndex);
            if (!Split(hero))
                continue;
            var models = ModelLightOf(hero, numbers);
            sink.Light(in models);
            Lights++;
            _nowModels.Add(hero.PlayerIndex);
        }

        PutOut(_lit, _now, KeyOf);
        PutOut(_litModels, _nowModels, ModelKeyOf);
    }

    /// <summary>Puts out each light lit last frame and not this one, once; this frame's become the lit.</summary>
    private void PutOut(HashSet<int> lit, HashSet<int> now, Func<int, int> key)
    {
        foreach (int gone in lit)
        {
            if (now.Contains(gone))
                continue;
            sink.Out(key(gone));
            Outs++;
        }

        lit.Clear();
        lit.UnionWith(now);
    }

    /// <summary>
    /// The hero's lamp as a dynamic light: a cone from just ahead of his eye along his look, his
    /// radius. At lamp.json's brightness it lights the world and models; brighter or dimmer it
    /// lights only the world, at his brightness (<see cref="ModelLightOf"/> lights the models).
    /// </summary>
    public static DynamicLight LightOf(LampHero hero, LampNumbers n)
    {
        var light = Cone(hero, n);
        light.Key = KeyOf(hero.PlayerIndex);
        if (Split(hero))
        {
            (light.Red, light.Green, light.Blue, light.Exponent) = n.Colour(hero.Brightness);
            light.Flags = (int)DynamicLightFlags.NoModelIllumination;
        }

        return light;
    }

    /// <summary>The models' half of a lamp that is not lamp.json's brightness: a dlight that lights no world surface, at lamp.json's colour, his radius and cone.</summary>
    public static DynamicLight ModelLightOf(LampHero hero, LampNumbers n)
    {
        var light = Cone(hero, n);
        light.Key = ModelKeyOf(hero.PlayerIndex);
        light.Flags = (int)DynamicLightFlags.NoWorldIllumination;
        return light;
    }

    /// <summary>The lamp's cone at lamp.json's colour: from just ahead of his eye along his look, his radius.</summary>
    private static DynamicLight Cone(LampHero hero, LampNumbers n)
    {
        Vector forward = MoveVec.AngleForward(new Vector(hero.Pitch, hero.Yaw, 0f));
        Vector origin = hero.Eye + forward * n.Forward;
        return new DynamicLight
        {
            OriginX = origin.X, OriginY = origin.Y, OriginZ = origin.Z,
            DirectionX = forward.X, DirectionY = forward.Y, DirectionZ = forward.Z,
            Radius = hero.Radius > 0f ? hero.Radius : n.Radius,
            Red = n.Red, Green = n.Green, Blue = n.Blue, Exponent = n.Exponent,
            InnerAngle = n.InnerAngle,
            OuterAngle = n.OuterAngle,
            LiveFor = DynamicLights.RefreshInterval,
        };
    }

    /// <summary>The level is going, and the engine's pool with it: nothing is lit any more.</summary>
    public void OnLevelShutdown()
    {
        _lit.Clear();
        _now.Clear();
        _litModels.Clear();
        _nowModels.Clear();
        _on.Clear();
    }

    /// <summary>Every hero's lamp as this client holds it, as lines (<c>tf2_descent_lamps_cl</c>).</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        var list = heroes.Heroes();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Lamp] client: local player {heroes.LocalPlayer}, {list.Count} hero(es), {_lit.Count} lit, {Lights} light(s) made, {Outs} put out\n");
        foreach (var h in list.OrderBy(h => h.PlayerIndex))
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Lamp]   player {h.PlayerIndex}: wire {(h.LampOn ? "lit" : "dark")}, {(h.Visible ? "seen" : "unseen")}, " +
                $"{(_lit.Contains(h.PlayerIndex) ? "lit here" : "not lit here")}{(_litModels.Contains(h.PlayerIndex) ? " (world and models apart)" : "")}, " +
                $"radius {(h.Radius > 0f ? h.Radius : content.Numbers.Radius):0} brightness x{(h.Brightness > 0f ? h.Brightness : 1f):0.##}, " +
                $"eye ({h.Eye.X:0} {h.Eye.Y:0} {h.Eye.Z:0}) look {h.Pitch:0.#} {h.Yaw:0.#}\n");
        return sb.ToString();
    }
}

/// <summary><c>tf2_descent_lamps_cl</c>: every hero's lamp as this client holds and lights it.</summary>
[ConCommand("tf2_descent_lamps_cl", Help = "tf2_descent_lamps_cl: every hero's miner's lamp as this client holds it: the wire, seen, lit here.")]
public sealed class ClientLampsCommand(HeroLampLights lamps, ILog log)
{
    public void Execute() => log.Msg(lamps.Report());
}
