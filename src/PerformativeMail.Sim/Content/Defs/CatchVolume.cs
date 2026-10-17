namespace PerformativeMail.Sim.Content;

/// <summary>
/// The box a thrown entry must enter to land in a mailbox (Mail R3, Throwing tests it). Millimetres, with the yaw in
/// milli-degrees. The constructor takes all five, but <c>default</c> / <c>new()</c> yields an all-zero volume, so
/// a zero extent is for the validator (story 018) to reject; the record cannot.
/// </summary>
/// <param name="WidthMm">Box width in millimetres.</param>
/// <param name="HeightMm">Box height in millimetres.</param>
/// <param name="DepthMm">Box depth in millimetres.</param>
/// <param name="CentreHeightMm">Height of the box centre above the mailbox base in millimetres.</param>
/// <param name="YawMilliDeg">Yaw in thousandths of a degree.</param>
/// <example>
/// <code>
/// var house = new CatchVolume(WidthMm: 1000, HeightMm: 800, DepthMm: 1000, CentreHeightMm: 1000, YawMilliDeg: 0);
/// </code>
/// </example>
public readonly record struct CatchVolume(int WidthMm, int HeightMm, int DepthMm, int CentreHeightMm, int YawMilliDeg) : IDefLeaf;
