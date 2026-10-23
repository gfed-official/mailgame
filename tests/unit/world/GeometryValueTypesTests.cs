using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Equality, hashing and field behaviour of the plain geometry value types created by world-stub
/// story 001 (<see cref="HeightCorners"/>, <see cref="Vec3Mm"/>, <see cref="AabbMm"/>,
/// <see cref="TileCoord"/>) and the minimal <see cref="AddressId"/>. Explicit equality is required on
/// hashed structs (control manifest I-6).
/// </summary>
public class GeometryValueTypesTests
{
    [Fact]
    public void HeightCorners_EqualFields_EqualAndSameHash()
    {
        // Arrange
        var a = new HeightCorners(1, 2, 3, 4);
        var b = new HeightCorners(1, 2, 3, 4);
        var c = new HeightCorners(1, 2, 3, 5);

        // Assert
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != c);
        Assert.False(a.Equals(c));
        Assert.False(a.Equals("x"));
        Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { a.H00, a.H10, a.H01, a.H11 });
    }

    [Fact]
    public void Vec3Mm_EqualFields_EqualAndSameHash()
    {
        // Arrange
        var a = new Vec3Mm(1, -2, 3);
        var b = new Vec3Mm(1, -2, 3);

        // Assert
        Assert.True(a == b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != new Vec3Mm(1, -2, 4));
        Assert.True(a != new Vec3Mm(1, 2, 3));
        Assert.True(a != new Vec3Mm(0, -2, 3));
        Assert.False(a.Equals(null));
        Assert.Equal(default, new Vec3Mm(0, 0, 0));
    }

    [Fact]
    public void AabbMm_EqualCorners_EqualAndSameHash()
    {
        // Arrange
        var a = new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(10, 20, 30));
        var b = new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(10, 20, 30));

        // Assert
        Assert.True(a == b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != new AabbMm(new Vec3Mm(0, 0, 1), new Vec3Mm(10, 20, 30)));
        Assert.True(a != new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(10, 20, 31)));
        Assert.Equal(new Vec3Mm(10, 20, 30), a.Max);
        Assert.Equal(new Vec3Mm(0, 0, 0), a.Min);
    }

    [Fact]
    public void TileCoord_EqualFields_EqualAndSameHash()
    {
        // Arrange
        var a = new TileCoord(3, 12);
        var b = new TileCoord(3, 12);

        // Assert
        Assert.True(a == b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != new TileCoord(12, 3));
        Assert.Equal(3, a.Tx);
        Assert.Equal(12, a.Tz);
    }
}
