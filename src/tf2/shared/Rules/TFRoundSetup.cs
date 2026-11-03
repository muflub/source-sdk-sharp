namespace TF2.Shared.Rules;

/// <summary>
/// Stock's setup gate as the medigun's charge reads it:
/// <c>TFGameRules()-&gt;InSetup() &amp;&amp; GetActiveRoundTimer()</c>
/// (teamplayroundbased_gamerules.h:205, asked at tf_weapon_medigun.cpp:1288,
/// 1321 and 1347). While it holds, the meter keeps building on a full patient
/// and at triple rate. Each side answers it from its own round words — the
/// server's gamerules flag and elected timer, the client's gamerules mirror
/// and timer — so the predicted build reads the same gate the server's does.
/// </summary>
public interface ITFRoundSetup
{
    /// <summary>The setup period with a round timer live.</summary>
    bool InSetupWithRoundTimer { get; }
}
