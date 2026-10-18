using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Players' canonical dump section (ADR-0006 section 4, ADR-0013 section 2): a <c>u32</c> count of present players, then for each
/// in ascending slot order the slot, <c>playerId</c>, life state and deadlines, <c>TeleportSeq</c>, aim, and every
/// <see cref="CarrierState"/> field written one by one (never a struct blit), the seven push pairs, then <c>WadeSlowPm</c> (story 009, appended); then the anchor registry (story 012, appended: <c>u32</c> count and the anchors in
/// ascending index, see <see cref="AnchorRegistry.Dump"/>). The capsule size is not
/// written (derived from the settings). Between ticks only; allocation-free.
/// </summary>
/// <example><code>builder.BindStateContributor(new PlayersStateContributor(players));</code></example>
internal sealed class PlayersStateContributor : ISimStateContributor
{
    private readonly PlayersSystem _players;

    public PlayersStateContributor(PlayersSystem players)
    {
        _players = players;
    }

    public ushort SectionId => SectionIds.Players;

    public void Write(ref CanonicalWriter w)
    {
        uint count = 0;
        for (byte s = 0; s < PlayerSlot.Count; s++)
        {
            if (!_players.RecordRef(PlayerSlot.FromValue(s)).IsEmpty)
            {
                count++;
            }
        }

        w.WriteU32(count);
        for (byte s = 0; s < PlayerSlot.Count; s++)
        {
            ref readonly PlayerRecord r = ref _players.RecordRef(PlayerSlot.FromValue(s));
            if (r.IsEmpty)
            {
                continue;
            }

            w.WriteU8(s);
            w.WriteU32(r.PlayerId.Value);
            w.WriteU8((byte)r.Life);
            w.WriteU32(r.ClockInTick);
            w.WriteU32(r.FrozenUntilTick);
            w.WriteU32(r.ProtectedUntilTick);
            w.WriteU8(r.TeleportSeq);
            w.WriteU16(r.LookYaw);
            w.WriteU16(unchecked((ushort)r.LookPitch));
            WriteCarrier(ref w, in r.Carrier);
        }

        _players.Anchors.Dump(ref w);   // story 012: the anchor registry, appended after the per-player block
    }

    private static void WriteCarrier(ref CanonicalWriter w, in CarrierState c)
    {
        w.WriteI32(c.X);
        w.WriteI32(c.Y);
        w.WriteI32(c.Z);
        w.WriteI32(c.RemX);
        w.WriteI32(c.RemZ);
        w.WriteI32(c.RemY);
        w.WriteI32(c.Vx);
        w.WriteI32(c.Vz);
        w.WriteI32(c.Vy);
        w.WriteU16(c.BodyYaw);
        w.WriteU8((byte)c.Loco);
        w.WriteU16((ushort)c.Flags);
        w.WriteU8(c.CoyoteLeft);
        w.WriteU8(c.JumpBufferLeft);
        w.WriteU8(c.HaulOutTicks);
        w.WriteU32(c.SupportId);
        w.WriteI32(c.SupportLocalX);
        w.WriteI32(c.SupportLocalY);
        w.WriteI32(c.SupportLocalZ);
        w.WriteU16(c.SupportYawRef);
        for (int i = 0; i < PushPairArray.Length; i++)
        {
            ref readonly PushPairState p = ref c.Pair[i];
            w.WriteU16(p.OverlapTicks);
            w.WriteI32(p.WindowStartX);
            w.WriteI32(p.WindowStartZ);
            w.WriteU8(p.Ghosted);
        }

        w.WriteU16(c.WadeSlowPm);   // story 009: appended after the pairs (additive layout)
    }
}
