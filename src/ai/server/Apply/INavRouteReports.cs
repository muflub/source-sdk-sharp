namespace SourceSharp.Ai.Server;

/// <summary>
/// Where the main thread hands what each bot's follower says of the route just ahead (round 2's S2), as it takes the bot's
/// drive step back, beside its link (<see cref="INavLinkReports"/>): how many second presses in the air a jump there needs
/// (<see cref="BrainDriver.DashesAheadOf"/>), so a game's hands can get ready for it (TF2's Atomizer, the third jump's).
/// Stamped as the link reports are, so a decide of tick D reads the newest report stamped before D however the workers
/// ran. The binding of a game whose hands care registers one; none, and nothing is handed.
/// </summary>
public interface INavRouteReports
{
    /// <summary><paramref name="bot"/>'s route needs <paramref name="dashes"/> presses in the air just ahead, in the drive step handed on <paramref name="stamp"/>. Main thread.</summary>
    void Dashes(ActorId bot, Tick stamp, int dashes);
}
