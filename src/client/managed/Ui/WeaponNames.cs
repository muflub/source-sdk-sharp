namespace SourceSharp.Client.Vgui;

/// <summary>
/// A weapon's name as the HUD shows it (plan_client_ui U3): its script's
/// <c>printname</c> (scripts/weapon_*.txt, a <c>#token</c>), localised; else the
/// class name without its <c>weapon_</c> prefix. Each class is resolved once.
/// </summary>
public sealed class WeaponNames(Func<string, string?> printName, Func<string, string?> localise)
{
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public string Display(string className)
    {
        if (_names.TryGetValue(className, out var known))
            return known;

        string? token = printName(className);
        string? text = token is { Length: > 0 } ? (token[0] == '#' ? localise(token) : token) : null;
        string name = text is { Length: > 0 } ? text
            : className.StartsWith("weapon_", StringComparison.Ordinal) ? className["weapon_".Length..] : className;
        _names[className] = name;
        return name;
    }
}
