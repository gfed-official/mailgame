using System.Numerics;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>Whether <see cref="OpQueue.Admit"/> would let an op in right now.</summary>
internal enum OpAdmission : byte
{
    /// <summary>There is room for this actor class.</summary>
    Accepted = 0,

    /// <summary>A non-System actor was refused: <c>Count &gt;= Capacity - SystemOpReserve</c>.</summary>
    BusyPlayer = 1,

    /// <summary>A System actor was refused: the ring is completely full.</summary>
    BusySystem = 2,
}

/// <summary>
/// The preallocated op FIFO owned by <see cref="SimWorld"/> (ADR-0007 section 2, TR-sim-017): an <see cref="Op"/> ring plus a
/// parallel <see cref="uint"/> ring of enqueue-boundary ticks (the <c>CurrentTick</c> at enqueue, the first tick that could
/// apply the op). Capacity is a power of two so indices wrap with a mask. Both arrays are allocated once in the constructor;
/// nothing here allocates, resizes or throws after construction. Not thread-safe (owner thread only). Policy (state checks,
/// counters, stamping the <c>opSeq</c> counter) lives in <see cref="SimWorld.EnqueueOp"/>; this type is the storage plus
/// the reserve arithmetic. The drain (Story 020), the dump (Story 019) and tests read it through <see cref="Count"/>,
/// <see cref="PeekAt"/> and <see cref="BoundaryTick"/>.
/// <para>
/// Bad construction arguments assert under <c>SIM_ASSERT</c>. Other builds repair them instead of throwing: a capacity that is
/// not a power of two is rounded up to the next one (at least 1, at most 2^30) and a reserve outside <c>0..Capacity</c> is clamped.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var queue = new OpQueue(capacity: 8, systemOpReserve: 2);
/// if (queue.Admit(isSystem: false) == OpAdmission.Accepted)
/// {
///     queue.Push(in op, actor, opSeq: 1, boundaryTick: 0);
/// }
///
/// ref readonly Op head = ref queue.PeekAt(0);
/// queue.TryDequeue(out Op taken, out uint boundary);
/// </code>
/// </example>
internal sealed class OpQueue
{
    private const int MaxCapacity = 1 << 30;

    private readonly Op[] _ops;
    private readonly uint[] _boundaryTicks;
    private readonly int _mask;
    private int _head;
    private int _count;

    /// <summary>Allocates the ring and the boundary-tick array.</summary>
    /// <param name="capacity">Ring capacity; a power of two (default settings: 4096).</param>
    /// <param name="systemOpReserve">Slots only System actors may fill; <c>0..capacity</c> (default settings: 64).</param>
    public OpQueue(int capacity, int systemOpReserve)
    {
        SimAssert.That(capacity >= 1 && BitOperations.IsPow2(capacity), SimAssertCode.OpQueueCapacityNotPow2);
        SimAssert.That(systemOpReserve >= 0 && systemOpReserve <= capacity, SimAssertCode.OpQueueReserveRange);

        int repaired = capacity <= 1 ? 1 : capacity >= MaxCapacity ? MaxCapacity : (int)BitOperations.RoundUpToPowerOf2((uint)capacity);
        Capacity = repaired;
        SystemOpReserve = Math.Clamp(systemOpReserve, 0, repaired);
        _mask = repaired - 1;
        _ops = new Op[repaired];
        _boundaryTicks = new uint[repaired];
    }

    /// <summary>Ring capacity (a power of two).</summary>
    public int Capacity { get; }

    /// <summary>Slots reserved for System actors.</summary>
    public int SystemOpReserve { get; }

    /// <summary>Ops currently queued (0 to <see cref="Capacity"/>).</summary>
    public int Count => _count;

    /// <summary>Whether an op from a System or non-System actor would be admitted now. Does not change the queue.</summary>
    /// <param name="isSystem">True for <see cref="ActorId.System"/>.</param>
    /// <returns><see cref="OpAdmission.Accepted"/>, or why the op would be refused.</returns>
    /// <example><code>OpAdmission a = queue.Admit(actor.IsSystem);</code></example>
    public OpAdmission Admit(bool isSystem)
    {
        if (_count >= Capacity)
        {
            return isSystem ? OpAdmission.BusySystem : OpAdmission.BusyPlayer;
        }

        if (!isSystem && _count >= Capacity - SystemOpReserve)
        {
            return OpAdmission.BusyPlayer;
        }

        return OpAdmission.Accepted;
    }

    /// <summary>
    /// Appends a copy of <paramref name="op"/> with <see cref="Op.Actor"/> and <see cref="Op.OpSeq"/> overwritten and records
    /// its boundary tick. Applies no reserve policy (use <see cref="Admit"/> first); it only refuses when the ring is completely full.
    /// </summary>
    /// <param name="op">The op to copy in.</param>
    /// <param name="actor">The value stored in <see cref="Op.Actor"/>, replacing the incoming field.</param>
    /// <param name="opSeq">The value stored in <see cref="Op.OpSeq"/>, replacing the incoming field.</param>
    /// <param name="boundaryTick">The tick at enqueue.</param>
    /// <returns>False (nothing stored) when the ring is completely full.</returns>
    /// <example><code>bool stored = queue.Push(in op, actor, opSeq: 7, boundaryTick: world.CurrentTick);</code></example>
    public bool Push(in Op op, ActorId actor, uint opSeq, uint boundaryTick)
    {
        if (_count >= Capacity)
        {
            return false;
        }

        int index = (_head + _count) & _mask;
        ref Op slot = ref _ops[index];
        slot = op;
        slot.Actor = actor;
        slot.OpSeq = opSeq;
        _boundaryTicks[index] = boundaryTick;
        _count++;
        return true;
    }

    /// <summary>The i-th queued op, oldest first. Asserts <c>0 &lt;= i &lt; Count</c> under <c>SIM_ASSERT</c>; other builds wrap the index (never out of bounds, content unspecified).</summary>
    /// <param name="i">Index from the head.</param>
    /// <returns>A read-only reference into the ring; valid until the next <see cref="Push"/> or dequeue.</returns>
    /// <example><code>ref readonly Op head = ref queue.PeekAt(0);</code></example>
    public ref readonly Op PeekAt(int i)
    {
        SimAssert.That(unchecked((uint)i) < (uint)_count, SimAssertCode.OpQueueIndexRange);
        return ref _ops[(_head + i) & _mask];
    }

    /// <summary>The boundary tick of the i-th queued op. Same index rules as <see cref="PeekAt"/>.</summary>
    /// <param name="i">Index from the head.</param>
    /// <returns>The <c>CurrentTick</c> when the op was enqueued.</returns>
    /// <example><code>uint t = queue.BoundaryTick(0);</code></example>
    public uint BoundaryTick(int i)
    {
        SimAssert.That(unchecked((uint)i) < (uint)_count, SimAssertCode.OpQueueIndexRange);
        return _boundaryTicks[(_head + i) & _mask];
    }

    /// <summary>Removes the head op and returns it with its boundary tick.</summary>
    /// <param name="op">The removed op (default when empty).</param>
    /// <param name="boundaryTick">Its boundary tick (0 when empty).</param>
    /// <returns>False when the queue is empty.</returns>
    /// <example><code>while (queue.TryDequeue(out Op op, out uint tick)) { /* apply */ }</code></example>
    public bool TryDequeue(out Op op, out uint boundaryTick)
    {
        if (_count == 0)
        {
            op = default;
            boundaryTick = 0;
            return false;
        }

        op = _ops[_head];
        boundaryTick = _boundaryTicks[_head];
        _head = (_head + 1) & _mask;
        _count--;
        return true;
    }

    /// <summary>Removes the first <paramref name="n"/> ops without reading them (the drain uses it after applying a batch). A count above <see cref="Count"/> asserts under <c>SIM_ASSERT</c> and is clamped otherwise; a negative count is treated as 0.</summary>
    /// <param name="n">How many head ops to drop.</param>
    /// <example><code>queue.DropFront(applied);</code></example>
    public void DropFront(int n)
    {
        SimAssert.That(n >= 0 && n <= _count, SimAssertCode.OpQueueIndexRange);
        n = Math.Clamp(n, 0, _count);
        _head = (_head + n) & _mask;
        _count -= n;
    }
}
