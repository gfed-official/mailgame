using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.Presentation;

/// <summary>
/// godot-host-shell story 009 AC-3: <see cref="FaceYaw.ToBam"/> turns a view's forward onto the lot's face, under ADR-0013 section 3's
/// <c>forward(θ) = (-sin θ, -cos θ)</c> evaluated with the Sim's own <see cref="TrigQ15"/>.
/// </summary>
public class FaceYawTests
{
    [Theory]
    [Trait("Id", "GHS-009-AC3")]
    [InlineData(FaceDir.PosX, 1, 0)]
    [InlineData(FaceDir.NegX, -1, 0)]
    [InlineData(FaceDir.PosZ, 0, 1)]
    [InlineData(FaceDir.NegZ, 0, -1)]
    public void ToBam_EachFace_ForwardPointsAlongTheFace(FaceDir face, int x, int z)
    {
        ushort bam = FaceYaw.ToBam(face);

        Assert.Equal(x * 32767, -TrigQ15.Sin(bam), tolerance: 1);
        Assert.Equal(z * 32767, -TrigQ15.Cos(bam), tolerance: 1);
    }
}
