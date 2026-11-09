namespace PerformativeMail.Sim.Players;

/// <summary>The result of <see cref="IPlayersApi.CorroborateClaim"/> (GDD F5; ADR-0014 section 5; throwing-catching story 009).</summary>
public enum ClaimCorroboration : byte
{
    /// <summary>The claim is within the tolerance (3D, inclusive) of a corroborating host sample.</summary>
    Corroborated = 0,

    /// <summary>The host position is fresh but does not corroborate the claim.</summary>
    Uncorroborated = 1,

    /// <summary>No usable sample: the position is stale (GDD F5 <c>stale(p)</c>) or the slot is empty.</summary>
    NoSample = 2,
}
