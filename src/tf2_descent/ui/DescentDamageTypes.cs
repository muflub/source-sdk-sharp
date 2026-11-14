namespace TF2.Descent.Client.Ui;

/// <summary>The four damage types heroes and robots share (R25): fire, blast, bullet, shock.
/// Copy tokens the screens draw: a static class of constants, so it stays beside the
/// screens rather than in TF2.Descent.Client.Interface, which holds data only.</summary>
public static class DescentDamageTypes
{
    public const string Fire = "#Descent_Type_Fire";
    public const string Blast = "#Descent_Type_Blast";
    public const string Bullet = "#Descent_Type_Bullet";
    public const string Shock = "#Descent_Type_Shock";
}
