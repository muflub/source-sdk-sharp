namespace TF2.Descent.Client.Ui;

// ---- robot health bars (plan_descent_logic UI6; plan_tf2_rpg R25, ruling L6) -----

/// <summary>
/// One robot's bar over its head, as the local hero may see it. What anyone may see is always
/// here (the name, the life as a fraction, champion, the statuses); what the bestiary gates is
/// here only once the server has revealed it to him: <see cref="Kills"/> -1, <see cref="LifeMax"/>
/// 0, <see cref="Style"/> "" and no resistance rows say "not yet".
/// </summary>
public readonly record struct DescentRobotBar(
    float X, float Y,          // the bar's foot over the robot's head, as fractions of the screen (0..1)
    string Name,               // ui.Data: the template's name ("Soldier Robot")
    float LifeFraction,        // 0..1, the body's own bar
    int Kills,                 // the hero's kills of its template; -1 while he has none
    int Life, int LifeMax,     // the numbers; LifeMax 0 while hidden
    bool Champion,
    string Style,              // ui.Data: its temperament style ("Reckless Deadeye"); "" while hidden
    bool Immune,               // his last hit on it drew IMMUNE, a moment ago
    string ImmuneToken);       // that hit's damage type, a #Descent_Type_* token; "" for none

/// <summary>The bars to draw this frame: index-and-count shapes, so reading them each frame allocates nothing.</summary>
public interface IDescentRobotBars
{
    int BarCount { get; }
    DescentRobotBar Bar(int i);

    /// <summary>Resistance rows of bar <paramref name="bar"/>: 0 while the bestiary hides them.</summary>
    int ResistCount(int bar);
    string ResistToken(int bar, int r);           // #Descent_Type_Fire ...
    int ResistPercent(int bar, int r);            // negative: a weakness
    bool ResistImmune(int bar, int r);            // the champion's immunity

    /// <summary>Champion affix names (ui.Data): none while the bestiary hides them.</summary>
    int AffixCount(int bar);
    string AffixName(int bar, int a);

    /// <summary>The statuses on the robot now (anyone may see them): #Descent_Status_* tokens.</summary>
    int StatusCount(int bar);
    string StatusToken(int bar, int s);
}
