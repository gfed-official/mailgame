namespace PerformativeMail.Sim.Core;

/// <summary>
/// Read-only view of the world's op queue (TD-031): the dump and tests read through it, and it has no way to push, dequeue or
/// drop, so the only writers are <see cref="SimWorld.EnqueueOp"/> and the phase-0 drain. A plain struct over the world's queue;
/// allocation-free. Owner thread only.
/// </summary>
/// <example>
/// <code>
/// OpQueueView ops = world.Ops;
/// for (int i = 0; i &lt; ops.Count; i++) { ref readonly Op op = ref ops.PeekAt(i); }
/// </code>
/// </example>
internal readonly struct OpQueueView
{
    private readonly OpQueue _queue;

    /// <summary>Wraps <paramref name="queue"/>.</summary>
    /// <param name="queue">The world's queue.</param>
    public OpQueueView(OpQueue queue)
    {
        _queue = queue;
    }

    /// <summary>Ops currently queued.</summary>
    public int Count => _queue.Count;

    /// <summary>Ring capacity.</summary>
    public int Capacity => _queue.Capacity;

    /// <summary>Slots reserved for System actors.</summary>
    public int SystemOpReserve => _queue.SystemOpReserve;

    /// <summary>The i-th queued op, oldest first (see <see cref="OpQueue.PeekAt"/>).</summary>
    /// <param name="i">Index from the head.</param>
    /// <returns>A read-only reference into the ring.</returns>
    public ref readonly Op PeekAt(int i) => ref _queue.PeekAt(i);

    /// <summary>The boundary tick of the i-th queued op.</summary>
    /// <param name="i">Index from the head.</param>
    /// <returns>The <c>CurrentTick</c> at enqueue.</returns>
    public uint BoundaryTick(int i) => _queue.BoundaryTick(i);
}
