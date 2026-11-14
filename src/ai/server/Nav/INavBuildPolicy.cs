namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// A game binding's consent to the server building a map's navigation in its own
/// process when the map has none (plan_bots_teams_tf2 E.3). The generic binding
/// builds only for a game whose binding registers one and answers yes for the
/// map: a game that registers none (TF2's, until P6 asks) keeps the refusal and
/// never starts a build on its own.
/// </summary>
public interface INavBuildPolicy
{
    /// <summary>Whether a navigation may be built for <paramref name="map"/>, which has none.</summary>
    bool Builds(string map);
}
