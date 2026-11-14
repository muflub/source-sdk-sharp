extern alias client;

using Xunit;

using Abi = client::SourceSharp.Abi;
using ClientCond = client::TF2.Shared.Conditions.TFConditions;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientVision = client::TF2.Client.PyroVision;
using Econ = client::TF2.Shared.Econ;
using Inputs = client::SourceSharp.Client.Interop.PostProcessInputs;
using Ops = client::SourceSharp.Client.Render.IPostOps;
using OwnersContract = client::TF2.Shared.ITFWeaponOwners;
using PostPrimitive = client::SourceSharp.Client.Interop.PostPrimitive;
using PostTexture = client::SourceSharp.Client.Interop.PostTexture;
using PostVar = client::SourceSharp.Client.Interop.PostVar;
using VisionConVars = client::TF2.Client.PyroVisionConVars;
using VisionContext = client::SourceSharp.Client.Render.PostVisionContext;

namespace TF2.Tests.Client;

/// <summary>
/// The vision filter's gate, stock's <c>C_TFPlayer::GetVisionFilterFlags</c>
/// (c_tf_player.cpp:8028-8105): the held item's <c>vision_opt_in_flags</c>
/// attribute (value_is_or) ors its bits onto the answer even when the convar
/// half holds none — what lets the Purea items (the Lollichop, the Rainblower)
/// switch the pyrovision on for their holder. The frame the gate opens draws
/// the vignette border (pyro_vignette = 1 keeps the border only).
/// </summary>
public sealed class PyroVisionOptInFacts
{
    [Fact]
    public void A_held_opt_in_item_turns_the_pyrovision_on_even_with_the_convar_clear()
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", 7);
        var convars = DeclaredClientConVars.Holder<VisionConVars>(
            ("pyro_vignette", "1"), ("localplayer_visionflags", "0"));
        var owners = new HeldOwners();
        owners.Give("1");
        var vision = new ClientVision(new EvenRandom(), convars, engine, owners);
        var ops = new RecordingOps();

        vision.AtVisionPoint(new VisionContext { Inputs = new Inputs { SupportsPixelShaders20 = 1 } }, ops);

        Assert.Contains("dev/pyro_vignette_border", ops.RectMaterials);
    }

    /// <summary>
    /// The same clear convar with nothing held draws nothing: the stimulus
    /// reaching the gate above is the attribute, not the harness. Same rig,
    /// empty item — the non-zero arm's control.
    /// </summary>
    [Fact]
    public void Without_an_opt_in_item_a_clear_convar_draws_nothing()
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", 7);
        var convars = DeclaredClientConVars.Holder<VisionConVars>(
            ("pyro_vignette", "1"), ("localplayer_visionflags", "0"));
        var owners = new HeldOwners();
        var vision = new ClientVision(new EvenRandom(), convars, engine, owners);
        var ops = new RecordingOps();

        vision.AtVisionPoint(new VisionContext { Inputs = new Inputs { SupportsPixelShaders20 = 1 } }, ops);

        Assert.Empty(ops.RectMaterials);
    }

    // ---------------------------------------------------------------------

    private sealed class HeldOwners : OwnersContract
    {
        private int _nextDef = 406;

        /// <summary>The active weapon's item, or null.</summary>
        public Econ.IAttributeProvider? Item { get; private set; }

        /// <summary>Hands the held item a value_is_or vision_opt_in_flags attribute.</summary>
        public void Give(string value)
        {
            var container = new Econ.AttributeContainer(null, Econ.AttributeProviderType.Generic);
            container.Set(Econ.AttributeValue.Parse(
                new Econ.AttributeDefinition(_nextDef++, "vision opt in flags", ClientVision.VisionOptInAttribute,
                                             Econ.AttributeDescriptionFormat.Or, Econ.AttributeEffectType.Positive,
                                             false, false, Econ.AttributeValueType.Default, Econ.MergedKeys.Block("x")),
                value, multiLine: true));
            Item = container;
        }

        public Econ.IAttributeProvider? ActiveItemOf(int playerIndex) => Item;

        public ClientCond? ConditionsOf(int playerIndex) => null;

        public float CritMult(int playerIndex) => 1f;

        public bool WinsCrits(int playerIndex) => false;

        public client::TF2.Shared.TFClass ClassOf(int playerIndex) => client::TF2.Shared.TFClass.Undefined;

        public Econ.IAttributeProvider? ItemOf(int playerIndex, string weaponName) => Item;

        public Econ.IAttributeProvider? ManagerOf(int playerIndex) => null;

        public bool OffGround(int playerIndex) => false;

        public bool EyesUnderwater(int playerIndex) => false;

        public bool DuckingOf(int playerIndex) => false;
    }

    /// <summary>The two draws the vignette makes; every other op is a no-op here.</summary>
    private sealed class RecordingOps : Ops
    {
        private int _nextId;
        private readonly Dictionary<int, string> _names = new();

        /// <summary>The material names of the DrawRect calls, in order.</summary>
        public List<string> RectMaterials { get; } = new();

        public int Material(string name)
        {
            _names[++_nextId] = name;
            return _nextId;
        }

        public void DrawRect(int material, int x, int y, int w, int h, float srcX0, float srcY0, float srcX1,
                             float srcY1, int srcWidth, int srcHeight, bool worldRenderable = false,
                             int diceX = 1, int diceY = 1)
            => RectMaterials.Add(_names.TryGetValue(material, out string? name) ? name : "?");

        public void CopyScreenEffect(int x, int y, int w, int h, bool destFullScreen) { }
        public void SetRenderTarget(PostTexture texture) { }
        public void SetRenderTargetAndViewport(PostTexture texture) { }
        public void PushRenderTargetAndViewport() { }
        public void PopRenderTargetAndViewport() { }
        public void SetVarFloat(int material, PostVar var, float value) { }
        public void Scissor(int x, int y, int w, int h, bool enable) { }
        public void ClearColor3ub(int r, int g, int b) { }
        public void Viewport(int x, int y, int w, int h) { }
        public void ClearBuffers(bool color, bool depth) { }
        public void ClearColor4ub(int r, int g, int b, int a) { }
        public void PushRenderTarget(PostTexture texture) { }
        public void MeshBegin(int material, PostPrimitive primitive, int count, bool identityMatrices,
                              bool worldRenderable, bool unlessDisabled) { }
        public void MeshVertex(float x, float y, float z, float u, float v,
                               (float R, float G, float B, float A)? color = null) { }
        public void MeshVertex(float x, float y, float z, float u, float v,
                               (float R, float G, float B, float A)? color,
                               SourceSharp.Abi.Vector tangentS, SourceSharp.Abi.Vector tangentT,
                               SourceSharp.Abi.Vector normal) { }
        public void MeshEnd() { }
    }

    private sealed class EvenRandom : Abi.IUniformRandom
    {
        public float RandomFloat(float min, float max) => (min + max) / 2f;

        public int RandomInt(int min, int max) => min;
    }
}
