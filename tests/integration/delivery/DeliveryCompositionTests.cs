using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.ManualDelivery;

/// <summary>
/// Manual-delivery story 005 AC-7 (from story 001; MD1; ADR-0003 section 4): the <c>ManualDelivery</c> case of
/// <see cref="SimComposition"/> builds the <see cref="DeliverySystem"/> and registers one Deliver anchor per destination while the world
/// is Constructed, and a failed registration is a <see cref="SimBuildException"/> naming the <see cref="RegisterResult"/>.
/// </summary>
public class DeliveryCompositionTests
{
    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void Ac7_M1aGreybox_BuildsDelivery_AndRegistersExactlyTenDeliverAnchors_OnePerDestinationAtSlot0()
    {
        // Act
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 7) { WorldSource = M1aWorldSource.Instance });

        // Assert
        DeliverySystem delivery = Assert.Single(host.Systems.OfType<DeliverySystem>());
        PlayersSystem players = Assert.Single(host.Systems.OfType<PlayersSystem>());
        MailSystem mail = Assert.Single(host.Systems.OfType<MailSystem>());
        AnchorDef[] deliver = Enumerable.Range(0, players.Anchors.Count)
            .Select(i => players.Anchors.DefAt(i))
            .Where(d => d.Verb == Verb.Deliver)
            .ToArray();
        Assert.Equal(10, deliver.Length);
        Assert.Equal(
            Enumerable.Range(0, 10).Select(i => new AnchorRef(mail.Registry.EntityIdOf(new DestinationOrdinal((ushort)i)), 0)).ToArray(),
            deliver.Select(d => d.Ref).ToArray());
        Assert.All(deliver, d => Assert.Equal(InteractMode.Hold, d.Mode));
        Assert.Equal(mail.Registry.Count, delivery.Registry.Count);
    }

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void Ac7_APreRegisteredDeliverVerbOnOneMailbox_MakesBuildThrowTheDeliveryCode_NamingDuplicateVerb()
    {
        // Arrange: Players pre-registers Verb.Deliver at destination row 3's AnchorRef(destId, 0) before Delivery's case runs.
        SimProfile profile = SimProfile.ForTest(
            "md005-dup",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx => new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
            new SimSystemEntry(SystemId.PlayerCharacter, ctx =>
            {
                var players = new PlayersSystem(ctx.World!, ctx.Content, ctx.Samples, ctx.Slots, ctx.Tick, ctx.CheckRing, ctx.Counters, ctx.Api<IInventoryApi>(SystemId.GridInventory));
                var registry = new DestinationRegistry(ctx.World!, ctx.Content);
                var row3 = new DestinationOrdinal(3);
                AnchorDef taken = AnchorDef.For(new AnchorRef(registry.EntityIdOf(row3), 0), registry.AnchorOf(row3), Verb.Deliver);
                Assert.Equal(RegisterResult.Ok, players.RegisterAnchor(in taken, new DeliverInteractable(ctx.StatSheet)));
                return players;
            }),
            new SimSystemEntry(SystemId.MailDestinations, ctx => new MailSystem(
                ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), new DeliverRig.StubWallet(),
                ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots)),
            new SimSystemEntry(SystemId.ManualDelivery, ctx => SimComposition.ConstructManualDelivery(ctx)!));

        // Act
        SimBuildException e = Assert.Throws<SimBuildException>(
            () => SimComposition.Build(new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance }));

        // Assert
        Assert.Equal(SimBuildErrorCode.DeliveryAnchorRegistrationFailed, e.Code);
        Assert.Contains(nameof(RegisterResult.DuplicateVerb), e.Message, StringComparison.Ordinal);
    }
}
