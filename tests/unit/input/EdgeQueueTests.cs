using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 007: event-time gating, <see cref="InputRouter.FlushToLatch"/> order, <c>ArrivalSeq</c> wrap, and the
/// 256-edge ring that drops the newest Press and never a Release or Cancel.
/// </summary>
public class EdgeQueueTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    private readonly InputTestHarness _h = new();
    private readonly UiEventLog _log = new();
    private readonly InputRouter _router;

    public EdgeQueueTests()
    {
        _router = new InputRouter(_map, InputSettings.Defaults, _h.Clock, _log);
    }

    [Fact]
    public void PressBeforeAStateChange_KeepsItsGatedState_AndFlushPushesEdgesThenStateThenClears()
    {
        // Arrange: Jump and Interact pressed in World, then the panel opens and Shift is pressed (Sprint is not live there).
        var latch = new OrderedLatch();
        Feed(_h.Press(KeySpace));
        Feed(_h.Press(KeyE));
        _router.ForceState(PanelId);
        Feed(_h.Press(KeyShift));

        // Act
        _router.FlushToLatch(latch);
        int firstFlush = latch.Calls.Count;
        _router.FlushToLatch(latch);

        // Assert
        Assert.Equal(
            [
                "edge Jump Press " + WorldId,
                "edge Interact Press " + WorldId,
                "edge Interact Cancel " + PanelId,
                "state held=0 router=" + PanelId,
                "state held=0 router=" + PanelId,
            ],
            latch.Calls);
        Assert.Equal(4, firstFlush);
    }

    [Fact]
    public void ArrivalSeq_WrapsPast65535_WithoutReordering()
    {
        // Arrange: Interact press + release pairs (never coalesced) past one full u16 cycle, flushed every 100 pairs.
        var latch = new SeqCheckingLatch();

        // Act
        for (int pair = 0; pair < 32_800; pair++)
        {
            Feed(_h.Press(KeyE));
            Feed(_h.Release(KeyE));
            if (pair % 100 == 99)
            {
                _router.FlushToLatch(latch);
            }
        }

        _router.FlushToLatch(latch);

        // Assert
        Assert.Equal(65_600, latch.Count);
        Assert.Equal(1, latch.Wraps);
        Assert.Equal(0, latch.OutOfOrder);
    }

    [Fact]
    public void FullRing_DropsTheNewestPress_AndNeverAReleaseOrCancel()
    {
        // Arrange: Interact and Throw aim open (each reserves its closing edge), then Jump presses fill the ring.
        Feed(_h.Press(KeyE));
        Feed(_h.Press(MouseRight));
        for (int i = 0; i < 252; i++)
        {
            Feed(_h.Press(KeySpace));
            Feed(_h.Release(KeySpace));
        }

        long droppedAtCapacity = _router.DroppedEdges;

        // Act: one Press too many, then a state change that cancels both open presses.
        Feed(_h.Press(KeySpace));
        Feed(_h.Release(KeySpace));
        _router.ForceState(PanelId);
        _router.FlushToLatch(_h.Latch);

        // Assert: 2 + 252 + 2 = 256 queued; the 253rd Jump was dropped and logged; both cancels arrived, last.
        Assert.Equal(0, droppedAtCapacity);
        Assert.Equal(1, _router.DroppedEdges);
        UiEventEntry drop = Assert.Single(Entries(), e => e.Kind == LogKind.Dropped);
        Assert.Equal((ushort)FrameAction.Jump, drop.IdA);
        Assert.Equal(
            [(FrameAction.Interact, EdgeKind.Press), (FrameAction.Aim, EdgeKind.Press), (FrameAction.Jump, EdgeKind.Press), (FrameAction.Interact, EdgeKind.Cancel), (FrameAction.Aim, EdgeKind.Cancel)],
            _h.Latch.Edges.Select(e => (e.Action, e.Kind)));
    }

    private IEnumerable<UiEventEntry> Entries()
    {
        for (int i = 0; i < _log.Count; i++)
        {
            _ = _log.TryGet(i, out UiEventEntry e);
            yield return e;
        }
    }

    private void Feed(RawInputRecord r) => _router.Feed(in r);

    /// <summary>Records edges and states as one ordered call list.</summary>
    private sealed class OrderedLatch : IInputLatchWriter
    {
        public List<string> Calls { get; } = [];

        public void PushEdge(in InputEdge e) => Calls.Add($"edge {e.Action} {e.Kind} {e.RouterStateId}");

        public void PushSteps(FrameAction a, int n) => Calls.Add($"steps {a} {n}");

        public void SetState(in InputState s) => Calls.Add($"state held={s.HeldBits} router={s.RouterStateId}");
    }

    /// <summary>Checks that every edge is newer than the one before it (wrap-safe) and counts the u16 wraps.</summary>
    private sealed class SeqCheckingLatch : IInputLatchWriter
    {
        private ushort _last;

        public int Count { get; private set; }

        public int Wraps { get; private set; }

        public int OutOfOrder { get; private set; }

        public void PushEdge(in InputEdge e)
        {
            if (Count > 0)
            {
                OutOfOrder += SerialU16.IsNewer(e.ArrivalSeq, _last) ? 0 : 1;
                Wraps += e.ArrivalSeq < _last ? 1 : 0;
            }

            _last = e.ArrivalSeq;
            Count++;
        }

        public void PushSteps(FrameAction a, int n)
        {
        }

        public void SetState(in InputState s)
        {
        }
    }
}
