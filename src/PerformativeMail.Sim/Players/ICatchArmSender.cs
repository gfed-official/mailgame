namespace PerformativeMail.Sim.Players;

/// <summary>
/// The port press resolution uses to ask Throwing to send <c>CatchArm{down|up}</c> (ADR-0014 section 3 rules 1 and 3; ADR-0015
/// section 7). Implemented by Throwing's client code (throwing-catching story 022 builds the op). PROVISIONAL shape: the
/// throwing-catching epic owns it. Must not allocate.
/// </summary>
/// <example><code>sender.Send(down: true);   // catch-armed while held</code></example>
public interface ICatchArmSender
{
    /// <summary>Sends <c>CatchArm{down}</c> or <c>CatchArm{up}</c>.</summary>
    /// <param name="down">True for the press (arm), false for the release.</param>
    /// <returns>True when sent; false when the request port was busy.</returns>
    bool Send(bool down);
}
