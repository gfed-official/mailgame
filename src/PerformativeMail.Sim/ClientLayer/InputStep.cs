using System.Runtime.CompilerServices;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// A saturated step count for one action (mouse-wheel notches, repeat steps): <see cref="N"/> is a signed byte in
/// -127 to 127 (ADR-0012 section 4). Two bytes, no references.
/// </summary>
/// <example><code>InputStep s = frame.Steps[0];   // s.Action == FrameAction.Jump, s.N == 3</code></example>
public readonly struct InputStep
{
    /// <summary>Creates a step entry.</summary>
    /// <param name="action">The action the steps belong to.</param>
    /// <param name="n">The saturated signed step count.</param>
    public InputStep(FrameAction action, sbyte n)
    {
        Action = action;
        N = n;
    }

    /// <summary>The action the steps belong to.</summary>
    public FrameAction Action { get; }

    /// <summary>The saturated signed step count (-127 to 127).</summary>
    public sbyte N { get; }
}

/// <summary>Fixed-capacity inline storage for <see cref="InputStep"/>s.</summary>
[InlineArray(InputFrame.MaxSteps)]
public struct InputStepBuffer
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
#pragma warning disable IDE0051 // Remove unused private members
    private InputStep _element;
#pragma warning restore IDE0051
#pragma warning restore CS0169
}
