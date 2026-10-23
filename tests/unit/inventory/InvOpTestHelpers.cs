using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Op-rig helpers shared by <see cref="PickMoveTests"/>, <see cref="MoveSplitMergeTests"/>, <see cref="QuickMoveReturnTests"/> and
/// <see cref="OpenSetPutAwayTests"/>; only helpers whose copies were identical were moved here. The reject contract
/// (<c>SideEffects</c> / <c>AssertRejected</c>), the op builders and <c>AssertWeightAndCountsMatchRecount</c> differ per file and stay
/// local to it.
/// </summary>
internal static class InvOpTestHelpers
{
    /// <summary>Spawns single letters into the Intake (one tick) until <see cref="IInventoryApi.Spawn"/> places none.</summary>
    internal static void ExhaustArenaThroughIntakeSpawns(InvAutoPlaceRig rig)
    {
        DefIndex letter = rig.KindIndex("letter");
        ContainerId intake = rig.Inventory.IntakeContainer;
        var one = new MailId[1];
        uint next = 1_000_000;
        int placed = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            int n = 0;
            while (true)
            {
                one[0] = new MailId(next++);
                if (rig.Inventory.Spawn(intake, letter, 9, 0, one, MergePolicy.NoMerge).Placed != 1)
                {
                    return n;
                }

                n++;
            }
        });
        Assert.True(placed > 0);
        Assert.True(rig.Header(rig.Intake).FreeCells > 0); // refused at the arena, not for room
        Assert.False(rig.Inventory.MailIds.CanReserve(1));
    }

    /// <summary>D1's <c>classPts</c> of the entry's def, from the balance knobs (not from the module under test).</summary>
    internal static int ClassPts(InvAutoPlaceRig rig, ushort entry)
    {
        EntryRecord e = rig.Entries[entry];
        WeightClass c = e.IsMail
            ? rig.Content.MailKinds[DefIndex.FromValue(e.MailKind)].WeightClass
            : rig.Content.Items[DefIndex.FromValue(e.ItemId)].WeightClass;
        InventoryBalance k = rig.Content.Inventory;
        return c switch
        {
            WeightClass.Light => k.WeightPointsLight,
            WeightClass.Medium => k.WeightPointsMedium,
            WeightClass.Heavy => k.WeightPointsHeavy,
            _ => throw new InvalidOperationException($"entry {entry}: class {c}"),
        };
    }

    /// <summary><c>CountItem</c> of every item def in every live container, in table order.</summary>
    internal static int[] AllItemCounts(InvAutoPlaceRig rig)
    {
        ContainerTable containers = rig.Inventory.Containers;
        var counts = new List<int>();
        for (int s = 0; s < containers.LiveCount; s++)
        {
            for (int i = 0; i < rig.Content.Items.Count; i++)
            {
                counts.Add(rig.Inventory.CountItem(new[] { containers[(ushort)s].Id }, DefIndex.FromValue((ushort)i)));
            }
        }

        return counts.ToArray();
    }

    internal static Reply AssertOk(EventEntry[] log)
    {
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(CoreReason.Ok, reply.Reason);
        return reply;
    }

    internal static EventEntry[] InventoryRecords(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.ItemsLost).ToArray();

    /// <summary>The inventory records and seals that start a record (mail-id run continuations dropped), in log order.</summary>
    internal static EventEntry[] RecordHeads(EventEntry[] log) =>
        InventoryRecords(log).Where(e => (e.Flags & EventFlags.Continuation) == 0).ToArray();

    internal static InvVersionPayload[] Seals(EventEntry[] log) =>
        log.Where(e => e.Kind == InvEventKinds.Version).Select(e => e.PayloadAs<InvVersionPayload>()).ToArray();
}
