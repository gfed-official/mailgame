using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// What <see cref="ICatchPredictor.Evaluate"/> returns (ADR-0015 section 8 return fields). Values only, a <c>byte</c> flag, no
/// reference. <c>default</c> predicts nothing. PROVISIONAL shape: the throwing-catching epic owns it.
/// </summary>
/// <example><code>var p = new CatchPrediction { Predicted = 1, FlightId = 7, Units = 1, PredictedStaticReceiver = mailbox };</code></example>
[StructLayout(LayoutKind.Sequential)]
public readonly struct CatchPrediction
{
    /// <summary>1 when a non-own flight enters the catch sphere on this tick, else 0.</summary>
    public byte Predicted { get; init; }

    /// <summary>The predicted flight.</summary>
    public ushort FlightId { get; init; }

    /// <summary>Its thrower.</summary>
    public PlayerSlot Thrower { get; init; }

    /// <summary>The units in flight.</summary>
    public byte Units { get; init; }

    /// <summary>The static receiver the flight would land in uncaught, or <see cref="AnchorRef.None"/> (the D3 gate compares it with the presented target).</summary>
    public AnchorRef PredictedStaticReceiver { get; init; }
}
