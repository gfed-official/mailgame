using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// A tick reader that is always inside a phase, the same allocator test hook the id allocator allocation test uses
/// (<c>IdAllocatorAllocationTests</c>): it lets a <see cref="MailTable"/> be driven without building a world. Tests that need a
/// real world run <see cref="MailTable.Create"/> from a stub phase instead.
/// </summary>
internal sealed class InPhaseTick : ITickReader
{
    /// <summary>The tick reported to the check ring.</summary>
    public uint Tick { get; set; }

    /// <inheritdoc />
    public uint CurrentTick => Tick;

    /// <inheritdoc />
    public TickStage Stage => TickStage.InPhase;

    /// <inheritdoc />
    public PhaseId Phase => PhaseId.PlayersStep;
}

/// <summary>One mail table with its allocator, ring and counters, for the mail table tests.</summary>
internal sealed class MailTableRig
{
    /// <summary>Builds a rig.</summary>
    /// <param name="pages">The <c>mailTablePages</c> setting.</param>
    /// <param name="lastIssued">The mail counter's starting value (last issued id).</param>
    public MailTableRig(int pages = 64, uint lastIssued = 0)
    {
        Counters = new SimCounters();
        Ring = new SimCheckRing();
        Tick = new InPhaseTick();
        Ids = new MailIds(Tick, Ring, Counters, lastIssued);
        Table = new MailTable(new SimSettings { MailTablePages = pages }, Ids, Tick, Ring);
    }

    public SimCounters Counters { get; }

    public SimCheckRing Ring { get; }

    public InPhaseTick Tick { get; }

    public MailIds Ids { get; }

    public MailTable Table { get; }

    /// <summary>Creates one row with fixed, valid fields.</summary>
    public MailId Create(ushort value = 100, byte shift = 1, MailFlags flags = MailFlags.None) =>
        Table.Create(DefIndex.FromValue(3), AddressId.Pack(1, 2, 7), value, shift, flags);

    /// <summary>Counts ring records with the given code.</summary>
    public int RingCount(CheckCode code)
    {
        int n = 0;
        for (int i = 0; i < Ring.Count; i++)
        {
            if (Ring[i].Code == (ushort)code)
            {
                n++;
            }
        }

        return n;
    }
}
