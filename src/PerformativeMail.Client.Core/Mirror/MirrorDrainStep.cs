using PerformativeMail.Client.Core.Net;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Mirror;

/// <summary>
/// The <see cref="ClientStepId.MirrorDrain"/> step (ADR-0003 section 6, ADR-0011 section 2): once per client tick it drains the
/// <see cref="ClientInbox"/> in arrival order and dispatches each record to every registered <see cref="IMirrorConsumer"/>,
/// replies through <see cref="IMirrorConsumer.OnReply"/> and everything else through <see cref="IMirrorConsumer.OnEntry"/>.
/// This is the only place host data is applied on the client. Only records queued when the step starts are drained, so a
/// consumer cannot extend a drain. Client.Core owns this step and binds it into the Sim's <c>ClientLayerBuilder</c>
/// (Client.Core to Sim is the allowed dependency direction). Allocation-free after consumers are registered.
/// </summary>
/// <example>
/// <code>
/// var drain = new MirrorDrainStep(inbox);
/// drain.Register(containerMirror);
/// drain.BindTo(builder);   // builder.Bind(ClientStepId.MirrorDrain, drain)
/// </code>
/// </example>
public sealed class MirrorDrainStep : IClientStep
{
    private readonly ClientInbox _inbox;
    private readonly ClientAdapter? _adapter;
    private IMirrorConsumer[] _consumers = Array.Empty<IMirrorConsumer>();

    /// <summary>Creates the step over <paramref name="inbox"/>.</summary>
    /// <param name="inbox">The inbox the client adapter pumps into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inbox"/> is null.</exception>
    public MirrorDrainStep(ClientInbox inbox)
        : this(inbox, null)
    {
    }

    /// <summary>
    /// Creates the step over <paramref name="inbox"/> with the adapter it pumps first (network-adapter-loopback story 007):
    /// at the start of each client tick the step calls <c>adapter.PumpInbound(inbox)</c> (decode only) and then drains, so a
    /// batch flushed after host tick T is applied at exactly the next client tick, with no frame-delayed queue.
    /// </summary>
    /// <param name="inbox">The inbox the adapter pumps into.</param>
    /// <param name="adapter">The client adapter to pump before each drain; null drains an externally pumped inbox.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inbox"/> is null.</exception>
    public MirrorDrainStep(ClientInbox inbox, ClientAdapter? adapter)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        _inbox = inbox;
        _adapter = adapter;
    }

    /// <summary>Entries dispatched (replies excluded).</summary>
    public long DrainedEntryCount { get; private set; }

    /// <summary>Replies dispatched.</summary>
    public long DrainedReplyCount { get; private set; }

    /// <summary>Records drained while no consumer was registered (they are dropped).</summary>
    public long UnconsumedCount { get; private set; }

    /// <summary>Registers a consumer; consumers are called in registration order. Setup-time call (allocates).</summary>
    /// <param name="consumer">The mirror consumer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="consumer"/> is null.</exception>
    public void Register(IMirrorConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var next = new IMirrorConsumer[_consumers.Length + 1];
        _consumers.CopyTo(next, 0);
        next[^1] = consumer;
        _consumers = next;
    }

    /// <summary>Binds this step at <see cref="ClientStepId.MirrorDrain"/>.</summary>
    /// <param name="builder">The client layer builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public void BindTo(ClientLayerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Bind(ClientStepId.MirrorDrain, this);
    }

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        _adapter?.PumpInbound(_inbox);
        IMirrorConsumer[] consumers = _consumers;
        int budget = _inbox.Count;
        for (int i = 0; i < budget && _inbox.TryDequeue(out ClientInboxRecord record); i++)
        {
            if (consumers.Length == 0)
            {
                UnconsumedCount++;
                continue;
            }

            if (record.IsReply)
            {
                DrainedReplyCount++;
                Reply reply = record.Entry.PayloadAs<Reply>();
                for (int c = 0; c < consumers.Length; c++)
                {
                    consumers[c].OnReply(record.Tick, in reply);
                }
            }
            else
            {
                DrainedEntryCount++;
                for (int c = 0; c < consumers.Length; c++)
                {
                    consumers[c].OnEntry(record.Tick, in record.Entry);
                }
            }
        }
    }
}
