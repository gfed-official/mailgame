using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests;

/// <summary>Covers <see cref="RouterStateInfo"/> (input-controls story 002 AC-5): the 1008-tuple pack/unpack round trip, rejection, predicates.</summary>
public class RouterStateInfoTests
{
    private static IEnumerable<(CaptureLayer C, OverlayLayer O, BodyLayer B, bool H, bool A, SubmenuLayer S)> AllTuples()
    {
        for (int c = 0; c < 3; c++)
        {
            for (int o = 0; o < 7; o++)
            {
                for (int b = 0; b < 4; b++)
                {
                    for (int h = 0; h < 2; h++)
                    {
                        for (int a = 0; a < 2; a++)
                        {
                            for (int s = 0; s < 3; s++)
                            {
                                yield return ((CaptureLayer)c, (OverlayLayer)o, (BodyLayer)b, h == 1, a == 1, (SubmenuLayer)s);
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void EnumRanges_Match3x7x4x2x2x3()
    {
        Assert.Equal(3, Enum.GetValues<CaptureLayer>().Length);
        Assert.Equal(7, Enum.GetValues<OverlayLayer>().Length);
        Assert.Equal(4, Enum.GetValues<BodyLayer>().Length);
        Assert.Equal(3, Enum.GetValues<SubmenuLayer>().Length);
        Assert.Equal(1008, 3 * 7 * 4 * 2 * 2 * 3);
        Assert.Equal(1008, RouterStateInfo.StateCount);
        Assert.Equal(
            new[] { "None", "Panel", "Map", "Station", "Draft", "Results", "History" },
            Enum.GetNames<OverlayLayer>());
        Assert.Equal(new[] { "None", "TextEntry", "Pause" }, Enum.GetNames<CaptureLayer>());
        Assert.Equal(new[] { "OnFoot", "Build", "Mounted", "Dead" }, Enum.GetNames<BodyLayer>());
        Assert.Equal(new[] { "None", "ActionMenu", "SplitStepper" }, Enum.GetNames<SubmenuLayer>());
    }

    [Fact]
    public void PackUnpack_AllTuples_RoundTripToDistinctIdsBelow1008InFormulaOrder()
    {
        int expectedId = 0;
        foreach ((CaptureLayer c, OverlayLayer o, BodyLayer b, bool h, bool a, SubmenuLayer s) in AllTuples())
        {
            ushort id = RouterStateInfo.Pack(c, o, b, h, a, s);

            // The documented formula, written out independently of the implementation.
            int formula = ((((((int)c * 7) + (int)o) * 4 + (int)b) * 2 + (h ? 1 : 0)) * 2 + (a ? 1 : 0)) * 3 + (int)s;
            Assert.Equal(formula, id);
            Assert.Equal(expectedId, id);
            Assert.True(RouterStateInfo.IsValid(id));

            Assert.True(RouterStateInfo.Unpack(id, out CaptureLayer c2, out OverlayLayer o2, out BodyLayer b2, out bool h2, out bool a2, out SubmenuLayer s2));
            Assert.Equal((c, o, b, h, a, s), (c2, o2, b2, h2, a2, s2));
            expectedId++;
        }

        Assert.Equal(1008, expectedId);
    }

    [Fact]
    public void Unpack_IdsFrom1008Up_AreRejectedWithDefaultedOutputs()
    {
        foreach (ushort id in new ushort[] { 1008, 1009, 2000, 32768, ushort.MaxValue })
        {
            bool ok = RouterStateInfo.Unpack(id, out CaptureLayer c, out OverlayLayer o, out BodyLayer b, out bool h, out bool a, out SubmenuLayer s);

            Assert.False(ok);
            Assert.False(RouterStateInfo.IsValid(id));
            Assert.Equal((CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None), (c, o, b, h, a, s));
            Assert.False(RouterStateInfo.IsPanel(id));
            Assert.False(RouterStateInfo.IsAiming(id));
            Assert.False(RouterStateInfo.IsCaptured(id));
            Assert.False(RouterStateInfo.IsHolding(id));
        }

        Assert.True(RouterStateInfo.IsValid(1007));
    }

    [Fact]
    public void Predicates_ReadTheirOwnLayerOnly_AcrossAllTuples()
    {
        foreach ((CaptureLayer c, OverlayLayer o, BodyLayer b, bool h, bool a, SubmenuLayer s) in AllTuples())
        {
            ushort id = RouterStateInfo.Pack(c, o, b, h, a, s);

            Assert.Equal(o == OverlayLayer.Panel, RouterStateInfo.IsPanel(id));
            Assert.Equal(a, RouterStateInfo.IsAiming(id));
            Assert.Equal(c != CaptureLayer.None, RouterStateInfo.IsCaptured(id));
            Assert.Equal(h, RouterStateInfo.IsHolding(id));
        }
    }

    [Fact]
    public void Id0_IsTheQuietOnFootState()
    {
        ushort id = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);

        Assert.Equal((ushort)0, id);
        Assert.Equal((ushort)1007, RouterStateInfo.Pack(CaptureLayer.Pause, OverlayLayer.History, BodyLayer.Dead, true, true, SubmenuLayer.SplitStepper));
    }

    [Fact]
    public void Pack_OutOfRangeLayerValue_AssertsInDebugAndReturnsInvalidIdInRelease()
    {
        var bad = (OverlayLayer)7;

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(
                () => RouterStateInfo.Pack(CaptureLayer.None, bad, BodyLayer.OnFoot, false, false, SubmenuLayer.None));
            Assert.Equal(SimAssertCode.RouterStateLayerRange, ex.Code);
        }
        else
        {
            Assert.Equal(RouterStateInfo.InvalidId, RouterStateInfo.Pack(CaptureLayer.None, bad, BodyLayer.OnFoot, false, false, SubmenuLayer.None));
            Assert.Equal(RouterStateInfo.InvalidId, RouterStateInfo.Pack((CaptureLayer)3, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None));
            Assert.Equal(RouterStateInfo.InvalidId, RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, (BodyLayer)4, false, false, SubmenuLayer.None));
            Assert.Equal(RouterStateInfo.InvalidId, RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, false, (SubmenuLayer)3));
        }

        Assert.False(RouterStateInfo.IsValid(RouterStateInfo.InvalidId));
    }
}
