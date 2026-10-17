using System.Buffers;
using System.Security.Cryptography;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests;

/// <summary>
/// Test-only hashing of the canonical state dump (ADR-0006 section 4: the Sim never hashes its own state; tests and tools
/// take SHA-256 of the dump bytes).
/// </summary>
internal static class DumpHash
{
    /// <summary>Writes the world's dump and returns its bytes; fails the test when the dump is refused.</summary>
    public static byte[] Bytes(SimWorld world)
    {
        var output = new ArrayBufferWriter<byte>();
        Assert.True(world.TryWriteDump(output), "TryWriteDump was refused.");
        return output.WrittenSpan.ToArray();
    }

    /// <summary>SHA-256 of the world's dump, as lower-case hex (fails the test when the dump is refused).</summary>
    public static string Sha256(SimWorld world) => Convert.ToHexString(SHA256.HashData(Bytes(world)));
}
