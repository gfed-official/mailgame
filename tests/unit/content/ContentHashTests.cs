using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 013 (TR-content-024; GDD CDD-HASH-1 value half, CDD-HASH-2, CDD-HASH-3 single-OS form): the
/// managed SHA-256 against the NIST FIPS 180-2 vectors, the canonical writer against the F6 27-byte <c>letter</c> example,
/// and <see cref="ContentHash"/> over <see cref="BuiltInContent.CreateM1a"/>. Nothing here is random, timed or read from
/// disk; the golden digest is the cross-process, cross-configuration check (Debug, Release, net8.0, net10.0 all assert it).
/// <para>
/// REGENERATING THE GOLDEN. The golden digest pins the exact bytes of the M-1a content set. When content changes
/// intentionally (a new def, a changed balance value, a changed M-1a table, a new <see cref="DefCanonical"/> field, or a
/// <see cref="ContentHash.HashSchemaVersion"/> bump), run this class once: <see cref="ContentHash_M1a_MatchesTheGoldenDigest"/>
/// fails and its message prints the new 64-hex digest. Review that the change is intended, then paste the digest into
/// <see cref="GoldenM1aDigest"/> (and the 8-hex display form into <see cref="GoldenM1aDisplay"/>) in the same commit
/// as the content change. Never regenerate to silence an unexplained diff.
/// </para>
/// </summary>
public class ContentHashTests
{
    /// <summary>The golden SHA-256 of <c>BuiltInContent.CreateM1a()</c> at <c>hashSchemaVersion</c> 1. See the class remarks to regenerate.</summary>
    private const string GoldenM1aDigest = "018735cb1eafd0b47f3337697e6c2d2bf830cadece1ce27697b6c7e27bf28fa6";

    private const string GoldenM1aDisplay = "018735cb";

    // ------------------------------------------------------------ SHA-256 (NIST FIPS 180-2)

    private static string Hex(ReadOnlySpan<byte> data) => Sha256.Hash(data).ToHexString();

    [Fact]
    public void Sha256_EmptyMessage_MatchesNist()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Hex(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Sha256_Abc_MatchesNist()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Hex("abc"u8));
    }

    [Fact]
    public void Sha256_448BitTwoBlockMessage_MatchesNist()
    {
        byte[] message = Encoding.ASCII.GetBytes("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq");

        Assert.Equal(56, message.Length);   // 448 bits: the padding spills into a second block
        Assert.Equal("248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1", Hex(message));
    }

    [Fact]
    public void Sha256_OneMillionA_MatchesNist()
    {
        byte[] message = new byte[1_000_000];
        Array.Fill(message, (byte)'a');

        Assert.Equal("cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0", Hex(message));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(1000)]
    public void Sha256_ChunkedIncrementalInput_EqualsOneShot(int chunk)
    {
        byte[] message = new byte[2500];
        for (int i = 0; i < message.Length; i++)
        {
            message[i] = (byte)((i * 31) + 7);
        }

        var sha = new Sha256();
        for (int offset = 0; offset < message.Length; offset += chunk)
        {
            sha.Update(message.AsSpan(offset, Math.Min(chunk, message.Length - offset)));
        }

        Assert.Equal(Sha256.Hash(message), sha.Finish());
    }

    [Fact]
    public void Sha256_EveryLengthAroundThePaddingBoundaries_MatchesThePlatformOracle()
    {
        // The test project (not the Sim) may use the BCL as an independent oracle.
        for (int length = 0; length <= 200; length++)
        {
            byte[] message = new byte[length];
            for (int i = 0; i < length; i++)
            {
                message[i] = (byte)(i ^ length);
            }

            Assert.Equal(System.Security.Cryptography.SHA256.HashData(message), Sha256.Hash(message).ToArray());
        }
    }

    [Fact]
    public void Sha256_ReuseAfterReset_GivesTheSameDigest_AndUseAfterFinishThrows()
    {
        var sha = new Sha256();
        sha.Update("abc"u8);
        Hash256 first = sha.Finish();

        Assert.Throws<InvalidOperationException>(() => sha.Update("x"u8));
        Assert.Throws<InvalidOperationException>(() => sha.Finish());

        sha.Reset();
        sha.Update("abc"u8);
        Assert.Equal(first, sha.Finish());
    }

    [Fact]
    public void Sim_Assembly_ReferencesNoSystemSecurityCryptography()
    {
        Assembly sim = typeof(SimAssembly).Assembly;

        string[] referenced = sim.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("System.Security.Cryptography", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(referenced);

        // A type reference stores its namespace as a UTF-8 string in the metadata heap, so the raw image catches a
        // reference even through a facade assembly that is not listed above.
        byte[] image = File.ReadAllBytes(sim.Location);
        Assert.Equal(-1, image.AsSpan().IndexOf("System.Security.Cryptography"u8));
    }

    // ------------------------------------------------------------ Hash256

    [Fact]
    public void Hash256_DisplayForm_IsTheFirstFourBytesAsEightLowercaseHexCharacters()
    {
        Hash256 h = Sha256.Hash("abc"u8);

        Assert.Equal("ba7816bf", h.ToDisplayString());
        Assert.Equal("ba7816bf", h.ToString());
        Assert.Equal(64, h.ToHexString().Length);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", h.ToHexString());
    }

    [Fact]
    public void Hash256_Equality_ComparesAll32Bytes()
    {
        byte[] bytes = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            bytes[i] = (byte)(i + 1);
        }

        var a = new Hash256(bytes);
        var b = new Hash256(bytes);
        bytes[31] ^= 1;
        var lastByteDiffers = new Hash256(bytes);
        bytes[31] ^= 1;
        bytes[0] ^= 1;
        var firstByteDiffers = new Hash256(bytes);

        Assert.True(a == b);
        Assert.True(a.Equals((object)b));
        Assert.True(a != lastByteDiffers);
        Assert.True(a != firstByteDiffers);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Hash256_RoundTripsBytes_AndRejectsAWrongLength()
    {
        byte[] bytes = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();

        Assert.Equal(bytes, new Hash256(bytes).ToArray());
        Assert.Throws<ArgumentException>(() => new Hash256(new byte[31]));
        Assert.Throws<ArgumentException>(() => new Hash256(new byte[33]));
        Assert.Throws<ArgumentException>(() => new Hash256(bytes).CopyTo(new byte[31]));
    }

    // ------------------------------------------------------------ F6 letter example (CDD-HASH-2)

    private static readonly byte[] _f6LetterBytes =
    {
        0x06, 0x6C, 0x65, 0x74, 0x74, 0x65, 0x72,
        0x01, 0x00,
        0x14, 0x00,
        0x90, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xFF,
        0x02, 0x00, 0x01, 0x00, 0x03, 0x00,
    };

    /// <summary>The F6 illustrative five-field def shape (test-only; not a production def).</summary>
    private sealed record LetterShape(string Id, ushort WeightClass, ushort MaxStack, long BaseValue, ushort FallbackKind, ushort[] AcceptedBy);

    private static readonly LetterShape _letter = new("letter", 1, 20, 400, 0xFFFF, new ushort[] { 3, 1 });

    /// <summary>A deliberately separate encoder: plain shifts into a list, sharing no code with the production writer.</summary>
    private static byte[] IndependentEncode(LetterShape d)
    {
        var o = new List<byte>();
        byte[] id = Encoding.UTF8.GetBytes(d.Id);
        o.Add((byte)id.Length);
        o.AddRange(id);
        U16(o, d.WeightClass);
        U16(o, d.MaxStack);
        for (int i = 0; i < 8; i++)
        {
            o.Add((byte)((ulong)d.BaseValue >> (8 * i)));
        }

        U16(o, d.FallbackKind);
        ushort[] set = d.AcceptedBy.OrderBy(v => v).ToArray();
        U16(o, (ushort)set.Length);
        foreach (ushort v in set)
        {
            U16(o, v);
        }

        return o.ToArray();
    }

    private static void U16(List<byte> o, ushort v)
    {
        o.Add((byte)(v & 0xFF));
        o.Add((byte)(v >> 8));
    }

    private static byte[] ProductionPrimitivesEncode(LetterShape d)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new CanonicalContentWriter(buffer);
        w.WriteId(d.Id);
        w.WriteEnum(d.WeightClass);
        w.WriteU16(d.MaxStack);
        w.WriteI64(d.BaseValue);
        w.WriteDefIndex(d.FallbackKind == 0xFFFF ? DefIndex.None : DefIndex.FromValue(d.FallbackKind));
        w.WriteSortedSet(d.AcceptedBy);
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public void F6LetterExample_IndependentEncoder_ProducesTheDocumented27Bytes()
    {
        byte[] bytes = IndependentEncode(_letter);

        Assert.Equal(27, bytes.Length);
        Assert.Equal(_f6LetterBytes, bytes);
    }

    [Fact]
    public void F6LetterExample_ProductionPrimitives_ProduceTheSameDocumented27Bytes()
    {
        byte[] bytes = ProductionPrimitivesEncode(_letter);

        Assert.Equal(27, bytes.Length);
        Assert.Equal(_f6LetterBytes, bytes);
        Assert.Equal(IndependentEncode(_letter), bytes);
    }

    [Fact]
    public void F6TypeBlock_OpensWith0600ForACountOfSix()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new CanonicalContentWriter(buffer);
        w.WriteCount(6);

        Assert.Equal(new byte[] { 0x06, 0x00 }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Writer_FixedWidthPrimitives_AreLittleEndianAtTheirDeclaredWidth()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new CanonicalContentWriter(buffer);
        w.WriteU8(0xAB);
        w.WriteBool(true);
        w.WriteBool(false);
        w.WriteU16(0x1234);
        w.WriteI32(-2);
        w.WriteI64(0x0102030405060708);
        w.WriteString("aé");   // no normalisation: U+00E9 stays two UTF-8 bytes C3 A9

        Assert.Equal(
            new byte[]
            {
                0xAB, 0x01, 0x00, 0x34, 0x12, 0xFE, 0xFF, 0xFF, 0xFF,
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x03, 0x00, 0x61, 0xC3, 0xA9,
            },
            buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Writer_SetValuedList_IsSortedByValueRegardlessOfSuppliedOrder()
    {
        byte[] a = SetBytes(1, 2, 3);
        byte[] b = SetBytes(3, 2, 1);
        byte[] c = SetBytes(2, 3, 1);

        Assert.Equal(new byte[] { 3, 0, 1, 0, 2, 0, 3, 0 }, a);
        Assert.Equal(a, b);
        Assert.Equal(a, c);

        static byte[] SetBytes(params ushort[] values)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var w = new CanonicalContentWriter(buffer);
            w.WriteSortedSet(values);
            return buffer.WrittenSpan.ToArray();
        }
    }

    // ------------------------------------------------------------ Over-wide lists and strings (CV-GEN-12)

    [Fact]
    public void Writer_CountOf65535_IsAccepted_And65536IsRejectedWithCvGen12()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new CanonicalContentWriter(buffer);
        w.WriteCount(65_535);
        Assert.Equal(new byte[] { 0xFF, 0xFF }, buffer.WrittenSpan.ToArray());

        ContentEncodingException ex = Throw(() =>
        {
            var inner = new CanonicalContentWriter(new ArrayBufferWriter<byte>());
            inner.WriteCount(65_536);
        });
        Assert.Equal("CV-GEN-12", ex.RuleId);
        Assert.StartsWith("CV-GEN-12", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_StringOf65535Bytes_IsAccepted_And65536IsRejectedWithCvGen12()
    {
        var ok = new ArrayBufferWriter<byte>();
        var w = new CanonicalContentWriter(ok);
        w.WriteString(new string('k', 65_535));
        Assert.Equal(2 + 65_535, ok.WrittenCount);

        ContentEncodingException ex = Throw(() =>
        {
            var inner = new CanonicalContentWriter(new ArrayBufferWriter<byte>());
            inner.WriteString(new string('k', 65_536));
        });
        Assert.Equal("CV-GEN-12", ex.RuleId);
    }

    [Fact]
    public void Writer_StringWhoseUtf8FormExceeds65535Bytes_IsRejectedEvenWhenItsCharCountFits()
    {
        // 32 768 two-byte characters: 32 768 chars, 65 536 UTF-8 bytes.
        string wide = new('é', 32_768);

        ContentEncodingException ex = Throw(() =>
        {
            var w = new CanonicalContentWriter(new ArrayBufferWriter<byte>());
            w.WriteString(wide);
        });
        Assert.Equal("CV-GEN-12", ex.RuleId);
    }

    [Fact]
    public void Writer_SetWithMoreThan65535Entries_IsRejectedWithCvGen12()
    {
        ContentEncodingException ex = Throw(() =>
        {
            var w = new CanonicalContentWriter(new ArrayBufferWriter<byte>());
            w.WriteSortedSet(new ushort[65_536]);
        });
        Assert.Equal("CV-GEN-12", ex.RuleId);
    }

    [Fact]
    public void Writer_IdOver255Bytes_IsRejectedWithCvGen02()
    {
        ContentEncodingException ex = Throw(() =>
        {
            var w = new CanonicalContentWriter(new ArrayBufferWriter<byte>());
            w.WriteId(new string('i', 256));
        });
        Assert.Equal("CV-GEN-02", ex.RuleId);
    }

    [Fact]
    public void DefCanonical_ItemWithMoreThan65535Tags_IsRejectedWithCvGen12()
    {
        ItemDef item = Plank() with { Tags = Enumerable.Repeat("t", 65_536).ToImmutableArray() };

        ContentEncodingException ex = Throw(() =>
        {
            var w = new CanonicalContentWriter(new ArrayBufferWriter<byte>());
            item.WriteCanonical(ref w);
        });
        Assert.Equal("CV-GEN-12", ex.RuleId);
    }

    [Fact]
    public void ContentHash_SetWhoseItemHasAnOverLongLocalisationKey_IsRejectedWithCvGen12()
    {
        ContentSet baseline = BuiltInContent.CreateM1a();
        ItemDef bad = Plank() with { Name = new string('n', 65_536) };
        ContentSet set = Rebuild(baseline, items: Items(baseline).Select(i => i.Id == "plank" ? bad : i).ToImmutableArray());

        ContentEncodingException ex = Throw(() => ContentHash.Compute(set));
        Assert.Equal("CV-GEN-12", ex.RuleId);
    }

    // ------------------------------------------------------------ Empty tables

    [Fact]
    public void EmptyDefTables_EncodeAs0000EachAfterTheBalanceBlock()
    {
        ContentSet baseline = BuiltInContent.CreateM1a();
        ContentSet empty = Rebuild(
            baseline,
            items: ImmutableArray<ItemDef>.Empty,
            containers: ImmutableArray<ContainerDef>.Empty,
            kinds: ImmutableArray<MailKindDef>.Empty,
            destinations: ImmutableArray<DestinationTypeDef>.Empty,
            mixes: ImmutableArray<MailMixDef>.Empty);

        var buffer = new ArrayBufferWriter<byte>();
        ContentHash.WriteCanonical(empty, buffer);
        byte[] bytes = buffer.WrittenSpan.ToArray();

        int balanceBytes = 2;
        foreach (string name in baseline.Balance.Registry.Names)
        {
            balanceBytes += 2 + Encoding.UTF8.GetByteCount(name) + 8;
        }

        Assert.Equal(1 + balanceBytes + (5 * 2), bytes.Length);
        Assert.Equal(new byte[10], bytes.AsSpan(bytes.Length - 10).ToArray());
        Assert.Equal(ContentHash.HashSchemaVersion, bytes[0]);
    }

    // ------------------------------------------------------------ ContentHash over CreateM1a (CDD-HASH-1 value half)

    private static ImmutableArray<ItemDef> Items(ContentSet s) => s.Items.AsSpan().ToArray().ToImmutableArray();

    private static ItemDef Plank() => BuiltInContent.M1aItems().Single(i => i.Id == "plank");

    private static ContentSet Rebuild(
        ContentSet baseline,
        ImmutableArray<ItemDef>? items = null,
        ImmutableArray<ContainerDef>? containers = null,
        ImmutableArray<MailKindDef>? kinds = null,
        ImmutableArray<DestinationTypeDef>? destinations = null,
        ImmutableArray<MailMixDef>? mixes = null) =>
        new(
            baseline.Balance,
            baseline.ResolvedStatBounds,
            new DefTable<ContainerDef>(containers ?? baseline.Containers.AsSpan().ToArray().ToImmutableArray()),
            new DefTable<ItemDef>(items ?? Items(baseline)),
            new DefTable<MailKindDef>(kinds ?? baseline.MailKinds.AsSpan().ToArray().ToImmutableArray()),
            new DefTable<DestinationTypeDef>(destinations ?? baseline.DestinationTypes.AsSpan().ToArray().ToImmutableArray()),
            new DefTable<MailMixDef>(mixes ?? baseline.MailMixes.AsSpan().ToArray().ToImmutableArray()));

    private static ContentSet BuildWith(
        ImmutableArray<BalanceEntry>? entries = null,
        ImmutableArray<ItemDef>? items = null,
        ImmutableArray<DestinationTypeDef>? destinations = null)
    {
        RuleRegistry rules = RuleRegistry.CreateDefault();
        BalanceBuildResult balance = BalanceTable.Build(BalanceKeys.CreateRegistry(), entries ?? BalanceKeys.CreateDefaultEntries(), rules);
        Assert.NotNull(balance.Table);
        ContentBuildResult built = BuiltInContent.Build(
            balance.Table!,
            BuiltInContent.M1aContainers(),
            items ?? BuiltInContent.M1aItems(),
            BuiltInContent.M1aMailKinds(),
            destinations ?? BuiltInContent.M1aDestinationTypes(),
            rules);
        Assert.NotNull(built.Set);
        return built.Set!;
    }

    private static Hash256 M1aHash() => ContentHash.Compute(BuiltInContent.CreateM1a());

    private static ImmutableArray<ItemDef> ItemsWith(Func<ItemDef, ItemDef> change) =>
        BuiltInContent.M1aItems().Select(i => i.Id == "plank" ? change(i) : i).ToImmutableArray();

    [Fact]
    public void ContentHash_SameContentBuiltTwice_IsEqual()
    {
        Assert.Equal(M1aHash(), M1aHash());
        Assert.Equal(M1aHash(), ContentHash.Compute(BuildWith()));
    }

    [Fact]
    public void ContentHash_OneDefValueChanged_ChangesTheHash()
    {
        ContentSet changed = BuildWith(items: ItemsWith(i => i with { SellPriceCents = i.SellPriceCents + 1 }));

        Assert.NotEqual(M1aHash(), ContentHash.Compute(changed));
    }

    [Fact]
    public void ContentHash_OneBalanceKeyChanged_ChangesTheHash()
    {
        ImmutableArray<BalanceEntry> entries = BalanceKeys.CreateDefaultEntries();
        string key = BalanceKeys.Sim.Owner + "." + BalanceKeys.Sim.WarmupTicksKey;
        ImmutableArray<BalanceEntry> changedEntries = entries
            .Select(e => e.Name == key ? e with { Value = e.Value + 1 } : e)
            .ToImmutableArray();

        Assert.NotEqual(entries, changedEntries);
        Assert.NotEqual(M1aHash(), ContentHash.Compute(BuildWith(entries: changedEntries)));
    }

    [Fact]
    public void ContentHash_OptionalFieldPresenceChanged_ChangesTheHash()
    {
        ContentSet withBuyPrice = BuildWith(items: ItemsWith(i => i with { HasBuyPrice = true, BuyPriceCents = 0 }));

        // Present-with-zero differs from absent: the presence byte is part of the bytes, not just the value.
        Assert.NotEqual(M1aHash(), ContentHash.Compute(withBuyPrice));
    }

    [Fact]
    public void ContentHash_ValueBehindAFalsePresenceFlag_DoesNotReachTheBytes()
    {
        ContentSet stale = BuildWith(items: ItemsWith(i => i with { HasBuyPrice = false, BuyPriceCents = 999 }));

        Assert.Equal(M1aHash(), ContentHash.Compute(stale));
    }

    [Fact]
    public void ContentHash_SetValuedListSuppliedReordered_DoesNotChangeTheHash()
    {
        ImmutableArray<DestinationTypeDef> reordered = BuiltInContent.M1aDestinationTypes()
            .Select(d => d with { Accepts = d.Accepts.Reverse().ToImmutableArray() })
            .ToImmutableArray();
        Assert.NotEqual(BuiltInContent.M1aDestinationTypes()[0].Accepts, reordered[0].Accepts);

        Assert.Equal(M1aHash(), ContentHash.Compute(BuildWith(destinations: reordered)));
    }

    [Fact]
    public void ContentHash_AcceptedByAndAllowedCategoriesReordered_ProduceIdenticalDefBytes()
    {
        MailKindDef kind = BuiltInContent.M1aMailKinds()[0];
        MailKindDef kindA = kind with { AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(0), DefIndex.FromValue(2), DefIndex.FromValue(5)) };
        MailKindDef kindB = kind with { AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(5), DefIndex.FromValue(0), DefIndex.FromValue(2)) };
        ContainerDef box = BuiltInContent.M1aContainers().Single(c => c.Id == "po_intake");
        ContainerDef boxA = box with { AllowedCategories = ImmutableArray.Create(ItemCategory.Mail, ItemCategory.Tool) };
        ContainerDef boxB = box with { AllowedCategories = ImmutableArray.Create(ItemCategory.Tool, ItemCategory.Mail) };

        Assert.Equal(Bytes(kindA), Bytes(kindB));
        Assert.Equal(Bytes(boxA), Bytes(boxB));
        Assert.NotEqual(Bytes(kindA), Bytes(kind));   // a different set still differs
    }

    // ------------------------------------------------------------ Golden digest (CDD-HASH-3 single-OS form)

    [Fact]
    public void ContentHash_M1a_MatchesTheGoldenDigest()
    {
        Hash256 actual = M1aHash();

        Assert.True(
            GoldenM1aDigest == actual.ToHexString(),
            "The M-1a ContentHash changed. Actual digest: " + actual.ToHexString() + " (display " + actual.ToDisplayString()
            + "). If the content change is intentional, paste it into GoldenM1aDigest and GoldenM1aDisplay (see this class's remarks).");
        Assert.Equal(GoldenM1aDisplay, actual.ToDisplayString());
        Assert.Equal(8, actual.ToDisplayString().Length);
        Assert.Matches("^[0-9a-f]{8}$", actual.ToDisplayString());
    }

    [Fact]
    public void ContentHash_IsTheSha256OfTheCanonicalStream()
    {
        ContentSet set = BuiltInContent.CreateM1a();
        var buffer = new ArrayBufferWriter<byte>();
        ContentHash.WriteCanonical(set, buffer);

        Assert.Equal(ContentHash.Compute(set), Sha256.Hash(buffer.WrittenSpan));
        Assert.Equal(1, buffer.WrittenSpan[0]);   // hashSchemaVersion
    }

    // ------------------------------------------------------------ ComputeSettings (ADR-0006 section 7 header)

    [Fact]
    public void ComputeSettings_IsStable_AndChangesWithAnySettingsValue()
    {
        SimSettings settings = BuiltInContent.CreateM1a().Settings;
        Hash256 baseline = ContentHash.ComputeSettings(settings);

        Assert.Equal(baseline, ContentHash.ComputeSettings(BuiltInContent.CreateM1a().Settings));
        Assert.NotEqual(baseline, ContentHash.ComputeSettings(settings with { WarmupTicks = settings.WarmupTicks + 1 }));
        Assert.NotEqual(baseline, ContentHash.ComputeSettings(settings with { MailTablePages = settings.MailTablePages + 1 }));
        Assert.NotEqual(baseline, ContentHash.ComputeSettings(settings with { Stats = new StatsSettings { ModifierPoolPerTeam = 129 } }));
        Assert.NotEqual(baseline, ContentHash.ComputeSettings(settings with { Core = settings.Core with { CheckRingCapacity = settings.Core.CheckRingCapacity + 1 } }));
        Assert.NotEqual(baseline, M1aHash());
    }

    [Fact]
    public void ComputeSettings_Stream_IsVersionThenOrdinalSortedSimKeys()
    {
        var buffer = new ArrayBufferWriter<byte>();
        ContentHash.WriteSettings(BuiltInContent.CreateM1a().Settings, buffer);
        ReadOnlySpan<byte> bytes = buffer.WrittenSpan;

        Assert.Equal(1, bytes[0]);
        Assert.Equal(11, bytes[1] | (bytes[2] << 8));
        string first = Encoding.UTF8.GetString(bytes.Slice(5, bytes[3] | (bytes[4] << 8)));
        Assert.Equal("sim.checkRingCapacity", first);
        Assert.Equal(Sha256.Hash(bytes), ContentHash.ComputeSettings(BuiltInContent.CreateM1a().Settings));
    }

    [Fact]
    public void ComputeSettings_CoversEveryRegisteredSimKey_WithTheValuesOfTheBalanceTable()
    {
        ContentSet set = BuiltInContent.CreateM1a();
        var buffer = new ArrayBufferWriter<byte>();
        ContentHash.WriteSettings(set.Settings, buffer);
        byte[] bytes = buffer.WrittenSpan.ToArray();

        int pos = 1;
        int count = bytes[pos] | (bytes[pos + 1] << 8);
        pos += 2;
        var encoded = new List<(string Name, long Value)>();
        for (int i = 0; i < count; i++)
        {
            int len = bytes[pos] | (bytes[pos + 1] << 8);
            string name = Encoding.UTF8.GetString(bytes, pos + 2, len);
            pos += 2 + len;
            encoded.Add((name, BitConverter.ToInt64(bytes, pos)));
            pos += 8;
        }

        Assert.Equal(bytes.Length, pos);
        string[] registered = set.Balance.Registry.Names.Where(n => n.StartsWith("sim.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(registered, encoded.Select(e => e.Name).ToArray());
        for (int i = 0; i < registered.Length; i++)
        {
            Assert.True(set.Balance.Registry.TryFind(registered[i], out BalanceKey key));
            Assert.Equal(set.Balance.Get(key), encoded[i].Value);
        }
    }

    // ------------------------------------------------------------ Every def field reaches the bytes (field-order guard)

    private static byte[] Bytes(ItemDef d) { var b = new ArrayBufferWriter<byte>(); var w = new CanonicalContentWriter(b); d.WriteCanonical(ref w); return b.WrittenSpan.ToArray(); }

    private static byte[] Bytes(ContainerDef d) { var b = new ArrayBufferWriter<byte>(); var w = new CanonicalContentWriter(b); d.WriteCanonical(ref w); return b.WrittenSpan.ToArray(); }

    private static byte[] Bytes(MailKindDef d) { var b = new ArrayBufferWriter<byte>(); var w = new CanonicalContentWriter(b); d.WriteCanonical(ref w); return b.WrittenSpan.ToArray(); }

    private static byte[] Bytes(DestinationTypeDef d) { var b = new ArrayBufferWriter<byte>(); var w = new CanonicalContentWriter(b); d.WriteCanonical(ref w); return b.WrittenSpan.ToArray(); }

    private static byte[] Bytes(MailMixDef d) { var b = new ArrayBufferWriter<byte>(); var w = new CanonicalContentWriter(b); d.WriteCanonical(ref w); return b.WrittenSpan.ToArray(); }

    [Fact]
    public void ItemDef_EveryField_ChangesTheBytes()
    {
        ItemDef b = Plank() with { HasBuyPrice = true, BuyPriceCents = 5, Tags = ImmutableArray.Create("a") };
        ItemDef[] mutants =
        {
            b with { Id = "plank2" }, b with { Name = "item.other" }, b with { Category = ItemCategory.Tool }, b with { GridW = b.GridW + 1 },
            b with { GridH = b.GridH + 1 }, b with { MaxStack = b.MaxStack + 1 }, b with { WeightClass = WeightClass.Heavy },
            b with { SellPriceCents = 1 }, b with { HasBuyPrice = false }, b with { BuyPriceCents = 6 }, b with { CarryByHand = !b.CarryByHand },
            b with { EquipSlot = EquipSlot.Back }, b with { Tags = ImmutableArray.Create("b") }, b with { Tags = ImmutableArray.Create("a", "b") },
        };

        AssertAllDiffer(Bytes(b), mutants.Select(Bytes));
    }

    [Fact]
    public void ContainerDef_EveryField_ChangesTheBytes()
    {
        ContainerDef b = BuiltInContent.M1aContainers().Single(c => c.Id == "po_intake");
        ContainerDef[] mutants =
        {
            b with { Id = "po_intake2" }, b with { Layout = ContainerLayout.Slots }, b with { HasGrid = false }, b with { GridW = b.GridW + 1 },
            b with { GridH = b.GridH + 1 }, b with { View = ContainerView.Grid }, b with { BeltAccess = BeltAccess.None },
            b with { HasAllowedCategories = false }, b with { AllowedCategories = ImmutableArray.Create(ItemCategory.Tool) },
            b with { HandCarried = !b.HandCarried }, b with { AcceptsInsert = !b.AcceptsInsert }, b with { ReservedSlots = ImmutableArray.Create(3) },
        };

        AssertAllDiffer(Bytes(b), mutants.Select(Bytes));
    }

    [Fact]
    public void MailKindDef_EveryField_ChangesTheBytes()
    {
        MailKindDef b = BuiltInContent.M1aMailKinds()[1];
        MailKindDef[] mutants =
        {
            b with { Id = "letter2" }, b with { Name = "mail.other" }, b with { GridW = b.GridW + 1 }, b with { GridH = b.GridH + 1 },
            b with { MaxStack = b.MaxStack + 1 }, b with { BaseValueCents = b.BaseValueCents + 1 }, b with { WeightClass = WeightClass.Bulk },
            b with { CarryByHand = !b.CarryByHand }, b with { BeltLanes = b.BeltLanes + 1 }, b with { BeltLengthMm = b.BeltLengthMm + 1 },
            b with { DeadlineOffsetShifts = b.DeadlineOffsetShifts + 1 }, b with { AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(1)) },
            b with { ComplaintOnMisdeliveryMilliPoints = b.ComplaintOnMisdeliveryMilliPoints + 1 }, b with { UnlockShift = b.UnlockShift + 1 },
            b with { FallbackKind = DefIndex.FromValue(2) },
        };

        AssertAllDiffer(Bytes(b), mutants.Select(Bytes));
    }

    [Fact]
    public void DestinationTypeDef_EveryField_ChangesTheBytes()
    {
        DestinationTypeDef b = BuiltInContent.M1aDestinationTypes()[0];
        CatchVolume v = b.CatchVolume;
        DestinationTypeDef[] mutants =
        {
            b with { Id = "house2" }, b with { Accepts = ImmutableArray.Create(DefIndex.FromValue(1)) },
            b with { InsertRateMilliPerSec = b.InsertRateMilliPerSec + 1 }, b with { ManualInsertHoldMs = b.ManualInsertHoldMs + 1 },
            b with { MaxAutomatedFeeders = b.MaxAutomatedFeeders + 1 }, b with { RequiresUnit = !b.RequiresUnit },
            b with { CatchVolume = v with { WidthMm = v.WidthMm + 1 } }, b with { CatchVolume = v with { HeightMm = v.HeightMm + 1 } },
            b with { CatchVolume = v with { DepthMm = v.DepthMm + 1 } }, b with { CatchVolume = v with { CentreHeightMm = v.CentreHeightMm + 1 } },
            b with { CatchVolume = v with { YawMilliDeg = v.YawMilliDeg + 1 } },
        };

        AssertAllDiffer(Bytes(b), mutants.Select(Bytes));
    }

    [Fact]
    public void MailMixDef_EveryField_ChangesTheBytes()
    {
        MailMixDef b = BuiltInContent.M1aMailMix(BuiltInContent.M1aMailKinds());
        ShiftMix first = b.Shifts[0];
        MailShare share = first.Shares[0];
        MailMixDef[] mutants =
        {
            b with { Id = "default2" },
            b with { Shifts = b.Shifts.SetItem(0, first with { Shift = first.Shift + 5 }) },
            b with { Shifts = b.Shifts.SetItem(0, first with { Shares = first.Shares.SetItem(0, share with { SharePm = share.SharePm + 1 }) }) },
            b with { Shifts = b.Shifts.SetItem(0, first with { Shares = first.Shares.SetItem(0, share with { Kind = DefIndex.FromValue(4) }) }) },
            b with { Shifts = b.Shifts.RemoveAt(1) },
            b with { BatchIntervalMs = b.BatchIntervalMs + 1 }, b with { BatchJitterMs = b.BatchJitterMs + 1 }, b with { TailGuardMs = b.TailGuardMs + 1 },
            b with { OpeningRampBatches = b.OpeningRampBatches + 1 }, b with { SpawnOverheadPm = b.SpawnOverheadPm + 1 },
            b with { StreetStreakRatioPm = b.StreetStreakRatioPm + 1 }, b with { RaidSpawnRateRatioPm = b.RaidSpawnRateRatioPm + 1 },
            b with { DistanceStepRatioPm = b.DistanceStepRatioPm + 1 }, b with { ShiftStepRatioPm = b.ShiftStepRatioPm + 1 },
        };

        AssertAllDiffer(Bytes(b), mutants.Select(Bytes));
    }

    private static void AssertAllDiffer(byte[] baseline, IEnumerable<byte[]> mutants)
    {
        int index = 0;
        foreach (byte[] m in mutants)
        {
            Assert.False(baseline.AsSpan().SequenceEqual(m), "mutation #" + index + " did not change the bytes");
            index++;
        }
    }

    private static ContentEncodingException Throw(Action action) => Assert.Throws<ContentEncodingException>(action);
}
