using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 004 AC "Predicates": <see cref="LivePredicate"/> semantics (a state is live iff its bit is set in every
/// mask), the GDD named shorthands accept and reject exactly the expected tuples among the reachable set, and
/// <c>IsLive</c> allocates 0 B. Non-parallel because of the allocation test.
/// </summary>
[Collection(NonParallelCollection.Name)]
[Trait("Id", "TR-input-003")]
public class LivePredicateTests
{
    private static ushort Pack(
        CaptureLayer capture = CaptureLayer.None,
        OverlayLayer overlay = OverlayLayer.None,
        BodyLayer body = BodyLayer.OnFoot,
        bool holding = false,
        bool aiming = false,
        SubmenuLayer submenu = SubmenuLayer.None) =>
        RouterStateInfo.Pack(capture, overlay, body, holding, aiming, submenu);

    private static RouterState Decode(ushort id)
    {
        Assert.True(RouterState.TryFromId(id, out RouterState state));
        return state;
    }

    // ------------------------------------------------------------ mask constants

    [Fact]
    public void LayerMasks_HaveOneBitPerEnumValueAndAllIsTheirUnion()
    {
        Assert.Equal(0b111, CaptureMask.None | CaptureMask.TextEntry | CaptureMask.Pause);
        Assert.Equal(CaptureMask.All, CaptureMask.None | CaptureMask.TextEntry | CaptureMask.Pause);
        Assert.Equal(
            OverlayMask.All,
            OverlayMask.None | OverlayMask.Panel | OverlayMask.Map | OverlayMask.Station | OverlayMask.Draft | OverlayMask.Results | OverlayMask.History);
        Assert.Equal(BodyMask.All, BodyMask.OnFoot | BodyMask.Build | BodyMask.Mounted | BodyMask.Dead);
        Assert.Equal(HoldingMask.All, HoldingMask.No | HoldingMask.Yes);
        Assert.Equal(AimingMask.All, AimingMask.No | AimingMask.Yes);
        Assert.Equal(SubmenuMask.All, SubmenuMask.None | SubmenuMask.ActionMenu | SubmenuMask.SplitStepper);
        Assert.Equal(1 << (int)OverlayLayer.History, OverlayMask.History);
        Assert.Equal(1 << (int)BodyLayer.Dead, BodyMask.Dead);
    }

    // ------------------------------------------------------------ IsLive semantics

    [Fact]
    public void AnyPredicate_IsLiveForEveryValidIdAndNoInvalidId()
    {
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            Assert.True(LivePredicate.Any.IsLive((ushort)i));
        }

        Assert.False(LivePredicate.Any.IsLive(RouterStateInfo.StateCount));
        Assert.False(LivePredicate.Any.IsLive(ushort.MaxValue));
    }

    [Fact]
    public void DefaultPredicate_IsLiveForNothing()
    {
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            Assert.False(default(LivePredicate).IsLive((ushort)i));
        }
    }

    [Fact]
    public void Predicate_IsLiveOnlyWhenEveryMaskContainsTheStatesBit()
    {
        LivePredicate p = LivePredicate.Any with { OverlayMask = OverlayMask.Panel | OverlayMask.Map, BodyMask = BodyMask.OnFoot };

        Assert.True(p.IsLive(Pack(overlay: OverlayLayer.Panel)));
        Assert.True(p.IsLive(Pack(overlay: OverlayLayer.Map)));
        Assert.False(p.IsLive(Pack(overlay: OverlayLayer.Station)));
        Assert.False(p.IsLive(Pack(overlay: OverlayLayer.Panel, body: BodyLayer.Mounted)));
    }

    [Fact]
    public void EachMask_GatesLivenessOnItsOwnLayerAlone()
    {
        // World with the layer's mask emptied: nothing is live, whatever the other layers hold.
        (string Layer, LivePredicate P)[] emptied =
        [
            ("capture", LivePredicate.Any with { CaptureMask = 0 }),
            ("overlay", LivePredicate.Any with { OverlayMask = 0 }),
            ("body", LivePredicate.Any with { BodyMask = 0 }),
            ("holding", LivePredicate.Any with { HoldingMask = 0 }),
            ("aiming", LivePredicate.Any with { AimingMask = 0 }),
            ("submenu", LivePredicate.Any with { SubmenuMask = 0 }),
        ];

        foreach ((string layer, LivePredicate p) in emptied)
        {
            for (int i = 0; i < RouterStateInfo.StateCount; i++)
            {
                Assert.False(p.IsLive((ushort)i), layer + " mask emptied, state " + i);
            }
        }
    }

    [Fact]
    public void StaticAndInstanceAndStateOverloads_Agree()
    {
        LivePredicate p = NamedStates.HoldingOpen;

        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            ushort id = (ushort)i;
            RouterState state = Decode(id);
            bool expected = p.IsLive(id);

            Assert.Equal(expected, LivePredicate.IsLive(in p, id));
            Assert.Equal(expected, p.IsLive(in state));
        }
    }

    [Fact]
    public void StateOverload_NeverMatchesAnOutOfRangeEnumValue()
    {
        var bogus = new RouterState((CaptureLayer)200, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);

        Assert.False(LivePredicate.Any.IsLive(in bogus));
    }

    [Fact]
    public void Equality_IsByAllSixMasks()
    {
        LivePredicate a = LivePredicate.Any with { OverlayMask = OverlayMask.Panel };
        LivePredicate same = LivePredicate.Any with { OverlayMask = OverlayMask.Panel };

        Assert.True(a == same);
        Assert.True(a.Equals((object)same));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.True(a != LivePredicate.Any);
        Assert.True(a != (a with { SubmenuMask = SubmenuMask.None }));
        Assert.False(a.Equals("x"));
    }

    // ------------------------------------------------------------ named shorthands

    public enum Shorthand
    {
        World,
        WorldNotAiming,
        Panel,
        HoldingOpen,
        HoldingClosed,
    }

    private static LivePredicate PredicateOf(Shorthand s) => s switch
    {
        Shorthand.World => NamedStates.World,
        Shorthand.WorldNotAiming => NamedStates.WorldNotAiming,
        Shorthand.Panel => NamedStates.Panel,
        Shorthand.HoldingOpen => NamedStates.HoldingOpen,
        Shorthand.HoldingClosed => NamedStates.HoldingClosed,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    /// <summary>The GDD R2 sentence for each shorthand, written as branches over the enums (the oracle).</summary>
    private static bool Expected(Shorthand s, RouterState t) => s switch
    {
        Shorthand.World => t.Capture == CaptureLayer.None && t.Overlay == OverlayLayer.None && t.Body == BodyLayer.OnFoot && !t.Holding,
        Shorthand.WorldNotAiming => t.Capture == CaptureLayer.None && t.Overlay == OverlayLayer.None && t.Body == BodyLayer.OnFoot && !t.Holding && !t.Aiming,
        Shorthand.Panel => t.Overlay == OverlayLayer.Panel && !t.Holding,
        Shorthand.HoldingOpen => t.Overlay == OverlayLayer.Panel && t.Holding,
        Shorthand.HoldingClosed => t.Overlay == OverlayLayer.None && t.Body == BodyLayer.OnFoot && t.Holding,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    [Theory]
    [InlineData(Shorthand.World, 2)]
    [InlineData(Shorthand.WorldNotAiming, 1)]
    [InlineData(Shorthand.Panel, 9)]
    [InlineData(Shorthand.HoldingOpen, 9)]
    [InlineData(Shorthand.HoldingClosed, 3)]
    public void Shorthand_AcceptsAndRejectsExactlyTheExpectedReachableTuples(Shorthand shorthand, int expectedReachableMatches)
    {
        LivePredicate predicate = PredicateOf(shorthand);
        int accepted = 0;

        foreach (ushort id in ReachableSet.Default.Ids.ToArray())
        {
            RouterState state = Decode(id);
            bool live = predicate.IsLive(id);

            Assert.True(Expected(shorthand, state) == live, $"{shorthand} on {state.ToDebugString()}: expected {Expected(shorthand, state)}, got {live}");
            if (live)
            {
                accepted++;
            }
        }

        Assert.Equal(expectedReachableMatches, accepted);
    }

    [Fact]
    public void WorldPanelAndHolding_ArePairwiseDisjointOverTheTupleSpace()
    {
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            ushort id = (ushort)i;
            int matches = (NamedStates.World.IsLive(id) ? 1 : 0)
                + (NamedStates.Panel.IsLive(id) ? 1 : 0)
                + (NamedStates.HoldingOpen.IsLive(id) ? 1 : 0)
                + (NamedStates.HoldingClosed.IsLive(id) ? 1 : 0);

            Assert.True(matches <= 1, RouterState.ToDebugString(id) + " matches more than one of World / Panel / Holding-open / Holding-closed");
        }
    }

    // ------------------------------------------------------------ allocation

    [Fact]
    public void IsLive_AllocatesNothing()
    {
        // Warm-up (inside AllocAssert) builds the row table and JITs the loop.
        LivePredicate predicate = NamedStates.WorldNotAiming;
        ushort[] ids = [Pack(), Pack(aiming: true), Pack(overlay: OverlayLayer.Panel), Pack(holding: true), RouterStateInfo.StateCount];
        int live = 0;

        _ = AllocAssert.Zero("LivePredicate.IsLive / RouterState.IsReachable", () =>
        {
            live = 0;
            for (int rep = 0; rep < 200; rep++)
            {
                foreach (ushort id in ids)
                {
                    if (predicate.IsLive(id) || LivePredicate.IsLive(in NamedStates.Panel, id) || RouterState.IsReachable(id))
                    {
                        live++;
                    }
                }
            }
        });

        Assert.True(live > 0);
    }
}
