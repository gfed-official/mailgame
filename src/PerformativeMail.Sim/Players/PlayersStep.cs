using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Players' phase 3 on the host (ADR-0003 section 2, ADR-0013 section 1, ADR-0014 section 6; GDD P2; player-character story 019).
/// First it ingests the tick's latched <see cref="TransformSample"/>s in latch order (slot, then sequence): for an Alive player each
/// one becomes the stored host position (feet, yaw, <see cref="SampleFlags"/>) stamped <see cref="ISampleLatch.ReceivedTick"/>, so
/// the newest wins (<see cref="CounterId.SamplesIngested"/>); the host never steps an Alive carrier. A sample of a body the host
/// authors is ignored (<see cref="CounterId.SamplesIgnoredAuthored"/>). Then, in ascending slot order, it steps each host-authored
/// body with the same <see cref="OwnedCarrier.Step"/> and a zero <see cref="CarrierInput"/> with <c>HostAuthored</c> (ground drop and
/// depenetration only; <see cref="CounterId.HostCarrierSteps"/>) and stores the authored transform as the host position.
/// Allocation-free; Sim thread only.
/// </summary>
/// <example><code>builder.Bind(PhaseId.PlayersStep, new PlayersStep(players, samples, slots, counters));</code></example>
internal sealed class PlayersStep : ISimPhase
{
    private readonly PlayersSystem _players;
    private readonly ISampleLatch _samples;
    private readonly ISlotView _slots;
    private readonly SimCounters _counters;
    private readonly CarrierInput _hostAuthoredInput = new() { HostAuthored = true };
    private CarrierScratch _scratch;

    public PlayersStep(PlayersSystem players, ISampleLatch samples, ISlotView slots, SimCounters counters)
    {
        _players = players;
        _samples = samples;
        _slots = slots;
        _counters = counters;
        _scratch = new CarrierScratch(players.Settings.MaxCarrierCandidates);
    }

    public void Run(in TickContext ctx)
    {
        Ingest(ctx.Tick);
        StepAuthoredBodies(ctx.Tick);
    }

    /// <summary>
    /// Every non-Alive present player is host-authored: Dead and Frozen per GDD P2, and DeadAway (and the transient Joining) with
    /// them. Evan approved (TD-194 item 3), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019".
    /// </summary>
    private static bool IsHostAuthored(in PlayerRecord r) => r.Life != LifeState.Alive;

    private void Ingest(uint tick)
    {
        uint received = _samples.ReceivedTick;
        for (int i = 0; i < _samples.Count; i++)
        {
            ref readonly TransformSample s = ref _samples[i];
            PlayerSlot slot = PlayerSlot.FromValue(s.Slot);
            ref readonly PlayerRecord r = ref _players.RecordRef(slot);
            if (r.IsEmpty)
            {
                // A slot freed this tick (PlayerLeave in phase 0, after the prologue latched the sample) is a legal race: drop it.
                // Only a slot that was already free is a bug. Evan approved (TD-194 item 4), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019".
                SimAssert.That(!_slots.CanAssign(slot, tick), SimAssertCode.PlayersSampleForEmptySlot);
                continue;
            }

            if (IsHostAuthored(in r))
            {
                _counters.Add(CounterId.SamplesIgnoredAuthored);
                continue;
            }

            ref HostPosition p = ref _players.HostPositions[slot];
            p.Feet = new Vec3Mm(s.PosXMm, s.PosYMm, s.PosZMm);
            p.Yaw = s.YawBam;
            p.Flags = (SampleFlags)s.Flags;
            p.LastReceivedTick = received;
            _counters.Add(CounterId.SamplesIngested);
        }
    }

    private void StepAuthoredBodies(uint tick)
    {
        for (byte v = 0; v < PlayerSlot.Count; v++)
        {
            PlayerSlot slot = PlayerSlot.FromValue(v);
            ref PlayerRecord r = ref _players.RecordRef(slot);
            if (r.IsEmpty || !IsHostAuthored(in r))
            {
                continue;
            }

            CarrierEnv env = _players.CreateCarrierEnv(r.PlayerId);
            OwnedCarrier.Step(ref r.Carrier, in _hostAuthoredInput, in env, ref _scratch);
            CarrierCounterFlush.Flush(_counters, ref _scratch);
            _counters.Add(CounterId.HostCarrierSteps);

            // The host authored this transform, so it is the stored position and it is fresh. Evan approved (TD-194 item 5), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019".
            ref HostPosition p = ref _players.HostPositions[slot];
            p.Feet = new Vec3Mm(r.Carrier.X, r.Carrier.Y, r.Carrier.Z);
            p.Yaw = r.Carrier.BodyYaw;
            p.Flags = r.Carrier.Loco == LocoState.Swimming ? SampleFlags.Swimming : SampleFlags.None;
            p.LastReceivedTick = tick;
        }
    }
}
