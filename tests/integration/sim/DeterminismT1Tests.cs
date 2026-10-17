using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Core.Replay;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Integration.Sim;

/// <summary>
/// Records the 50 000-tick, 50 000-op log once (world R), then replays it on three worlds, each built and ticked on its own
/// thread (A and B: same run seed; C: another), hashing the canonical dump with SHA-256 after every tick. Shared by every test of
/// <see cref="DeterminismT1Tests"/> through <c>IClassFixture</c>.
/// </summary>
/// <example><code>public class DeterminismT1Tests : IClassFixture&lt;DeterminismRun&gt; { }</code></example>
public sealed class DeterminismRun
{
    /// <summary>Ticks in the log; one op is enqueued per tick, so this is also the op count.</summary>
    public const int Ticks = 50_000;

    /// <summary>Run seed of worlds R, A and B.</summary>
    public const ulong Seed = 0x5EED_0001UL;

    /// <summary>Run seed of world C.</summary>
    public const ulong OtherSeed = 0x5EED_0002UL;

    /// <summary>Size of one SHA-256 digest.</summary>
    public const int HashSize = 32;

    /// <summary>The tick on which the script issues the first accepted RNG draw (nothing earlier touches the RNG).</summary>
    public const uint FirstDrawTick = 3;

    internal static readonly SimInitialCounters Counters = new();

    /// <summary>Records world R and replays A, B and C in parallel.</summary>
    public DeterminismRun()
    {
        Content = BuiltInContent.CreateM1a();
        RecordedHashes = new byte[(Ticks + 1) * HashSize];
        RunRecording();
        Input = new ReplayInput(Recorder!.Ops.ToArray(), Recorder.Samples.ToArray());

        HashesA = new byte[(Ticks + 1) * HashSize];
        HashesB = new byte[(Ticks + 1) * HashSize];
        HashesC = new byte[(Ticks + 1) * HashSize];
        var results = new WorldResult?[3];
        var errors = new Exception?[3];
        Thread[] threads =
        [
            Start("world-A", 0, Seed, HashesA, results, errors),
            Start("world-B", 1, Seed, HashesB, results, errors),
            Start("world-C", 2, OtherSeed, HashesC, results, errors),
        ];
        foreach (Thread t in threads)
        {
            t.Join();
        }

        for (int i = 0; i < errors.Length; i++)
        {
            if (errors[i] is { } e)
            {
                ExceptionDispatchInfo.Capture(e).Throw();
            }
        }

        ResultA = results[0]!;
        ResultB = results[1]!;
        ResultC = results[2]!;
    }

    internal ContentSet Content { get; }

    /// <summary>Gets the recorder of world R (op log, drain log with replies, sample log).</summary>
    internal InMemoryRecorder? Recorder { get; private set; }

    /// <summary>Gets the fixture system of world R.</summary>
    internal DeterminismFixtureSystem? RecordedSystem { get; private set; }

    /// <summary>Gets the replay input extracted from the recorder.</summary>
    internal ReplayInput Input { get; }

    /// <summary>Gets world R's hashes: entry <c>i</c> is the hash after <c>i</c> ticks (entry 0 = before the first tick).</summary>
    public byte[] RecordedHashes { get; }

    /// <summary>Gets world A's hashes (same layout).</summary>
    public byte[] HashesA { get; }

    /// <summary>Gets world B's hashes (same layout).</summary>
    public byte[] HashesB { get; }

    /// <summary>Gets world C's hashes (same layout).</summary>
    public byte[] HashesC { get; }

    /// <summary>Gets what world A's thread reported.</summary>
    internal WorldResult ResultA { get; }

    /// <summary>Gets what world B's thread reported.</summary>
    internal WorldResult ResultB { get; }

    /// <summary>Gets what world C's thread reported.</summary>
    internal WorldResult ResultC { get; }

    /// <summary>Gets the index of the first differing hash of the two buffers, or -1 when the first <paramref name="count"/> hashes match.</summary>
    /// <param name="a">One hash buffer.</param>
    /// <param name="b">The other hash buffer.</param>
    /// <param name="count">How many hashes to compare.</param>
    /// <returns>The first differing index, or -1.</returns>
    public static int FirstMismatch(byte[] a, byte[] b, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!a.AsSpan(i * HashSize, HashSize).SequenceEqual(b.AsSpan(i * HashSize, HashSize)))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Builds a fresh world with a fresh fixture and replays <paramref name="input"/> on the calling thread, one op per tick,
    /// writing the SHA-256 of the dump into <paramref name="hashes"/> before the first tick and after every tick. Allocates the
    /// dump buffer once, outside the loop.
    /// </summary>
    /// <param name="content">The content set.</param>
    /// <param name="seed">The run seed.</param>
    /// <param name="input">The op and sample log.</param>
    /// <param name="ticks">How many ticks to run.</param>
    /// <param name="hashes">Preallocated, at least <c>(ticks + 1) * HashSize</c> bytes.</param>
    /// <returns>The world's final facts, taken on the calling (owner) thread.</returns>
    internal static WorldResult Replay(ContentSet content, ulong seed, ReplayInput input, int ticks, byte[] hashes)
    {
        var fixture = new DeterminismFixture();
        SimHost host = SimComposition.Build(new SimBuildArgs(fixture.Profile, content, seed, Counters));
        SimWorld world = host.World;
        fixture.World = world;
        var dump = new ArrayBufferWriter<byte>(1 << 16);
        HashInto(world, dump, hashes, 0);

        int nextOp = 0;
        int nextSample = 0;
        for (int t = 0; t < ticks; t++)
        {
            while (nextSample < input.Samples.Length && input.Samples[nextSample].ReceivedTick <= (uint)t)
            {
                RecordedSample rs = input.Samples[nextSample++];
                TransformSample sample = rs.SampleValue;
                if (!world.TryWriteSample(PlayerSlot.FromValue(rs.Slot), in sample))
                {
                    throw new InvalidOperationException($"Sample for tick {t} was refused.");
                }
            }

            while (nextOp < input.Ops.Length && input.Ops[nextOp].BoundaryTick <= (uint)t)
            {
                Op op = input.Ops[nextOp++].Op;
                if (!world.EnqueueOp(op.Actor, in op))
                {
                    throw new InvalidOperationException($"Op for tick {t} was refused at enqueue.");
                }
            }

            world.Tick();
            HashInto(world, dump, hashes, t + 1);
        }

        string finalHex = DumpHash.Sha256(world);
        string lastHex = Convert.ToHexString(hashes.AsSpan(ticks * HashSize, HashSize));
        return new WorldResult(Environment.CurrentManagedThreadId, world.CurrentTick, finalHex == lastHex, fixture.System!.Stats);
    }

    private static void HashInto(SimWorld world, ArrayBufferWriter<byte> dump, byte[] hashes, int index)
    {
        dump.Clear();
        if (!world.TryWriteDump(dump))
        {
            throw new InvalidOperationException("TryWriteDump was refused.");
        }

        SHA256.HashData(dump.WrittenSpan, hashes.AsSpan(index * HashSize, HashSize));
    }

    private Thread Start(string name, int slot, ulong seed, byte[] hashes, WorldResult?[] results, Exception?[] errors)
    {
        var thread = new Thread(() =>
        {
            try
            {
                results[slot] = Replay(Content, seed, Input, Ticks, hashes);
            }
            catch (Exception ex)
            {
                errors[slot] = ex;
            }
        })
        {
            Name = name,
        };
        thread.Start();
        return thread;
    }

    // ------------------------------------------------------------ recording (world R, seeded generator)

    private static uint Next(ref Pcg32 rng, uint bound) => (uint)(((ulong)rng.Next32() * bound) >> 32);

    private static Op MakeOp(OpKind kind, ActorId actor, uint req, byte verb, ushort key, int arg)
    {
        var op = new Op { Kind = kind, Actor = actor, ReqId = req };
        op.SetPayload(new DeterminismPayload { Verb = verb, Key = key, Arg = arg });
        return op;
    }

    private static ActorId PlayerActor(uint counter) => new(EntityId.Compose(EntityClass.Player, counter).Value);

    private void RunRecording()
    {
        var fixture = new DeterminismFixture();
        var header = RecordingHeader.Create(Content, Seed, Counters);
        var recorder = new InMemoryRecorder(header, new RecorderCapacity(Ticks + 16, Ticks + 16, Ticks, 16, 16, 64));
        SimHost host = SimComposition.Build(new SimBuildArgs(fixture.Profile, Content, Seed, Counters) { Recorder = recorder });
        SimWorld world = host.World;
        fixture.World = world;
        var dump = new ArrayBufferWriter<byte>(1 << 16);
        HashInto(world, dump, RecordedHashes, 0);

        var rng = new Pcg32(0xD1CE_F00DUL, 54UL);
        var nextSeq = new uint[PlayerSlot.Count];
        uint nextPlayerCounter = 1; // the Player class counter the next accepted join receives
        uint slot1Player = 0;
        uint slot3Player = 0;
        uint staleSlot3Player = 0;

        for (uint t = 0; t < Ticks; t++)
        {
            Op op = ScriptOp(ref rng, t, ref nextPlayerCounter, ref slot1Player, ref slot3Player, ref staleSlot3Player);
            if (!world.EnqueueOp(op.Actor, in op))
            {
                throw new InvalidOperationException($"Recording: op for tick {t} was refused at enqueue.");
            }

            if (t >= 1 && (t % 2 == 0 || t % 7 == 0))
            {
                WriteSample(world, ref rng, nextSeq, 1);
                if (slot3Player != 0 && t % 7 == 0)
                {
                    WriteSample(world, ref rng, nextSeq, 3);
                }
            }

            world.Tick();
            HashInto(world, dump, RecordedHashes, (int)t + 1);
            recorder.Flush(world.CurrentTick);
        }

        Recorder = recorder;
        RecordedSystem = fixture.System;
    }

    private static void WriteSample(SimWorld world, ref Pcg32 rng, uint[] nextSeq, byte slot)
    {
        var s = new TransformSample
        {
            Seq = ++nextSeq[slot],
            PosXMm = (int)Next(ref rng, 200_000) - 100_000,
            PosYMm = (int)Next(ref rng, 5_000),
            PosZMm = (int)Next(ref rng, 200_000) - 100_000,
            YawBam = (ushort)Next(ref rng, 65_536),
        };
        if (!world.TryWriteSample(PlayerSlot.FromValue(slot), in s))
        {
            throw new InvalidOperationException("Recording: a sample was refused.");
        }
    }

    // The op for tick t. Ticks 0 to 3 are fixed (join, set, add, first draw) so that the first RNG-dependent tick is known;
    // every 5 000 ticks a join/leave cycle on slot 3 runs, including a bad-slot join and a leave of an absent player; all the
    // other ticks draw a weighted random op that is accepted or rejected by the rules in DeterminismFixtureSystem.
    private static Op ScriptOp(ref Pcg32 rng, uint t, ref uint nextPlayerCounter, ref uint slot1Player, ref uint slot3Player, ref uint staleSlot3Player)
    {
        uint req = t + 1;
        ActorId member = slot1Player == 0 ? ActorId.System : PlayerActor(slot1Player);
        switch (t)
        {
            case 0:
                slot1Player = nextPlayerCounter++;
                return Join(req, 1);
            case 1:
                return MakeOp(DeterminismFixture.PlayerOpKind, member, req, DeterminismFixture.VerbSet, 1, 100);
            case 2:
                return MakeOp(DeterminismFixture.PlayerOpKind, member, req, DeterminismFixture.VerbAdd, 1, 5);
            case DeterminismRun.FirstDrawTick:
                return MakeOp(DeterminismFixture.PlayerOpKind, member, req, DeterminismFixture.VerbDraw, 1, 1000);
        }

        uint phase = t % 5000;
        if (phase == 5)
        {
            slot3Player = nextPlayerCounter++;
            return Join(req, 3);
        }

        if (phase == 7)
        {
            return Join(req, 9); // slot_invalid
        }

        if (phase == 9)
        {
            return Join(req, 1); // slot_busy
        }

        if (phase == 2500)
        {
            staleSlot3Player = slot3Player;
            slot3Player = 0;
            return Leave(req, staleSlot3Player);
        }

        if (phase == 2501)
        {
            return Leave(req, staleSlot3Player); // absent player: bad_request
        }

        ushort key = (ushort)Next(ref rng, 256);
        uint pick = Next(ref rng, 100);
        if (pick < 40)
        {
            int arg = (int)Next(ref rng, 1000) - (Next(ref rng, 10) == 0 ? 1000 : 0); // ~10% negative: rejected
            return MakeOp(DeterminismFixture.PlayerOpKind, member, req, DeterminismFixture.VerbSet, key, arg);
        }

        if (pick < 55)
        {
            return MakeOp(DeterminismFixture.PlayerOpKind, member, req, DeterminismFixture.VerbAdd, key, (int)Next(ref rng, 50));
        }

        if (pick < 65)
        {
            return MakeOp(DeterminismFixture.PlayerOpKind, member, req, DeterminismFixture.VerbRemove, key, 0);
        }

        if (pick < 80)
        {
            return MakeOp(DeterminismFixture.PlayerOpKind, member, req, DeterminismFixture.VerbDraw, key, (int)Next(ref rng, 100));
        }

        if (pick < 88)
        {
            return MakeOp(DeterminismFixture.SystemOpKind, ActorId.System, req, DeterminismFixture.VerbAlloc, key, 0);
        }

        if (pick < 92)
        {
            return MakeOp(DeterminismFixture.SystemOpKind, ActorId.System, req, DeterminismFixture.VerbBump, 0, 0);
        }

        if (pick < 95)
        {
            return MakeOp(DeterminismFixture.SystemOpKind, member, req, DeterminismFixture.VerbBump, 0, 0); // system-only kind from a player
        }

        if (pick < 98)
        {
            return MakeOp(DeterminismFixture.PlayerOpKind, new ActorId(0x0100_0F00u), req, DeterminismFixture.VerbSet, key, 1); // unknown actor
        }

        return MakeOp(DeterminismFixture.PlayerOpKind, member, req, 9, key, 0); // unknown verb
    }

    private static Op Join(uint req, byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, Actor = ActorId.System, ReqId = req };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        return op;
    }

    private static Op Leave(uint req, uint playerCounter)
    {
        var op = new Op { Kind = OpKind.PlayerLeave, Actor = ActorId.System, ReqId = req };
        op.SetPayload(new PlayerLeavePayload { PlayerId = EntityId.Compose(EntityClass.Player, playerCounter).Value });
        return op;
    }
}

/// <summary>The op and sample log a replay feeds to a world.</summary>
/// <param name="Ops">Enqueued ops in enqueue order with their boundary ticks.</param>
/// <param name="Samples">Latched samples in latch order with their received ticks.</param>
/// <example><code>var tampered = new ReplayInput(ops, input.Samples);</code></example>
internal sealed record ReplayInput(RecordedOp[] Ops, RecordedSample[] Samples);

/// <summary>What a world's thread reports after its last tick.</summary>
/// <param name="ThreadId">Managed id of the thread that built and ticked the world.</param>
/// <param name="CurrentTick">The world's tick count at the end.</param>
/// <param name="FinalHashMatchesBuffer"><c>DumpHash.Sha256</c> of the final dump equals the last buffered per-tick hash.</param>
/// <param name="Stats">The fixture system's counters.</param>
/// <example><code>Assert.NotEqual(run.ResultA.ThreadId, run.ResultB.ThreadId);</code></example>
internal sealed record WorldResult(int ThreadId, uint CurrentTick, bool FinalHashMatchesBuffer, DeterminismStats Stats);

/// <summary>
/// SC-DET-1 (TR-sim-033, TR-sim-032; Story 027): two <c>SimWorld</c>s built and ticked in parallel on separate threads from the
/// same content, settings and run seed, fed the same 50 000-op log and sample log one op per tick, have the same SHA-256 canonical
/// dump hash after every tick; a world with another run seed diverges on the first tick whose state depends on an RNG draw. The
/// remaining tests keep the check from being vacuous: the log really contains rejected ops, joins and leaves, allocations, draws,
/// events and samples; and a one-op or one-sample change to the log is caught at exactly the tick it takes effect.
/// </summary>
[Trait("Id", "SC-DET-1")]
public class DeterminismT1Tests : IClassFixture<DeterminismRun>
{
    private readonly DeterminismRun _run;

    /// <summary>Creates the test class around the shared recording and replay.</summary>
    /// <param name="run">The shared run.</param>
    public DeterminismT1Tests(DeterminismRun run)
    {
        _run = run;
    }

    [Fact]
    public void TwoParallelWorlds_SameLogs_HaveIdenticalDumpHashAfterEveryTick()
    {
        int count = DeterminismRun.Ticks + 1;

        int ab = DeterminismRun.FirstMismatch(_run.HashesA, _run.HashesB, count);
        int ar = DeterminismRun.FirstMismatch(_run.HashesA, _run.RecordedHashes, count);

        Assert.Equal(-1, ab);
        Assert.Equal(-1, ar);
        Assert.Equal((uint)DeterminismRun.Ticks, _run.ResultA.CurrentTick);
        Assert.Equal((uint)DeterminismRun.Ticks, _run.ResultB.CurrentTick);
        Assert.True(_run.ResultA.FinalHashMatchesBuffer);
        Assert.True(_run.ResultB.FinalHashMatchesBuffer);
    }

    [Fact]
    public void TwoParallelWorlds_RanOnDistinctThreads_AndAppliedTheSameWork()
    {
        Assert.NotEqual(_run.ResultA.ThreadId, _run.ResultB.ThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, _run.ResultA.ThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, _run.ResultB.ThreadId);
        foreach (DeterminismStats other in new[] { _run.ResultB.Stats, _run.RecordedSystem!.Stats })
        {
            DeterminismStats a = _run.ResultA.Stats;
            Assert.Equal(
                (a.Accepted, a.Rejected, a.Allocations, a.RngDraws, a.EventsRaised, a.SamplesFolded),
                (other.Accepted, other.Rejected, other.Allocations, other.RngDraws, other.EventsRaised, other.SamplesFolded));
        }
    }

    [Fact]
    public void WorldWithOtherRunSeed_DivergesOnFirstTickWithAnRngDraw()
    {
        // The first draw is applied in tick 3, so the first differing hash is the one after tick 3 (index 4); all earlier match.
        int firstApplied = FirstAcceptedDrawApplyTick();
        Assert.Equal(DeterminismRun.FirstDrawTick, (uint)firstApplied);
        int expectedIndex = firstApplied + 1;

        int divergence = DeterminismRun.FirstMismatch(_run.HashesA, _run.HashesC, DeterminismRun.Ticks + 1);

        Assert.Equal(expectedIndex, divergence);
        Assert.Equal(DeterminismRun.FirstDrawTick + 1, (uint)divergence);
        Assert.True(divergence > 0, "ticks before the first draw must not depend on the seed");
    }

    [Fact]
    public void WorldWithOtherRunSeed_StaysDivergentAfterTheFirstDraw()
    {
        int differing = 0;
        for (int i = (int)DeterminismRun.FirstDrawTick + 1; i <= DeterminismRun.Ticks; i++)
        {
            if (!_run.HashesA.AsSpan(i * DeterminismRun.HashSize, DeterminismRun.HashSize).SequenceEqual(_run.HashesC.AsSpan(i * DeterminismRun.HashSize, DeterminismRun.HashSize)))
            {
                differing++;
            }
        }

        Assert.Equal(DeterminismRun.Ticks - (int)DeterminismRun.FirstDrawTick, differing);
    }

    [Fact]
    public void RecordedLog_HasAcceptedAndRejectedOps_Joins_Leaves_AndSamples()
    {
        InMemoryRecorder rec = _run.Recorder!;
        Assert.True(rec.IsReproducible);
        Assert.Equal(DeterminismRun.Ticks, rec.Ops.Count);
        Assert.Equal(DeterminismRun.Ticks, rec.Drains.Count);
        Assert.True(rec.Samples.Count > 20_000, $"samples recorded: {rec.Samples.Count}");

        int accepted = 0;
        int rejected = 0;
        int joinsOk = 0;
        int leavesOk = 0;
        var reasons = new SortedDictionary<ushort, int>();
        for (int i = 0; i < rec.Drains.Count; i++)
        {
            ref readonly RecordedDrain d = ref rec.Drains[i];
            Assert.Equal(rec.Ops[i].BoundaryTick, d.ApplyTick); // one op per tick: applied in the tick it was enqueued for
            if (d.Reply.IsOk)
            {
                accepted++;
                joinsOk += d.Op.Kind == OpKind.PlayerJoin ? 1 : 0;
                leavesOk += d.Op.Kind == OpKind.PlayerLeave ? 1 : 0;
            }
            else
            {
                rejected++;
                reasons[d.Reply.Reason] = reasons.GetValueOrDefault(d.Reply.Reason) + 1;
            }
        }

        Assert.Equal(DeterminismRun.Ticks, accepted + rejected);
        Assert.True(accepted > 25_000, $"accepted: {accepted}");
        Assert.True(rejected > 3_000, $"rejected: {rejected}");
        Assert.Equal(11, joinsOk); // slot 1 once, slot 3 ten times (every 5 000 ticks)
        Assert.Equal(10, leavesOk);
        foreach (ushort reason in new[] { CoreReason.BadRequest, CoreReason.UnknownActor, CoreReason.SlotInvalid, CoreReason.SlotBusy })
        {
            Assert.True(reasons.GetValueOrDefault(reason) > 0, $"no rejection with reason {reason}");
        }
    }

    [Fact]
    public void RecordedLog_ExercisesAllocationsRngDrawsEventsAndFoldedSamples()
    {
        DeterminismStats s = _run.RecordedSystem!.Stats;

        Assert.True(s.Allocations > 3_000, $"allocations: {s.Allocations}");
        Assert.True(s.RngDraws > 5_000, $"rng draws: {s.RngDraws}");
        Assert.True(s.EventsRaised > 25_000, $"events: {s.EventsRaised}");
        Assert.Equal(_run.Recorder!.Samples.Count, s.SamplesFolded);
        Assert.True(_run.RecordedSystem.RowCount > 0);
    }

    [Fact]
    public void PerTickHashes_AreSensitiveToEveryTick_NotConstant()
    {
        int unchanged = 0;
        for (int i = 1; i <= DeterminismRun.Ticks; i++)
        {
            unchanged += _run.RecordedHashes.AsSpan(i * DeterminismRun.HashSize, DeterminismRun.HashSize)
                .SequenceEqual(_run.RecordedHashes.AsSpan((i - 1) * DeterminismRun.HashSize, DeterminismRun.HashSize)) ? 1 : 0;
        }

        Assert.Equal(0, unchanged); // the tick counter alone is in the dump, so no two consecutive hashes may be equal
    }

    [Fact]
    public void OneChangedOp_DivergesAtTheTickItIsApplied()
    {
        const int Window = 1_300;
        int victim = FindOpIndex(1_000, d => d.Op.Kind == DeterminismFixture.PlayerOpKind && d.Reply.IsOk && d.Op.PayloadAs<DeterminismPayload>().Verb == DeterminismFixture.VerbSet);
        RecordedOp[] ops = (RecordedOp[])_run.Input.Ops.Clone();
        Op changed = ops[victim].Op;
        DeterminismPayload p = changed.PayloadAs<DeterminismPayload>();
        p.Arg += 1;
        changed.SetPayload(p);
        ops[victim] = new RecordedOp(ops[victim].BoundaryTick, in changed);
        var tampered = new ReplayInput(ops, _run.Input.Samples);
        var hashes = new byte[(Window + 1) * DeterminismRun.HashSize];

        DeterminismRun.Replay(_run.Content, DeterminismRun.Seed, tampered, Window, hashes);
        int divergence = DeterminismRun.FirstMismatch(hashes, _run.RecordedHashes, Window + 1);

        Assert.Equal((int)ops[victim].BoundaryTick + 1, divergence);
    }

    [Fact]
    public void OneDroppedSample_DivergesAtTheTickItWasLatched()
    {
        const int Window = 1_300;
        int victim = 700;
        uint receivedTick = _run.Input.Samples[victim].ReceivedTick;
        RecordedSample[] samples = _run.Input.Samples.Where((_, i) => i != victim).ToArray();
        var tampered = new ReplayInput(_run.Input.Ops, samples);
        var hashes = new byte[(Window + 1) * DeterminismRun.HashSize];

        DeterminismRun.Replay(_run.Content, DeterminismRun.Seed, tampered, Window, hashes);
        int divergence = DeterminismRun.FirstMismatch(hashes, _run.RecordedHashes, Window + 1);

        Assert.True(receivedTick < Window);
        Assert.Equal((int)receivedTick + 1, divergence);
    }

    [Fact]
    public void OneOpMovedToTheNextTick_Diverges()
    {
        const int Window = 1_300;
        // Arrange: an accepted, state-visible Set op, delayed by one tick.
        int victim = FindOpIndex(1_000, d => d.Op.Kind == DeterminismFixture.PlayerOpKind && d.Reply.IsOk && d.Op.PayloadAs<DeterminismPayload>().Verb == DeterminismFixture.VerbSet);
        RecordedOp[] ops = (RecordedOp[])_run.Input.Ops.Clone();
        uint originalTick = ops[victim].BoundaryTick;
        Op moved = ops[victim].Op;
        ops[victim] = new RecordedOp(originalTick + 1, in moved);
        var tampered = new ReplayInput(ops, _run.Input.Samples);
        var hashes = new byte[(Window + 1) * DeterminismRun.HashSize];

        // Act
        DeterminismRun.Replay(_run.Content, DeterminismRun.Seed, tampered, Window, hashes);
        int divergence = DeterminismRun.FirstMismatch(hashes, _run.RecordedHashes, Window + 1);

        // Assert: the dump after the op's original tick is the first to differ.
        Assert.Equal((int)originalTick + 1, divergence);
    }

    private int FirstAcceptedDrawApplyTick()
    {
        InMemoryRecorder rec = _run.Recorder!;
        for (int i = 0; i < rec.Drains.Count; i++)
        {
            ref readonly RecordedDrain d = ref rec.Drains[i];
            if (d.Op.Kind == DeterminismFixture.PlayerOpKind && d.Reply.IsOk && d.Op.PayloadAs<DeterminismPayload>().Verb == DeterminismFixture.VerbDraw)
            {
                return (int)d.ApplyTick;
            }
        }

        throw new InvalidOperationException("The log holds no accepted draw.");
    }

    private int FindOpIndex(int from, Func<RecordedDrain, bool> match)
    {
        InMemoryRecorder rec = _run.Recorder!;
        for (int i = from; i < rec.Drains.Count; i++)
        {
            if (match(rec.Drains[i]))
            {
                return i;
            }
        }

        throw new InvalidOperationException("No matching op in the log.");
    }
}
