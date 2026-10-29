using PerformativeMail.Sim.Content.Balance;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The T6 receiver rank table (GDD <c>design/gdd/throwing-catching.md</c> T6; ADR-0015 section 4): one rank 0 to 7 per
/// <see cref="ReceiverKind"/>, copied from the <c>throw.rank.*</c> knobs of <see cref="ThrowBalance"/>. THROW-CV-05 guarantees
/// the eight values are a permutation of 0 to 7, so two different kinds never share a rank. A lower rank wins a tie on
/// <c>f</c>. Immutable value type; <c>default</c> is not a valid table (every rank 0), so build it with
/// <see cref="ReceiverRanks(ThrowBalance)"/> or <see cref="Default"/>.
/// </summary>
/// <example>
/// <code>
/// var ranks = new ReceiverRanks(contentSet.Throw);
/// int water = ranks.Of(ReceiverKind.Water);   // 7 with the M-1a defaults
/// </code>
/// </example>
public readonly struct ReceiverRanks
{
    private readonly byte _armedCatcher;
    private readonly byte _mailbox;
    private readonly byte _goon;
    private readonly byte _teammate;
    private readonly byte _belt;
    private readonly byte _solid;
    private readonly byte _ground;
    private readonly byte _water;

    /// <summary>Copies the eight ranks from a validated <see cref="ThrowBalance"/> (THROW-CV-05: a permutation of 0 to 7).</summary>
    /// <param name="balance">The content set's throw record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="balance"/> is null.</exception>
    public ReceiverRanks(ThrowBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        this = new ReceiverRanks(
            balance.RankArmedCatcher,
            balance.RankMailbox,
            balance.RankGoon,
            balance.RankTeammate,
            balance.RankBelt,
            balance.RankSolid,
            balance.RankGround,
            balance.RankWater);
    }

    /// <summary>Creates a table from explicit ranks (each 0 to 7; the caller keeps them a permutation).</summary>
    /// <param name="armedCatcher">Armed catcher rank.</param>
    /// <param name="mailbox">Mailbox rank.</param>
    /// <param name="goon">Goon rank.</param>
    /// <param name="teammate">Teammate rank.</param>
    /// <param name="belt">Belt rank.</param>
    /// <param name="solid">Solid (and map side) rank.</param>
    /// <param name="ground">Ground rank.</param>
    /// <param name="water">Water rank.</param>
    public ReceiverRanks(int armedCatcher, int mailbox, int goon, int teammate, int belt, int solid, int ground, int water)
    {
        _armedCatcher = (byte)armedCatcher;
        _mailbox = (byte)mailbox;
        _goon = (byte)goon;
        _teammate = (byte)teammate;
        _belt = (byte)belt;
        _solid = (byte)solid;
        _ground = (byte)ground;
        _water = (byte)water;
    }

    /// <summary>The GDD default table: each kind's rank is its <see cref="ReceiverKind"/> value.</summary>
    public static ReceiverRanks Default => new(0, 1, 2, 3, 4, 5, 6, 7);

    /// <summary>The rank of a receiver kind (lower wins a tie on <c>f</c>).</summary>
    /// <param name="kind">The receiver kind.</param>
    /// <returns>Its rank, 0 to 7.</returns>
    /// <example><code>byte r = ReceiverRanks.Default.Of(ReceiverKind.Ground);   // 6</code></example>
    public byte Of(ReceiverKind kind) => kind switch
    {
        ReceiverKind.ArmedCatcher => _armedCatcher,
        ReceiverKind.Mailbox => _mailbox,
        ReceiverKind.Goon => _goon,
        ReceiverKind.Teammate => _teammate,
        ReceiverKind.Belt => _belt,
        ReceiverKind.Solid => _solid,
        ReceiverKind.Ground => _ground,
        _ => _water, // ReceiverKind.Water; the enum has no other value
    };
}
