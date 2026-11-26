using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A book of jumps for the tree's facts: it answers every stretch asked about with one plan (or
/// none), and keeps what it was asked.
/// </summary>
public sealed class TFJumpAdvice(JumpPlan? plan) : IJumpAdvisor
{
    /// <summary>The blast's cost the default plan carries.</summary>
    public const float Cost = 30f;

    public TFJumpAdvice()
        : this(Plan())
    {
    }

    /// <summary>A plan east along the field from x = 500, numbered <paramref name="id"/>, whose blast costs <paramref name="health"/>.</summary>
    public static JumpPlan Plan(int id = 7, float health = Cost)
        => new(new JumpRecipe(new Vector(500f, 256f, 0f), new Vector(1700f, 256f, 0f), 160f, 35f, 180f, -3, true), TFField.BlueFlag, Slide: 160f, Seconds: 2.4f,
               AirSeconds: 1.8f, WalkSeconds: 5.6f, Health: health, Robustness: 0.95f, Touch: new Vector(1690f, 256f, 0f), Apex: 200f) { Id = id };

    public List<JumpStretchAsk> Asks { get; } = [];

    /// <summary>Every ask of the hands in flight the tree wrote (round 2's S1), null for an ask taken back.</summary>
    public List<AirAsk?> Hands { get; } = [];

    public void AskHands(ActorId bot, AirAsk? ask) => Hands.Add(ask);

    /// <summary>The flight a fact says the bot's program has in the air (round 2's S1), or null: none.</summary>
    public AirFlight? Flight { get; set; }

    public AirFlight? Flying(ActorId bot) => Flight is { } f && f.Bot == bot ? f : null;

    public JumpPlan? Advise(in JumpStretchAsk ask)
    {
        Asks.Add(ask);
        return plan;
    }
}
