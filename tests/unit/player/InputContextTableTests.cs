using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 013, PCM-P14-1 and the Contexts AC: the verb x context table against an independently written copy of
/// the GDD P14 table (design/gdd/player-character-movement.md P14), the '—' = Absent encoding, Respawn bound nowhere, the
/// tap+hold rule, <see cref="InputContexts.CanOpenPanel"/> and Build = World.
/// </summary>
public class InputContextTableTests
{
    // The GDD P14 table as written in the GDD, one row per verb, columns World | Panel | Holding | Mounted | Dead.
    // Each GDD cell text is mapped to a CellState by GddCell below (story 013 encoding, 2026-10-20).
    private static readonly string[][] _gddP14 =
    {
        new[] { "✓", "✓", "✓", "vehicle's controls (Vehicles)", "—" },                                    // Move
        new[] { "✓", "mouse: suspended while the cursor is in UI", "✓", "orbit", "— (camera auto-orbits your pile)" }, // Look
        new[] { "✓", "— (walk; Shift is quick-move)", "✓", "Vehicles may reuse (bike sprint)", "—" },           // Sprint
        new[] { "✓", "disabled", "✓", "—", "—" },                                                          // Jump
        new[] { "✓", "disabled", "✓ (panel closed)", "Dismount only", "—" },                               // Interact
        new[] { "✓", "—", "✓", "✓", "—" },                                                                 // Shoulder swap
        new[] { "✓", "—", "✓", "✓", "✓ (pings anchor at your pile, Say; emotes greyed)" },                  // Comms wheel
        new[] { "—", "—", "—", "—", "—" },                                                                 // Respawn
    };

    private static CellState GddCell(string text)
    {
        if (text.StartsWith("—", StringComparison.Ordinal) || text.StartsWith("Vehicles may reuse", StringComparison.Ordinal))
        {
            return CellState.Absent; // '—' is Absent; Mounted sprint ("Vehicles may reuse") is Absent at M-1a.
        }

        if (text == "disabled")
        {
            return CellState.Disabled;
        }

        if (text.Contains("emotes greyed", StringComparison.Ordinal))
        {
            return CellState.Greyed;
        }

        return CellState.Live; // ✓, vehicle's controls, orbit, suspended in UI, Dismount only, panel closed.
    }

    /// <summary>Every one of the 40 cells equals the GDD table; all mismatches are reported together.</summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void EveryCell_MatchesTheGddP14Table()
    {
        // Arrange
        var mismatches = new List<string>();

        // Act
        for (int v = 0; v < 8; v++)
        {
            for (int c = 0; c < 5; c++)
            {
                CellState expected = GddCell(_gddP14[v][c]);
                CellState actual = VerbContextTable.Cell((ContextVerb)v, (InputContext)c);
                if (expected != actual)
                {
                    mismatches.Add($"{(ContextVerb)v} x {(InputContext)c}: expected {expected}, table {actual}");
                }
            }
        }

        // Assert
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>The matrix is exactly 8 verbs x 5 contexts, and the enums have exactly those members.</summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void Table_IsEightVerbsByFiveContexts()
    {
        // Act
        int cells = VerbContextTable.Cells.Length;

        // Assert
        Assert.Equal(40, cells);
        Assert.Equal(8, Enum.GetValues<ContextVerb>().Length);
        Assert.Equal(5, Enum.GetValues<InputContext>().Length);
        Assert.Equal(VerbContextTable.VerbCount * VerbContextTable.ContextCount, cells);
    }

    /// <summary>The named GDD cells, spelled out: '—' cells are Absent, "disabled" Disabled, "emotes greyed" Greyed.</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(ContextVerb.Jump, InputContext.Panel, CellState.Disabled)]
    [InlineData(ContextVerb.Interact, InputContext.Panel, CellState.Disabled)]
    [InlineData(ContextVerb.Interact, InputContext.Mounted, CellState.Live)]
    [InlineData(ContextVerb.Interact, InputContext.Dead, CellState.Absent)]
    [InlineData(ContextVerb.Look, InputContext.Dead, CellState.Absent)]
    [InlineData(ContextVerb.Look, InputContext.Panel, CellState.Live)]
    [InlineData(ContextVerb.Sprint, InputContext.Panel, CellState.Absent)]
    [InlineData(ContextVerb.Sprint, InputContext.Mounted, CellState.Absent)]
    [InlineData(ContextVerb.Sprint, InputContext.Dead, CellState.Absent)]
    [InlineData(ContextVerb.CommsWheel, InputContext.Panel, CellState.Absent)]
    [InlineData(ContextVerb.CommsWheel, InputContext.Dead, CellState.Greyed)]
    [InlineData(ContextVerb.Move, InputContext.Mounted, CellState.Live)]
    public void NamedCells_UseTheStoryEncoding(ContextVerb verb, InputContext context, CellState expected)
    {
        // Act
        CellState actual = VerbContextTable.Cell(verb, context);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>The comms wheel is usable (Live or Greyed) in every context except Panel.</summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void CommsWheel_IsUsableEverywhereExceptPanel()
    {
        foreach (InputContext c in Enum.GetValues<InputContext>())
        {
            // Act
            CellState cell = VerbContextTable.Cell(ContextVerb.CommsWheel, c);

            // Assert
            Assert.Equal(c != InputContext.Panel, cell is CellState.Live or CellState.Greyed);
        }
    }

    /// <summary>Respawn is automatic: no context enables it, and it carries no press.</summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void Respawn_IsBoundInNoContext()
    {
        foreach (InputContext c in Enum.GetValues<InputContext>())
        {
            // Act + Assert
            Assert.Equal(CellState.Absent, VerbContextTable.Cell(ContextVerb.Respawn, c));
        }

        Assert.Equal(VerbPress.None, VerbContextTable.Presses(ContextVerb.Respawn));
    }

    /// <summary>
    /// In no context does a bound (non-Absent: Live, Disabled or Greyed) verb carry both a tap and a hold, except Interact. Every one
    /// of the 8 x 5 cells is visited; a Disabled cell still occupies its binding, so it counts.
    /// </summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void TapAndHold_OnlyInteractCarriesBoth_InAnyContext()
    {
        // Arrange
        var offenders = new List<string>();
        int visited = 0;
        int interactBound = 0;

        // Act
        foreach (InputContext c in Enum.GetValues<InputContext>())
        {
            foreach (ContextVerb v in Enum.GetValues<ContextVerb>())
            {
                visited++;
                bool present = VerbContextTable.Cell(v, c) != CellState.Absent;
                bool both = (VerbContextTable.Presses(v) & (VerbPress.Tap | VerbPress.Hold)) == (VerbPress.Tap | VerbPress.Hold);
                if (present && both && v == ContextVerb.Interact)
                {
                    interactBound++;
                }
                else if (present && both)
                {
                    offenders.Add($"{v} in {c}");
                }
            }
        }

        // Assert
        Assert.Equal(40, visited);
        Assert.Empty(offenders);
        Assert.Equal(4, interactBound);   // World, Panel (disabled), Holding, Mounted (Dismount only); absent in Dead
    }

    /// <summary>
    /// The press-kind declaration the tap+hold rule reads (<see cref="VerbContextTable.Presses"/>), pinned verb by verb so the rule's
    /// input cannot drift unseen. Comms wheel = Hold is this story's declaration (TD-123: Input &amp; Controls to confirm).
    /// </summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void Presses_DeclarationIsPinned()
    {
        // Arrange
        (ContextVerb Verb, VerbPress Press)[] expected =
        [
            (ContextVerb.Move, VerbPress.None),
            (ContextVerb.Look, VerbPress.None),
            (ContextVerb.Sprint, VerbPress.Hold),
            (ContextVerb.Jump, VerbPress.Tap),
            (ContextVerb.Interact, VerbPress.Tap | VerbPress.Hold),
            (ContextVerb.ShoulderSwap, VerbPress.Tap),
            (ContextVerb.CommsWheel, VerbPress.Hold),
            (ContextVerb.Respawn, VerbPress.None),
        ];

        // Act + Assert
        Assert.Equal(expected.Length, Enum.GetValues<ContextVerb>().Length);
        foreach ((ContextVerb verb, VerbPress press) in expected)
        {
            Assert.Equal(press, VerbContextTable.Presses(verb));
        }
    }

    /// <summary>Out-of-range rows and columns read as Absent / no press (never out of bounds).</summary>
    [Fact]
    [Trait("Id", "PCM-P14-1")]
    public void OutOfRange_IsAbsent()
    {
        // Act + Assert
        Assert.Equal(CellState.Absent, VerbContextTable.Cell((ContextVerb)8, InputContext.World));
        Assert.Equal(CellState.Absent, VerbContextTable.Cell(ContextVerb.Move, (InputContext)5));
        Assert.Equal(VerbPress.None, VerbContextTable.Presses((ContextVerb)200));
    }

    /// <summary>The Panel cannot open while Mounted or Dead.</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(BodyContext.OnFoot, true)]
    [InlineData(BodyContext.Build, true)]
    [InlineData(BodyContext.Mounted, false)]
    [InlineData(BodyContext.Dead, false)]
    public void CanOpenPanel_FalseWhileMountedOrDead(BodyContext body, bool expected)
    {
        // Act
        bool actual = InputContexts.CanOpenPanel(body);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>The status to table-column mapping; Build reads as World (Holding with a cursor item), Panel wins over Holding.</summary>
    [Theory]
    [Trait("Id", "PCM-P14-1")]
    [InlineData(BodyContext.OnFoot, false, false, InputContext.World)]
    [InlineData(BodyContext.Build, false, false, InputContext.World)]
    [InlineData(BodyContext.OnFoot, false, true, InputContext.Holding)]
    [InlineData(BodyContext.Build, false, true, InputContext.Holding)]
    [InlineData(BodyContext.OnFoot, true, false, InputContext.Panel)]
    [InlineData(BodyContext.OnFoot, true, true, InputContext.Panel)]
    [InlineData(BodyContext.Mounted, false, true, InputContext.Mounted)]
    [InlineData(BodyContext.Dead, true, true, InputContext.Dead)]
    public void InputContextOf_MapsBodyPanelAndCursor(BodyContext body, bool panel, bool holding, InputContext expected)
    {
        // Arrange
        var s = new CarrierStatus { Life = LifeState.Alive, Body = body, PanelOpen = panel, CursorHolding = holding };

        // Act
        InputContext actual = InputContexts.Of(in s);

        // Assert
        Assert.Equal(expected, actual);
    }
}
