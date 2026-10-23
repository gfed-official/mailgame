namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>Whole-world dump section lookup shared by the inventory dump tests (unit and integration).</summary>
internal static class DumpSections
{
    /// <summary>The payload of the section with id <paramref name="id"/> in a whole-world dump (header, then id u16 / length u32 / payload).</summary>
    internal static byte[] Section(byte[] dump, ushort id)
    {
        int pos = 6;
        while (pos < dump.Length)
        {
            ushort sectionId = BitConverter.ToUInt16(dump, pos);
            int length = (int)BitConverter.ToUInt32(dump, pos + 2);
            if (sectionId == id)
            {
                return dump.AsSpan(pos + 6, length).ToArray();
            }

            pos += 6 + length;
        }

        Assert.Fail($"Section {id} is not in the dump.");
        return Array.Empty<byte>();
    }
}
