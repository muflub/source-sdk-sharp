extern alias client;

using SourceSharp.Abi;

using TF2.Descent.Rules;
using TF2.Descent.Rules.World;
using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using HeroLampLights = client::TF2.Descent.Client.Lamp.HeroLampLights;
using ILampHeroes = client::TF2.Descent.Client.Lamp.ILampHeroes;
using ILampLightSink = client::TF2.Descent.Client.Lamp.ILampLightSink;
using LampContent = client::TF2.Descent.Client.Lamp.LampContent;
using LampHero = client::TF2.Descent.Client.Lamp.LampHero;
using LampKey = client::TF2.Descent.Client.Lamp.LampKey;
using ClientKeyActions = client::SourceSharp.Client.Input.KeyActions;

namespace TF2.Descent.Tests.World;

/// <summary>
/// The miner's lamp on the client (HE10, mine plan E5): one light a hero this client can see with
/// his lamp lit on the wire, none for a dark lamp, a dead hero or one out of view; the light's
/// numbers are lamp.json's; a light that goes is put out once; a level's end forgets them all.
/// </summary>
public sealed class HeroLampLightsTests
{
    private sealed class HeroList : ILampHeroes
    {
        public List<LampHero> List { get; } = [];
        public int LocalPlayer { get; set; } = 1;
        public IReadOnlyList<LampHero> Heroes() => List;
    }

    /// <summary>The fake effects seam: counts the lights asked for and put out.</summary>
    private sealed class Sink : ILampLightSink
    {
        public List<DynamicLight> Lit { get; } = [];
        public List<int> Out { get; } = [];
        public void Light(in DynamicLight light) => Lit.Add(light);
        void ILampLightSink.Out(int key) => Out.Add(key);
    }

    private static readonly TF2DescentRules Rules = new();

    private readonly HeroList _heroes = new();
    private readonly Sink _sink = new();
    private readonly LampContent _content = new(new Catalog());
    private readonly HeroLampLights _lamps;

    public HeroLampLightsTests() => _lamps = new HeroLampLights(_heroes, _sink, _content);

    private static LampHero Hero(int index, bool on = true, bool visible = true, float pitch = 0f, float yaw = 0f) =>
        new(index, on, visible, new Vector(100 * index, 0, 64), pitch, yaw);

    [Fact]
    public void Every_visible_hero_with_his_lamp_lit_gets_one_light()
    {
        _heroes.List.AddRange([Hero(1), Hero(2), Hero(3)]);

        _lamps.ClientThink(0.016f);

        Assert.Equal(3, _sink.Lit.Count);
        Assert.Equal(new[] { 1, 2, 3 }.Select(HeroLampLights.KeyOf), _sink.Lit.Select(l => l.Key));
    }

    [Fact]
    public void A_dark_lamp_a_dead_or_dormant_hero_gets_no_light()
    {
        _heroes.List.AddRange([Hero(1), Hero(2, on: false), Hero(3, visible: false)]);

        _lamps.ClientThink(0.016f);

        var lit = Assert.Single(_sink.Lit);
        Assert.Equal(HeroLampLights.KeyOf(1), lit.Key);
        Assert.True(_lamps.On(1));
        Assert.False(_lamps.On(2));
        Assert.True(_lamps.On(3));      // lit on the wire, but this client cannot see him
    }

    [Fact]
    public void The_light_is_lamp_jsons_cone_from_just_ahead_of_the_eye_along_the_look()
    {
        var n = Rules.Content.Lamp.Light;
        _heroes.List.Add(Hero(2, yaw: 90f));

        _lamps.ClientThink(0.016f);

        var light = Assert.Single(_sink.Lit);
        Assert.Equal((float)n.Radius, light.Radius);
        Assert.Equal((float)n.InnerAngle, light.InnerAngle);
        Assert.Equal((float)n.OuterAngle, light.OuterAngle);
        Assert.Equal((n.Red, n.Green, n.Blue, n.Exponent), (light.Red, light.Green, light.Blue, light.Exponent));
        Assert.Equal(0f, light.DirectionX, 4);
        Assert.Equal(1f, light.DirectionY, 4);              // yaw 90 looks along +y
        Assert.Equal(200f, light.OriginX, 3);
        Assert.Equal((float)n.Forward, light.OriginY, 3);   // just ahead of the eye
        Assert.True(light.LiveFor > 0f);
    }

    [Fact]
    public void A_lamp_that_goes_dark_is_put_out_once()
    {
        _heroes.List.Add(Hero(2));
        _lamps.ClientThink(0.016f);

        _heroes.List[0] = Hero(2, on: false);
        _lamps.ClientThink(0.016f);
        _lamps.ClientThink(0.016f);

        Assert.Equal([HeroLampLights.KeyOf(2)], _sink.Out);
        Assert.Empty(_lamps.Lit);
    }

    [Fact]
    public void A_hero_who_leaves_the_clients_view_has_his_light_put_out()
    {
        _heroes.List.Add(Hero(2));
        _lamps.ClientThink(0.016f);

        _heroes.List.Clear();
        _lamps.ClientThink(0.016f);

        Assert.Equal([HeroLampLights.KeyOf(2)], _sink.Out);
    }

    [Fact]
    public void A_levels_end_forgets_what_was_lit()
    {
        _heroes.List.Add(Hero(2));
        _lamps.ClientThink(0.016f);

        _lamps.OnLevelShutdown();
        _heroes.List.Clear();
        _lamps.ClientThink(0.016f);

        Assert.Empty(_sink.Out);   // the engine's pool went with the level: nothing to put out
        Assert.Empty(_lamps.Lit);
    }

    [Fact]
    public void The_local_heroes_lamp_state_is_his_wires()
    {
        _heroes.LocalPlayer = 2;
        _heroes.List.AddRange([Hero(1, on: false), Hero(2)]);

        _lamps.ClientThink(0.016f);

        Assert.True(_lamps.LocalOn);
    }

    [Fact]
    public void The_client_reads_the_rules_lamp_numbers_and_their_pitch_steps()
    {
        var rules = Rules.Content.Lamp;
        var n = _content.Numbers;

        Assert.Equal((float)rules.Light.Radius, n.Radius);
        Assert.Equal((float)rules.PitchStep, n.PitchStep);
        foreach (double pitch in new[] { -90.0, -33.0, 0, 45.0, 90 })
            Assert.Equal((float)LampRules.PitchOf(rules, LampRules.PitchSteps(rules, pitch)), n.PitchOf(LampRules.PitchSteps(rules, pitch)), 4);
    }

    /// <summary>The light's linear intensity per channel as the engine reads it: colour times two to the exponent.</summary>
    private static double[] Intensity(DynamicLight l) =>
        [l.Red * Math.Pow(2, l.Exponent), l.Green * Math.Pow(2, l.Exponent), l.Blue * Math.Pow(2, l.Exponent)];

    [Fact]
    public void Each_heroes_lamp_reaches_as_far_as_his_wire_says()
    {
        _heroes.List.AddRange([Hero(1) with { Radius = 900f }, Hero(2)]);

        _lamps.ClientThink(0.016f);

        Assert.Equal([900f, (float)Rules.Content.Lamp.Light.Radius], _sink.Lit.Select(l => l.Radius));   // 2 has none written: lamp.json's
    }

    [Fact]
    public void A_lamp_at_lamp_jsons_brightness_is_one_light_for_the_world_and_models()
    {
        _heroes.List.Add(Hero(2) with { Brightness = 1f });

        _lamps.ClientThink(0.016f);

        var light = Assert.Single(_sink.Lit);
        Assert.Equal((0, 0), (light.Flags, light.IsElight));
    }

    [Fact]
    public void A_brighter_lamp_lights_the_world_brighter_and_models_at_lamp_jsons_intensity()
    {
        var n = Rules.Content.Lamp.Light;
        _heroes.List.Add(Hero(2) with { Brightness = 1.6f, Radius = 800f });

        _lamps.ClientThink(0.016f);

        Assert.Equal(2, _sink.Lit.Count);
        var world = Assert.Single(_sink.Lit, l => l.Key == HeroLampLights.KeyOf(2));
        var models = Assert.Single(_sink.Lit, l => l.Key == HeroLampLights.ModelKeyOf(2));
        Assert.Equal((int)DynamicLightFlags.NoModelIllumination, world.Flags);
        Assert.Equal((int)DynamicLightFlags.NoWorldIllumination, models.Flags);
        Assert.Equal((0, 0), (world.IsElight, models.IsElight));   // both dlights: static props take no elight
        Assert.Equal((n.Red, n.Green, n.Blue, n.Exponent), (models.Red, models.Green, models.Blue, models.Exponent));
        double[] baseI = Intensity(models), brightI = Intensity(world);
        for (int c = 0; c < 3; c++)
            Assert.Equal(1.6, brightI[c] / baseI[c], 1);   // every channel 1.6 times: brighter, the hue kept
        Assert.Equal((800f, 800f), (world.Radius, models.Radius));
        Assert.Equal((world.OriginX, world.InnerAngle, world.OuterAngle), (models.OriginX, models.InnerAngle, models.OuterAngle));
    }

    [Fact]
    public void A_lamp_back_at_lamp_jsons_brightness_puts_its_models_light_out_once()
    {
        _heroes.List.Add(Hero(2) with { Brightness = 1.5f });
        _lamps.ClientThink(0.016f);

        _heroes.List[0] = Hero(2) with { Brightness = 1f };
        _lamps.ClientThink(0.016f);
        _lamps.ClientThink(0.016f);

        Assert.Equal([HeroLampLights.ModelKeyOf(2)], _sink.Out);
        Assert.Equal([2], _lamps.Lit);   // the lamp itself stays lit
    }

    [Fact]
    public void A_brighter_lamp_that_goes_dark_puts_out_both_its_lights()
    {
        _heroes.List.Add(Hero(2) with { Brightness = 2f });
        _lamps.ClientThink(0.016f);

        _heroes.List[0] = Hero(2, on: false) with { Brightness = 2f };
        _lamps.ClientThink(0.016f);

        Assert.Equal([HeroLampLights.KeyOf(2), HeroLampLights.ModelKeyOf(2)], _sink.Out.Order());
    }

    [Fact]
    public void The_colour_at_lamp_jsons_brightness_is_lamp_jsons()
    {
        var n = Rules.Content.Lamp.Light;

        Assert.Equal((n.Red, n.Green, n.Blue, n.Exponent), _content.Numbers.Colour(1f));
    }

    [Fact]
    public void Twice_the_brightness_is_one_step_of_the_exponent_with_the_colour_kept()
    {
        var n = Rules.Content.Lamp.Light;

        Assert.Equal((n.Red, n.Green, n.Blue, n.Exponent + 1), _content.Numbers.Colour(2f));
        Assert.Equal((n.Red, n.Green, n.Blue, n.Exponent - 1), _content.Numbers.Colour(0.5f));
    }

    [Fact]
    public void A_wire_with_nothing_written_is_lamp_jsons_lamp()
    {
        var n = _content.Numbers;

        Assert.Equal((n.Radius, 1f), (n.RadiusOf(0), n.BrightnessOf(0)));
        Assert.Equal((864f, 1.3f), (n.RadiusOf(864), n.BrightnessOf(130)));
    }

    [Fact]
    public void L_lights_the_lamp_and_takes_only_tf2s_second_drop_item_key()
    {
        var descent = typeof(LampKey).Assembly;
        var tf2 = typeof(client::TF2.Client.MoveForwardKey).Assembly;
        var actions = ClientKeyActions.Of(tf2.GetTypes().Concat(descent.GetTypes()), typeof(ClientKeyActions).Assembly);
        var final = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, command) in actions.DefaultBinds)
            final[key] = command;

        Assert.Equal("tf2_descent_lamp_toggle", final["l"]);
        Assert.Contains(actions.DefaultBinds, b => b.Key == "e" && b.Command == "dropitem");   // dropping keeps its first key
        Assert.Single(final, kv => kv.Value == "tf2_descent_lamp_toggle");
    }
}
