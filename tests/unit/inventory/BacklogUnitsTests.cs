using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/021 review round 1 (A6, A5 c): the widest well-formed <see cref="IInventoryApi.AppendBacklog"/> (255 ids, for
/// a kind whose effective maxStack is 255) and the <c>BacklogUnits</c> payload's saturation: <see cref="IInventoryApi.BacklogUnits"/>
/// is an <c>int</c>, the 0x008A payload's <c>units</c> a <c>u16</c> clamped at 65,535. The payload's <c>entries</c> clamp cannot
/// trip: the ring holds at most <c>inventory.entryPoolCapacity</c> entries, which the settings bound at 65,535.
/// </summary>
public sealed class BacklogUnitsTests
{
    private const uint Address4 = 4u;

    [Fact]
    public void AppendBacklog_A255IdEntryOfAKindWithMaxStack255_IsQueued_Count255_Units255_AndOnePayloadOf255Units()
    {
        // Arrange: letter maxStack 255; a full Intake (so the drain idles)
        var rig = new InvAutoPlaceRig(content: LetterMaxStack255Content());
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        uint[] ids = InvAutoPlaceRig.Ids(10_000, 255);

        // Act
        bool ok = IntakeDrainTests.Append(rig, "letter", Address4, ids);
        EventEntry[] log = rig.Sink.Last;

        // Assert: one count-255 entry holding the 255 ids; 255 units, 1 entry; one BacklogUnits {255, 1}
        Assert.True(ok);
        ushort queued = Assert.Single(IntakeDrainTests.Queue(rig));
        Assert.Equal(255, rig.Entries[queued].Count);
        Assert.Equal(ids, rig.IdsOf(queued));
        Assert.Equal(255, rig.Inventory.BacklogUnits);
        Assert.Equal(1, rig.Inventory.BacklogEntries);
        BacklogUnitsPayload p = Assert.Single(log, e => e.Kind == InvEventKinds.BacklogUnits).PayloadAs<BacklogUnitsPayload>();
        Assert.Equal(255, p.Units);
        Assert.Equal(1, p.Entries);
    }

    [Fact]
    public void BacklogUnitsPayload_SaturatesUnitsAt65535_WhileTheBacklogUnitsPropertyKeepsTheTrueSum()
    {
        // Arrange: letter maxStack 255; a 600,000-unit arena (its 256-unit class then holds 258 blocks); a full Intake; 258 entries
        // of 255 ids (65,790 units) appended in one MailStep
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: 600_000, content: LetterMaxStack255Content());
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1);
        DefIndex letter = rig.KindIndex("letter");

        // Act
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            for (uint i = 0; i < 258; i++)
            {
                Assert.True(rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(InvAutoPlaceRig.Ids(100_000 + (i * 1_000), 255)))));
            }

            return 0;
        });
        BacklogUnitsPayload[] payloads = rig.Sink.Last
            .Where(e => e.Kind == InvEventKinds.BacklogUnits)
            .Select(e => e.PayloadAs<BacklogUnitsPayload>())
            .ToArray();

        // Assert: one record per append; units 255 x n up to 65,535 (n = 257 exactly), then held at 65,535; the property is unclamped
        Assert.Equal(258, payloads.Length);
        Assert.Equal(510, payloads[1].Units);
        Assert.Equal(65_280, payloads[255].Units);
        Assert.Equal(65_535, payloads[256].Units);
        Assert.Equal(65_535, payloads[257].Units);
        Assert.Equal(258, payloads[257].Entries);
        Assert.Equal(65_790, rig.Inventory.BacklogUnits);
        Assert.Equal(258, rig.Inventory.BacklogEntries);
    }

    /// <summary>The M-1a content with the letter's <c>maxStack</c> raised to 255 (the <c>MailMaxStack</c> ceiling).</summary>
    internal static ContentSet LetterMaxStack255Content()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ImmutableArray<MailKindDef> kinds = m1a.MailKinds.AsSpan().ToArray()
            .Select(k => k.Id == "letter" ? k with { MaxStack = 255 } : k)
            .ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, m1a.Containers, m1a.Items, new DefTable<MailKindDef>(kinds), m1a.DestinationTypes, m1a.MailMixes);
    }
}
