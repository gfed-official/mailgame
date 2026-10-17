using PerformativeMail.Client.Core.Input;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 006 AC "Chord resolution": most-specific-chord-wins over the built-in map, the plain action does not
/// also fire, extra held modifiers never block a plainer binding, and unbound inputs resolve to nothing.
/// </summary>
public class ChordResolverTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    [Fact]
    public void ShiftEnter_InPanel_IsQuickMove_AndPlainEnterIsPickPlace()
    {
        Assert.Equal(ActionId.QuickMove, ChordResolver.Resolve(ModifierMask.Shift, KeyEnter, PanelId, _map));
        Assert.Equal(ActionId.PickPlace, ChordResolver.Resolve(ModifierMask.None, KeyEnter, PanelId, _map));
    }

    [Fact]
    public void NumpadEnter_IsTheEnterAlias_ForTheChordAndThePlainBinding()
    {
        Assert.Equal(ActionId.QuickMove, ChordResolver.Resolve(ModifierMask.Shift, KeyNumpadEnter, PanelId, _map));
        Assert.Equal(ActionId.PickPlace, ChordResolver.Resolve(ModifierMask.None, KeyNumpadEnter, PanelId, _map));
    }

    [Fact]
    public void ShiftLmb_InPanel_IsQuickMove_AndPlainLmbIsPickPlace()
    {
        Assert.Equal(ActionId.QuickMove, ChordResolver.Resolve(ModifierMask.Shift, MouseLeft, PanelId, _map));
        Assert.Equal(ActionId.PickPlace, ChordResolver.Resolve(ModifierMask.None, MouseLeft, PanelId, _map));
    }

    [Fact]
    public void ShiftLmb_InHoldingOpen_FallsBackToPickPlace_BecauseQuickMoveIsNotLive()
    {
        Assert.Equal(ActionId.PickPlace, ChordResolver.Resolve(ModifierMask.Shift, MouseLeft, HoldingOpenId, _map));
    }

    [Fact]
    public void ShiftHeldForSprint_InWorld_NeverBlocksE()
    {
        Assert.Equal(ActionId.Sprint, ChordResolver.Resolve(ModifierMask.None, KeyShift, WorldId, _map));
        Assert.Equal(ActionId.Interact, ChordResolver.Resolve(ModifierMask.Shift, KeyE, WorldId, _map));
        Assert.Equal(ActionId.Tool, ChordResolver.Resolve(ModifierMask.Shift, MouseLeft, WorldId, _map));
    }

    [Fact]
    public void BackLadderSteps_ResolveToBack_InEveryState()
    {
        foreach (ushort state in ReachableSet.Default.Ids.ToArray())
        {
            Assert.Equal(ActionId.Back, _map.Chords.Resolve(ModifierMask.None, KeyEscape, state));
            Assert.Equal(ActionId.Back, _map.Chords.Resolve(ModifierMask.None, PadB, state));
            Assert.Equal(ActionId.Pause, _map.Chords.Resolve(ModifierMask.None, PadMenu, state));
        }
    }

    [Fact]
    public void LiveStateDecides_DisjointSharesResolvePerState()
    {
        Assert.Equal(ActionId.Jump, _map.Chords.Resolve(ModifierMask.None, PadA, WorldId));
        Assert.Equal(ActionId.PickPlace, _map.Chords.Resolve(ModifierMask.None, PadA, PanelId));
        Assert.Equal(ActionId.OpenPanel, _map.Chords.Resolve(ModifierMask.None, KeyTab, HoldingClosedId));
        Assert.Equal(ActionId.ClosePanel, _map.Chords.Resolve(ModifierMask.None, KeyTab, HoldingOpenId));
        Assert.Equal(ActionId.Throw, _map.Chords.Resolve(ModifierMask.None, MouseRight, WorldAimingId));
        Assert.Equal(ActionId.None, _map.Chords.Resolve(ModifierMask.None, KeyE, WorldAimingId));
    }

    [Fact]
    public void UnboundOrUnknownInputs_AndInvalidStates_ResolveToNone()
    {
        Assert.Equal(ActionId.None, _map.Chords.Resolve(ModifierMask.None, KeyF, WorldId));
        Assert.Equal(ActionId.None, _map.Chords.Resolve(ModifierMask.None, PhysicalInput.None, WorldId));
        Assert.Equal(ActionId.None, _map.Chords.Resolve(ModifierMask.None, (PhysicalInput)0xFF00, WorldId));
        Assert.Equal(ActionId.None, _map.Chords.Resolve(ModifierMask.None, KeyE, 5000));
    }

    [Fact]
    public void TryResolve_ReportsTheMatchedModifiers_ForModifierConsumption()
    {
        Assert.True(_map.Chords.TryResolve(ModifierMask.Shift, KeyEnter, PanelId, out ChordMatch chord));
        Assert.Equal(ActionId.QuickMove, chord.Action);
        Assert.Equal(ModifierMask.Shift, chord.Modifiers);
        Assert.False(chord.HasLadderStep);

        Assert.True(_map.Chords.TryResolve(ModifierMask.Shift, KeyE, WorldId, out ChordMatch plain));
        Assert.Equal(ActionId.Interact, plain.Action);
        Assert.Equal(ModifierMask.None, plain.Modifiers);
    }

    [Fact]
    public void TryResolve_ForBack_NamesTheLadderStepBoundOnThatInput()
    {
        Assert.True(_map.Chords.TryResolve(ModifierMask.None, KeyEscape, WorldId, out ChordMatch esc));
        Assert.Equal(ActionId.Back, esc.Action);
        Assert.Equal(ActionId.Pause, esc.LadderStepOwner);

        Assert.True(_map.Chords.TryResolve(ModifierMask.None, PadB, PanelId, out ChordMatch bPanel));
        Assert.Equal(ActionId.ClosePanel, bPanel.LadderStepOwner);

        Assert.True(_map.Chords.TryResolve(ModifierMask.None, PadB, HoldingOpenId, out ChordMatch bHolding));
        Assert.Equal(ActionId.CancelReturn, bHolding.LadderStepOwner);

        Assert.True(_map.Chords.TryResolve(ModifierMask.None, PadB, WorldId, out ChordMatch bWorld));
        Assert.Equal(ActionId.Back, bWorld.Action);
        Assert.False(bWorld.HasLadderStep);
    }

    [Fact]
    public void TryResolve_Unbound_ReturnsFalseAndDefault()
    {
        Assert.False(_map.Chords.TryResolve(ModifierMask.None, KeyF, WorldId, out ChordMatch m));
        Assert.Equal(ActionId.None, m.Action);
        Assert.Equal(ModifierMask.None, m.Modifiers);
    }

    [Fact]
    public void Index_ListsBoundKeysAscending_WithTheirModifiers()
    {
        ReadOnlySpan<PhysicalInput> keys = _map.Chords.Keys;
        for (int i = 1; i < keys.Length; i++)
        {
            Assert.True(keys[i - 1] < keys[i]);
        }

        Assert.Equal(ModifierMask.Shift, _map.Chords.ModifiersOn(KeyEnter));
        Assert.Equal(ModifierMask.Shift, _map.Chords.ModifiersOn(MouseLeft));
        Assert.Equal(ModifierMask.None, _map.Chords.ModifiersOn(KeyE));
    }
}

/// <summary>Story 006 guardrail: <see cref="ChordResolver.Resolve(ModifierMask, PhysicalInput, ushort)"/> is 0 B.</summary>
[Collection(NonParallelCollection.Name)]
public class ChordResolverAllocationTests
{
    [Fact]
    public void Resolve_AllocatesNothing()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();
        ushort[] states = ReachableSet.Default.Ids.ToArray();
        PhysicalInput[] keys = map.Chords.Keys.ToArray();
        int sink = 0;

        _ = AllocAssert.Zero("ChordResolver.Resolve over every reachable state x bound key x {None, Shift}", () =>
        {
            foreach (ushort s in states)
            {
                foreach (PhysicalInput k in keys)
                {
                    sink += ChordResolver.Resolve(ModifierMask.None, k, s, map);
                    sink += map.Chords.Resolve(ModifierMask.Shift, k, s);
                }
            }
        });

        Assert.True(sink > 0);
    }

    [Fact]
    public void TryResolve_AllocatesNothing()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();
        ushort[] states = ReachableSet.Default.Ids.ToArray();
        PhysicalInput[] keys = map.Chords.Keys.ToArray();
        int sink = 0;

        _ = AllocAssert.Zero("ChordResolver.TryResolve over every reachable state x bound key x {None, Shift}", () =>
        {
            foreach (ushort s in states)
            {
                foreach (PhysicalInput k in keys)
                {
                    if (map.Chords.TryResolve(ModifierMask.None, k, s, out ChordMatch a))
                    {
                        sink += a.Action + a.LadderStepOwner;
                    }

                    if (map.Chords.TryResolve(ModifierMask.Shift, k, s, out ChordMatch b))
                    {
                        sink += b.Action + (int)b.Modifiers;
                    }
                }
            }
        });

        Assert.True(sink > 0);
    }
}
