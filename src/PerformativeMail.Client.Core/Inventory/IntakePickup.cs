using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Players.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Inventory;

/// <summary>
/// What E at the Intake does while the inventory panel stays closed (WP-F; Evan's panel card t316s1406 is open): the press itself sends
/// the Intake's <c>Open</c> (the client's Open anchor). When that <c>Open</c> is answered ok, this step sends <c>take_all</c> of the
/// street's district (GDD R7: oldest first, into the panel — mail goes to the player_inventory first, R9), then <c>Close</c>; if the
/// hotbar then holds no mail, it quick-moves the first player_inventory mail entry to the hotbar (R9: inventory, nothing open →
/// hotbar) and, once that reply is back, selects the slot it landed in (<see cref="IInventoryActions.SelectHotbar"/>), so the letter is
/// in hand for a throw. Each step waits for the previous reply (the mirror has applied the records by then: event before reply). Between
/// pickups the same staging runs whenever the mirror changes and leaves the hotbar without mail while the player_inventory still holds
/// some (a throw or a delivery took the last hotbar letter; WP-H), so the next letter is in hand without going back to the Intake. One
/// step owns both so the pickup's staging and the restage never both send. Binds as a mirror part (replies) and as a plain Previews
/// step (sends). Allocation-free; client thread only.
/// </summary>
/// <example>
/// <code>
/// var pickup = new IntakePickup(mirror, requests, selector, ContainerIdPlan.Intake, district: 1);
/// builder.BindMirrorPart(EventKindSet.Of(), pickup);
/// builder.Bind(ClientStepId.Previews, pickup);
/// </code>
/// </example>
public sealed class IntakePickup : IClientStep, IMirrorPart
{
    private readonly OwnPanelMirror _mirror;
    private readonly IRequestSink _requests;
    private readonly IInventoryActions _select;
    private readonly ContainerId _intake;
    private readonly uint _district;
    private Phase _phase;
    private EntryId _staged;
    private uint _seenVersion;

    /// <summary>Creates the step.</summary>
    /// <param name="mirror">The own-panel mirror.</param>
    /// <param name="requests">The request port.</param>
    /// <param name="select">The hotbar selection sender.</param>
    /// <param name="intake">The Intake container.</param>
    /// <param name="district">The district <c>take_all</c> filters on (the street's).</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    public IntakePickup(OwnPanelMirror mirror, IRequestSink requests, IInventoryActions select, ContainerId intake, uint district)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(select);
        _mirror = mirror;
        _requests = requests;
        _select = select;
        _intake = intake;
        _district = district;
    }

    private enum Phase : byte
    {
        Idle = 0,
        TakeAll = 1,
        AwaitTakeAll = 2,
        CloseAndStage = 3,
        AwaitStage = 4,
        Select = 5,
    }

    /// <summary>Completed pickups (a <c>take_all</c> answered ok).</summary>
    public int Pickups { get; private set; }

    /// <summary>The last <c>take_all</c> reply's reason (0 ok).</summary>
    public ushort LastTakeAllReason { get; private set; }

    /// <inheritdoc />
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
    }

    /// <inheritdoc />
    public void ApplyReply(in Reply r)
    {
        switch (r.Kind)
        {
            case PlayerOpKinds.Open when r.IsOk && _phase == Phase.Idle:
                _phase = Phase.TakeAll;
                break;
            case InvOpKinds.TakeAll when _phase == Phase.AwaitTakeAll:
                LastTakeAllReason = r.Reason;
                Pickups += r.IsOk ? 1 : 0;
                _phase = Phase.CloseAndStage;
                break;
            case InvOpKinds.QuickMove when _phase == Phase.AwaitStage:
                _phase = r.IsOk ? Phase.Select : Phase.Idle;
                break;
            default:
                break;
        }
    }

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        switch (_phase)
        {
            case Phase.Idle when _mirror.Version != _seenVersion:
                _seenVersion = _mirror.Version;
                if (!_mirror.HotbarHasMail())
                {
                    Stage(_mirror.FirstInventoryMail());
                }

                break;
            case Phase.TakeAll:
                var take = new Op { Kind = InvOpKinds.TakeAll };
                take.SetPayload(new TakeAllPayload { Container = _intake.Value, FilterKind = (byte)TakeAllFilter.District, FilterValue = _district });
                if (_requests.TrySend(in take))
                {
                    _phase = Phase.AwaitTakeAll;
                }

                break;
            case Phase.CloseAndStage:
                var close = new Op { Kind = InvOpKinds.Close };
                close.SetPayload(new ClosePayload { Container = _intake.Value });
                if (!_requests.TrySend(in close))
                {
                    break;
                }

                _phase = Phase.Idle;
                Stage(_mirror.HotbarHasMail() ? EntryId.None : _mirror.FirstInventoryMail());
                break;
            case Phase.Select:
                int slot = _mirror.HotbarSlotOf(_staged);
                if (slot > 0)
                {
                    _ = _select.SelectHotbar(slot);
                }

                _phase = Phase.Idle;
                break;
            default:
                break;
        }
    }

    // Quick-moves the player_inventory entry to the hotbar (R9: inventory, nothing open → hotbar); Select follows its ok reply.
    private void Stage(EntryId entry)
    {
        _staged = entry;
        if (entry.IsNone)
        {
            return;
        }

        var move = new Op { Kind = InvOpKinds.QuickMove };
        move.SetPayload(new QuickMovePayload { From = _mirror.InventoryId.Value, EntryId = entry.Value });
        _phase = _requests.TrySend(in move) ? Phase.AwaitStage : Phase.Idle;
    }
}
