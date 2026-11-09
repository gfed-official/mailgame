using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing-catching story 004, TC-T3-1 a (GDD T3 "Launch direction", F1 <c>dir</c>): <see cref="ThrowAim.Quantise"/> clamps the camera
/// pitch to −60°…+70°, adds <c>loftDeg</c> 6° and quantises once to an int16 x3 vector the F1 host bounds accept.
/// </summary>
public class ThrowAimTests
{
    private const int Loft = 6000;

    [Theory]
    [Trait("Id", "TC-T3-1")]
    [InlineData((short)14_564, 15_897)]    // +80°, past the camera clamp: elevation +76°
    [InlineData((short)-12_743, -13_255)]  // −70°, past the camera clamp: elevation −54°
    public void PitchPastTheClamp_QuantisesToTheClampedElevationPlusLoft(short pitchBam, int expectedDy)
    {
        // Act
        ThrowAim.Quantise(0, pitchBam, Loft, out _, out short dy, out _);

        // Assert
        Assert.InRange(dy, expectedDy - 2, expectedDy + 2);
    }

    [Fact]
    [Trait("Id", "TC-T3-1")]
    public void PitchPlus6_FacingMinusZ_IsTheFxTVector()
    {
        // Act: +6° is 1092 BAM; yaw 0 faces −Z.
        ThrowAim.Quantise(0, 1092, Loft, out short dx, out short dy, out short dz);

        // Assert
        Assert.Equal(ThrowGoldenVectors.FxTDir, (dx, dy, dz));
    }

    [Fact]
    [Trait("Id", "TC-T3-1")]
    public void TenThousandRandomAims_AllPassTheHostBounds()
    {
        // Arrange
        var random = new Random(4004);

        for (int i = 0; i < 10_000; i++)
        {
            ushort yaw = (ushort)random.Next(0, 65_536);
            short pitch = (short)random.Next(short.MinValue, short.MaxValue + 1);

            // Act
            ThrowAim.Quantise(yaw, pitch, Loft, out short dx, out short dy, out short dz);

            // Assert: ValidateDir is the host's check 8 (16000 <= n <= 16768 and the elevation bounds).
            Assert.True(LaunchMath.ValidateDir(dx, dy, dz, out long n), $"yaw {yaw} pitch {pitch} -> ({dx}, {dy}, {dz}), n {n}");
        }
    }
}
