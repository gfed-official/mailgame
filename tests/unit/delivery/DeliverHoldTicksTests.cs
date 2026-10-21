using System.Reflection;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Covers manual-delivery story 001 AC-2 and AC-3 (GDD M-F2-1, M-MD9-1; TR-delivery-015; ADR-0014 section 4, ADR-0010 section 7):
/// <see cref="DeliverInteractable.GetHoldTicks"/> is the shared <see cref="Hold.HoldTicks(int, int, int, in HoldSettings)"/> fed with
/// the actor's <see cref="Stat.DeliverInsertPm"/>; the stat's 0 to 750 clamp holds through the real <see cref="StatSheet.Build"/>; and
/// nothing is cached between calls. A stub <see cref="IStatSheet"/> supplies the stat elsewhere. That a latched hold ignores a later
/// change is player-character story 016's (M-MD9-1 in <c>PressResolutionTests.cs</c>).
/// </summary>
public class DeliverHoldTicksTests
{
    private const int HouseHoldMs = 400;
    private const int QuickHandsPm = 500;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();
    private static readonly PlayerSlot _actor = PlayerSlot.FromValue(2);

    // ---------------------------------------------------------------- helpers

    private static HoldSettings DefaultHoldSettings() =>
        HoldSettings.FromPlayerSettings(PlayerSettings.FromBalance(_content.Player));

    private static InteractionContext Context(int holdBaseMs, int holdSettingMs, uint destination = 1) => new()
    {
        Tick = 30,
        Anchor = DestinationAnchor(destination),
        Actor = _actor,
        Verb = Verb.Deliver,
        Mode = InteractMode.Hold,
        HoldBaseMs = holdBaseMs,
        HoldSettingMs = holdSettingMs,
        HoldSettings = DefaultHoldSettings(),
    };

    private static AnchorRef DestinationAnchor(uint destination) => new(EntityId.Compose(EntityClass.Destination, destination), 0);

    private static InteractionLatch Latch(uint destination) => new()
    {
        Anchor = DestinationAnchor(destination),
        Verb = Verb.Deliver,
        Mode = InteractMode.Hold,
        PressTick = 30,
    };

    private static Modifier QuickHands(PlayerSlot slot, ushort source) =>
        new(SourceKind.Perk, DefIndex.FromValue(source), 0, slot, Stat.DeliverInsertPm, StatSheet.NoQualifier, ModifierOp.Add, QuickHandsPm, ModifierLifetime.Run);

    private static int Ticks(int insertPm, int holdBaseMs, int holdSettingMs)
    {
        var impl = new DeliverInteractable(new StubStatSheet { InsertPm = insertPm });
        InteractionContext c = Context(holdBaseMs, holdSettingMs);
        InteractionLatch latch = Latch(1);
        return impl.GetHoldTicks(in latch, in c);
    }

    /// <summary>
    /// A test stat sheet: answers <see cref="Stat.DeliverInsertPm"/> for <see cref="_actor"/> from a settable field (0 for any other
    /// stat or slot) and records the last slot read. The field stands in for the sheet's "current value"; the hooks never write it.
    /// </summary>
    private sealed class StubStatSheet : IStatSheet
    {
        public int InsertPm { get; set; }

        public PlayerSlot? LastSlot { get; private set; }

        public uint Version => 0;

        public int Read(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0)
        {
            LastSlot = slot;
            return stat == Stat.DeliverInsertPm && qualifier == StatSheet.NoQualifier && slot == _actor ? InsertPm : 0;
        }

        public StatExplain Explain(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0) =>
            throw new NotSupportedException();

        public bool Has(RuleFlag flag, PlayerSlot? slot = null) => false;
    }

    // ---------------------------------------------------------------- M-F2-1

    /// <summary>The M-F2-1 table: hold setting to ticks, no perk and Quick Hands.</summary>
    [Theory]
    [Trait("Id", "M-F2-1")]
    [InlineData(200, 6, 6)]
    [InlineData(250, 8, 6)]
    [InlineData(400, 12, 6)]
    [InlineData(600, 18, 9)]
    [InlineData(1000, 30, 15)]
    public void GetHoldTicks_HouseBaseBySetting_MatchesTheF2Table_AndEqualsHoldHoldTicks(int settingMs, int noPerk, int quickHands)
    {
        // Arrange
        HoldSettings s = DefaultHoldSettings();

        // Act
        int plain = Ticks(0, HouseHoldMs, settingMs);
        int fast = Ticks(QuickHandsPm, HouseHoldMs, settingMs);

        // Assert
        Assert.Equal((noPerk, quickHands), (plain, fast));
        Assert.Equal(Hold.HoldTicks(HouseHoldMs, 0, settingMs, in s), plain);
        Assert.Equal(Hold.HoldTicks(HouseHoldMs, QuickHandsPm, settingMs, in s), fast);
    }

    [Fact]
    [Trait("Id", "M-F2-1")]
    public void GetHoldTicks_1500MsBaseAtTheOneSecondSetting_Is60Ticks()
    {
        // Arrange
        HoldSettings s = DefaultHoldSettings();

        // Act
        int ticks = Ticks(0, 1500, 1000);

        // Assert
        Assert.Equal(60, ticks);   // ceil(1500 * 1000 * 30 / (400 * 1000)) = ceil(112.5) = 113, held to the 2000 ms ceiling's 60
        Assert.Equal(Hold.HoldTicks(1500, 0, 1000, in s), ticks);
    }

    [Fact]
    [Trait("Id", "M-F2-1")]
    public void GetHoldTicks_ReadsDeliverInsertPm_ForTheActorsSlot()
    {
        // Arrange
        var stats = new StubStatSheet { InsertPm = QuickHandsPm };
        var impl = new DeliverInteractable(stats);
        InteractionContext c = Context(HouseHoldMs, 400);
        InteractionLatch latch = Latch(1);

        // Act
        int ticks = impl.GetHoldTicks(in latch, in c);

        // Assert
        Assert.Equal(6, ticks);
        Assert.Equal(_actor, stats.LastSlot);
    }

    // ---------------------------------------------------------------- M-MD9-1

    /// <summary>A MATCH and a MISDELIVER_ONE target are both 6 ticks with Quick Hands, at the default setting and at 0.2 s.</summary>
    [Theory]
    [Trait("Id", "M-MD9-1")]
    [InlineData(DefaultClientInputSettings.DefaultHoldSettingMs)]
    [InlineData(200)]
    public void GetHoldTicks_QuickHands_MatchAndMisdeliverOneHolds_AreBothSixTicks(int settingMs)
    {
        // Arrange (two destinations of the same type: the hold depends on the anchor's base, never on the outcome the press
        // would have; the latch carries no outcome until story 004's OnPress payload)
        var impl = new DeliverInteractable(new StubStatSheet { InsertPm = QuickHandsPm });
        InteractionContext matchContext = Context(HouseHoldMs, settingMs, destination: 1);
        InteractionContext misdeliverContext = Context(HouseHoldMs, settingMs, destination: 2);
        InteractionLatch match = Latch(1);
        InteractionLatch misdeliverOne = Latch(2);

        // Act
        int matchTicks = impl.GetHoldTicks(in match, in matchContext);
        int misdeliverTicks = impl.GetHoldTicks(in misdeliverOne, in misdeliverContext);

        // Assert
        Assert.Equal((6, 6), (matchTicks, misdeliverTicks));
    }

    [Fact]
    [Trait("Id", "M-MD9-1")]
    public void StatSheetBuild_TwoQuickHandsSumming1000_ClampsDeliverInsertPmTo750_AndGetHoldTicksUsesIt()
    {
        // Arrange
        IStatSheet sheet = StatSheet.Build(_content, new StatSources([QuickHands(_actor, 0), QuickHands(_actor, 1)]));
        var impl = new DeliverInteractable(sheet);
        InteractionContext c = Context(HouseHoldMs, 1000);
        InteractionLatch latch = Latch(1);
        HoldSettings s = DefaultHoldSettings();

        // Act
        int insertPm = sheet.Read(Stat.DeliverInsertPm, StatSheet.NoQualifier, _actor);
        int ticks = impl.GetHoldTicks(in latch, in c);

        // Assert
        Assert.Equal(750, insertPm);   // 2 x 500 = 1000 summed, clamped to the StatDef's 750
        Assert.Equal(Hold.HoldTicks(HouseHoldMs, 750, 1000, in s), ticks);
        Assert.Equal(8, ticks);   // ceil(400 * 250 * 1000 * 30 / (400 * 1000 * 1000)) = ceil(7.5)
    }

    [Fact]
    [Trait("Id", "M-MD9-1")]
    public void StatSheetBuild_NoQuickHands_ReadsZero_AndAnotherSlotsPerkDoesNotApply()
    {
        // Arrange
        IStatSheet sheet = StatSheet.Build(_content, new StatSources([QuickHands(PlayerSlot.FromValue(5), 0)]));
        var impl = new DeliverInteractable(sheet);
        InteractionContext c = Context(HouseHoldMs, 400);
        InteractionLatch latch = Latch(1);

        // Act
        int ticks = impl.GetHoldTicks(in latch, in c);

        // Assert
        Assert.Equal(0, sheet.Read(Stat.DeliverInsertPm, StatSheet.NoQualifier, _actor));
        Assert.Equal(12, ticks);
    }

    [Fact]
    [Trait("Id", "M-MD9-1")]
    public void GetHoldTicks_StatChangedBetweenTwoCalls_ReturnsTheNewResult()
    {
        // Arrange
        var stats = new StubStatSheet { InsertPm = 0 };
        var impl = new DeliverInteractable(stats);
        InteractionContext c = Context(HouseHoldMs, 600);
        InteractionLatch latch = Latch(1);

        // Act
        int before = impl.GetHoldTicks(in latch, in c);
        stats.InsertPm = QuickHandsPm;
        int after = impl.GetHoldTicks(in latch, in c);
        stats.InsertPm = 0;
        int back = impl.GetHoldTicks(in latch, in c);

        // Assert
        Assert.Equal((18, 9, 18), (before, after, back));
    }

    [Fact]
    [Trait("Id", "M-MD9-1")]
    public void DeliverInteractable_HoldsOnlyTheStatSheet_SoNothingIsCached()
    {
        // Arrange
        const BindingFlags All =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        // Act
        FieldInfo[] fields = typeof(DeliverInteractable).GetFields(All);

        // Assert
        FieldInfo only = Assert.Single(fields);
        Assert.Equal(typeof(IStatSheet), only.FieldType);
        Assert.True(only.IsInitOnly);
    }

    // ---------------------------------------------------------------- stub hooks (stories 003, 004, 010 fill them)

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void StubHooks_AreDeterministic_UntilStories003_004_010()
    {
        // Arrange
        var impl = new DeliverInteractable(new StubStatSheet());
        InteractionContext c = Context(HouseHoldMs, 400);
        InteractionLatch latch = Latch(1);
        InteractionLatch pressed = latch;
        var op = default(Op);
        var prompt = default(PromptArgs);

        // Act
        bool available = impl.IsAvailable(in c, out StringId availableReason);
        bool cancel = impl.ShouldCancel(in latch, in c, out StringId cancelReason);
        bool built = impl.TryBuildRequest(in latch, in c, ref op);
        impl.OnPress(in c, ref pressed);
        impl.WritePrompt(in c, ref prompt);

        // Assert
        Assert.False(available);
        Assert.True(availableReason.IsNone);
        Assert.False(cancel);
        Assert.True(cancelReason.IsNone);
        Assert.False(built);
        Assert.Equal(default(Op).Kind, op.Kind);
        Assert.Equal(latch.Anchor, pressed.Anchor);
        Assert.Equal(latch.PressTick, pressed.PressTick);
    }
}
